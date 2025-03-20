using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for shared map operations that are common between tournament and casual contexts
    /// </summary>
    public interface IMapService
    {
        /// <summary>
        /// Gets a map by name
        /// </summary>
        /// <param name="mapName">The name of the map to get</param>
        /// <returns>The map if found, or null if not found</returns>
        Map? GetMapByName(string mapName);

        /// <summary>
        /// Creates an embed for a map with optional description
        /// </summary>
        /// <param name="map">The map to create an embed for</param>
        /// <param name="description">Optional description for the embed</param>
        /// <returns>A Discord embed builder with the map information</returns>
        DiscordEmbedBuilder CreateMapEmbed(Map map, string? description = null);

        /// <summary>
        /// Gets map thumbnail data asynchronously
        /// </summary>
        /// <param name="mapName">The name of the map to get thumbnail for</param>
        /// <returns>A tuple containing the URL and raw data (if a local file)</returns>
        Task<(string? url, byte[]? data)> GetMapThumbnailAsync(string mapName);

        /// <summary>
        /// Normalizes a thumbnail path for cross-platform compatibility
        /// </summary>
        /// <param name="thumbnailPath">The original thumbnail path</param>
        /// <returns>A normalized path that works on the current platform</returns>
        string NormalizeThumbnailPath(string thumbnailPath);

        /// <summary>
        /// Sends a map embed with thumbnail to a channel
        /// </summary>
        /// <param name="channel">The channel to send to</param>
        /// <param name="map">The map to display</param>
        /// <param name="description">Optional description</param>
        /// <param name="tempDisplaySeconds">Optional time in seconds to display before deleting (0 means no deletion)</param>
        /// <returns>The sent message</returns>
        Task<DiscordMessage> SendMapEmbedAsync(DiscordChannel channel, Map map, string? description = null, int tempDisplaySeconds = 0);

        /// <summary>
        /// Checks if a map exists in a given pool
        /// </summary>
        /// <param name="mapName">The map name to check</param>
        /// <param name="isInTournamentPool">True to check tournament pool, false to check random pool</param>
        /// <returns>True if the map exists in the specified pool</returns>
        bool IsMapInPool(string mapName, bool isInTournamentPool);

        /// <summary>
        /// Validates a map name to ensure it exists in the collection
        /// </summary>
        /// <param name="mapName">The map name to validate</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if valid, false otherwise</returns>
        bool ValidateMap(string mapName, out string? errorMessage);

        /// <summary>
        /// Gets a list of map names that match the specified pool type and size
        /// </summary>
        /// <param name="isInTournamentPool">True to get tournament maps, false for casual maps</param>
        /// <param name="mapSize">The map size to filter by (e.g., "1v1" or "2v2")</param>
        /// <returns>A list of map names that match the criteria</returns>
        List<string> GetMapsByPoolTypeAndSize(bool isInTournamentPool, string mapSize);
    }
}