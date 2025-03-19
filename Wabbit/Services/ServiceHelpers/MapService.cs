using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Data;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Implementation of the IMapService interface for shared map operations
    /// </summary>
    public class MapService : IMapService
    {
        private readonly ILogger<MapService> _logger;

        /// <summary>
        /// Constructor for MapService
        /// </summary>
        /// <param name="logger">Logger for logging information and errors</param>
        public MapService(ILogger<MapService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Map? GetMapByName(string mapName)
        {
            try
            {
                if (Maps.MapCollection == null)
                {
                    _logger.LogError("Map collection is null in GetMapByName");
                    return null;
                }

                return Maps.MapCollection.FirstOrDefault(m =>
                    string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting map by name: {MapName}", mapName);
                return null;
            }
        }

        /// <inheritdoc />
        public DiscordEmbedBuilder CreateMapEmbed(Map map, string? description = null)
        {
            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle(map.Name)
                .WithDescription(description ?? MapUtilities.GetMapDescription(map))
                .WithColor(DiscordColor.Orange)
                .AddField("Size", map.Size ?? "Unknown", true)
                .AddField("In Tournament Pool", map.IsInTournamentPool ? "Yes" : "No", true)
                .AddField("In Random Pool", map.IsInRandomPool ? "Yes" : "No", true);

            return embedBuilder;
        }

        /// <inheritdoc />
        public async Task<(string? url, byte[]? data)> GetMapThumbnailAsync(string mapName)
        {
            try
            {
                var map = GetMapByName(mapName);
                if (map == null || string.IsNullOrEmpty(map.Thumbnail))
                {
                    return (null, null);
                }

                // Check if it's a URL or local file
                if (Uri.TryCreate(map.Thumbnail, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    // It's a URL
                    return (map.Thumbnail, null);
                }
                else
                {
                    // It's a local file
                    string normalizedPath = NormalizeThumbnailPath(map.Thumbnail);
                    if (!File.Exists(normalizedPath))
                    {
                        _logger.LogWarning("Thumbnail file not found: {FilePath}", normalizedPath);
                        return (null, null);
                    }

                    byte[] fileData = await File.ReadAllBytesAsync(normalizedPath);
                    return (null, fileData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting thumbnail for map: {MapName}", mapName);
                return (null, null);
            }
        }

        /// <inheritdoc />
        public string NormalizeThumbnailPath(string thumbnailPath)
        {
            if (string.IsNullOrEmpty(thumbnailPath))
            {
                return thumbnailPath;
            }

            // Replace forward slashes with the correct directory separator for the current platform
            string normalizedPath = thumbnailPath.Replace('/', Path.DirectorySeparatorChar)
                                               .Replace('\\', Path.DirectorySeparatorChar);

            // If the path is not absolute, make it relative to the application directory
            if (!Path.IsPathRooted(normalizedPath))
            {
                normalizedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalizedPath);
            }

            return normalizedPath;
        }

        /// <inheritdoc />
        public async Task<DiscordMessage> SendMapEmbedAsync(DiscordChannel channel, Map map, string? description = null, int tempDisplaySeconds = 0)
        {
            try
            {
                var embed = CreateMapEmbed(map, description);
                var (url, data) = await GetMapThumbnailAsync(map.Name);

                DiscordMessage message;
                if (url != null)
                {
                    // URL thumbnail
                    embed.WithImageUrl(url);
                    message = await channel.SendMessageAsync(embed: embed);
                }
                else if (data != null)
                {
                    // Local file thumbnail
                    using var ms = new MemoryStream(data);
                    var fileName = Path.GetFileName(map.Thumbnail) ?? "map_thumbnail.png";

                    // Create a message builder with the embed
                    var messageBuilder = new DiscordMessageBuilder()
                        .AddEmbed(embed);

                    // Add the file as an attachment
                    messageBuilder.AddFile(fileName, ms);
                    embed.WithImageUrl($"attachment://{fileName}");

                    message = await channel.SendMessageAsync(messageBuilder);
                }
                else
                {
                    // No thumbnail
                    message = await channel.SendMessageAsync(embed: embed);
                }

                // Set up auto-deletion if requested
                if (tempDisplaySeconds > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(tempDisplaySeconds));
                        try
                        {
                            await message.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete temporary map message");
                        }
                    });
                }

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending map embed for map: {MapName}", map.Name);
                throw;
            }
        }

        /// <inheritdoc />
        public bool IsMapInPool(string mapName, bool isInTournamentPool)
        {
            var map = GetMapByName(mapName);
            if (map == null)
            {
                return false;
            }

            return isInTournamentPool ? map.IsInTournamentPool : map.IsInRandomPool;
        }

        /// <inheritdoc />
        public bool ValidateMap(string mapName, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(mapName))
            {
                errorMessage = "Map name cannot be empty.";
                return false;
            }

            var map = GetMapByName(mapName);
            if (map == null)
            {
                errorMessage = $"Map '{mapName}' does not exist in the map collection.";
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Static utility methods for map operations
    /// </summary>
    public static class MapUtilities
    {
        /// <summary>
        /// Gets the full file path for a map thumbnail
        /// </summary>
        /// <param name="map">The map to get the thumbnail path for</param>
        /// <returns>The full thumbnail path, or null if the map has no thumbnail</returns>
        public static string? GetThumbnailPath(Map map)
        {
            if (string.IsNullOrEmpty(map.Thumbnail))
            {
                return null;
            }

            // Check if it's a URL
            if (Uri.TryCreate(map.Thumbnail, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return map.Thumbnail;
            }

            // It's a local file, normalize the path
            string normalizedPath = map.Thumbnail
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            // If the path is not absolute, make it relative to the application directory
            if (!Path.IsPathRooted(normalizedPath))
            {
                normalizedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalizedPath);
            }

            return normalizedPath;
        }

        /// <summary>
        /// Checks if a map exists in the map collection
        /// </summary>
        /// <param name="mapName">The name of the map to check</param>
        /// <param name="mapCollection">The map collection to check in</param>
        /// <returns>True if the map exists, false otherwise</returns>
        public static bool MapExists(string mapName)
        {
            if (Maps.MapCollection == null)
            {
                return false;
            }

            return Maps.MapCollection.Any(m =>
                string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all maps with a specific size
        /// </summary>
        /// <param name="size">The size to filter by</param>
        /// <returns>A list of maps with the specified size</returns>
        public static List<Map> GetMapsBySize(string size)
        {
            if (Maps.MapCollection == null)
            {
                return new List<Map>();
            }

            return Maps.MapCollection
                .Where(m => string.Equals(m.Size, size, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Gets a formatted description for a map
        /// </summary>
        /// <param name="map">The map to get a description for</param>
        /// <returns>A formatted description string</returns>
        public static string GetMapDescription(Map map)
        {
            return $"Map Size: {map.Size}";
        }
    }
}