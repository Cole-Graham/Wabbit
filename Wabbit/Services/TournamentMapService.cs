using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Data;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;
using Wabbit.Services.ServiceHelpers;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of ITournamentMapService for map operations
    /// </summary>
    public class TournamentMapService : ITournamentMapService
    {
        private readonly ILogger<TournamentMapService> _logger;
        private readonly IRandomProvider _randomProvider;
        private readonly IMapService _mapService;

        // Minimum number of maps required for a valid tournament
        private const int MinimumRequiredMaps = 5;
        // Fallback map names if no maps are available
        private readonly List<string> _fallbackMaps = new() { "Default Map", "Fallback Map 1", "Fallback Map 2" };

        /// <summary>
        /// Constructor
        /// </summary>
        public TournamentMapService(
            ILogger<TournamentMapService> logger,
            IRandomProvider randomProvider,
            IMapService mapService)
        {
            _logger = logger;
            _randomProvider = randomProvider;
            _mapService = mapService;
        }

        /// <summary>
        /// Gets the tournament map pool
        /// </summary>
        public List<string> GetTournamentMapPool(bool oneVOne)
        {
            try
            {
                string mapSize = oneVOne ? "1v1" : "2v2";

                // Check if MapCollection is properly initialized
                if (Maps.MapCollection == null)
                {
                    _logger.LogError("Map collection is null. Returning fallback maps.");
                    return _fallbackMaps.ToList();
                }

                var mapPool = Maps.MapCollection
                    .Where(m =>
                        m != null &&
                        !string.IsNullOrEmpty(m.Name) &&
                        m.Size == mapSize &&
                        m.IsInTournamentPool)
                    .Select(m => m.Name)
                    .ToList();

                // Log the map pool for debugging
                _logger.LogInformation($"Retrieved tournament map pool for {mapSize}: {mapPool.Count} maps");

                // If map pool is too small, log a warning
                if (mapPool.Count < MinimumRequiredMaps)
                {
                    _logger.LogWarning($"Tournament map pool for {mapSize} contains only {mapPool.Count} maps, which is below the recommended minimum of {MinimumRequiredMaps}");
                }

                // Return the map pool, or fallback maps if empty
                return mapPool.Count > 0 ? mapPool : _fallbackMaps.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tournament map pool");
                return _fallbackMaps.ToList();
            }
        }

        /// <summary>
        /// Gets a random map
        /// </summary>
        public string GetRandomMap(bool oneVOne)
        {
            try
            {
                var mapPool = GetTournamentMapPool(oneVOne);
                if (mapPool.Count == 0)
                {
                    _logger.LogWarning("Map pool is empty, returning Default Map");
                    return "Default Map";
                }

                int randomIndex = _randomProvider.Instance.Next(mapPool.Count);
                return mapPool[randomIndex];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map");
                return "Default Map";
            }
        }

        /// <summary>
        /// Gets multiple random maps
        /// </summary>
        public List<string> GetRandomMaps(bool oneVOne, int count)
        {
            try
            {
                if (count <= 0)
                {
                    _logger.LogWarning($"Invalid map count requested: {count}. Using 1 instead.");
                    count = 1;
                }

                var mapPool = GetTournamentMapPool(oneVOne);
                if (mapPool.Count == 0)
                {
                    _logger.LogWarning("Map pool is empty");
                    return Enumerable.Repeat("Default Map", count).ToList();
                }

                // If count is greater than the map pool, handle this gracefully
                if (count >= mapPool.Count)
                {
                    _logger.LogWarning($"Requested {count} maps but only {mapPool.Count} are available. Using all available maps.");
                    return mapPool.ToList();
                }

                // Shuffle the map pool and take the requested number of maps
                var shuffledMaps = mapPool.OrderBy(_ => _randomProvider.Instance.Next()).Take(count).ToList();
                return shuffledMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random maps");
                return Enumerable.Repeat("Default Map", count).ToList();
            }
        }

        /// <summary>
        /// Processes map bans and generates a map list
        /// </summary>
        public List<string> GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength, Round? round = null)
        {
            try
            {
                // Validate inputs
                team1Bans = team1Bans ?? new List<string>();
                team2Bans = team2Bans ?? new List<string>();

                if (matchLength <= 0)
                {
                    _logger.LogWarning($"Invalid match length: {matchLength}. Using 1 instead.");
                    matchLength = 1;
                }

                var mapPool = GetTournamentMapPool(oneVOne);
                if (mapPool.Count == 0)
                {
                    _logger.LogWarning("Map pool is empty");
                    return Enumerable.Repeat("Default Map", matchLength).ToList();
                }

                // Log ban information for debugging
                _logger.LogInformation($"Processing map bans: Team 1 ({team1Bans.Count} bans), Team 2 ({team2Bans.Count} bans)");

                // Process bans - remove banned maps from the pool
                var availableMaps = mapPool.ToList();

                // Validate ban lists to ensure they only contain valid maps
                var validTeam1Bans = team1Bans.Where(m => mapPool.Contains(m)).ToList();
                var validTeam2Bans = team2Bans.Where(m => mapPool.Contains(m)).ToList();

                if (validTeam1Bans.Count != team1Bans.Count)
                {
                    _logger.LogWarning($"Team 1 has {team1Bans.Count - validTeam1Bans.Count} invalid map bans");
                }

                if (validTeam2Bans.Count != team2Bans.Count)
                {
                    _logger.LogWarning($"Team 2 has {team2Bans.Count - validTeam2Bans.Count} invalid map bans");
                }

                // Check for conditional bans based on match length
                if (round != null && round.CoinflipPerformed && matchLength > 1)
                {
                    string? team1Name = round.Teams.Count > 0 ? round.Teams[0].Name : null;
                    string? team2Name = round.Teams.Count > 1 ? round.Teams[1].Name : null;

                    // Get winning team (if it exists)
                    bool team1Won = string.Equals(team1Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);
                    bool team2Won = string.Equals(team2Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);

                    if (matchLength == 3) // Bo3 match
                    {
                        // For Bo3, only apply priority 3 bans for the team that won the coinflip
                        if (validTeam1Bans.Count >= 3 && !team1Won)
                        {
                            // Remove priority 3 ban from team 1's bans if they lost the coinflip
                            validTeam1Bans.RemoveAt(2);
                            _logger.LogInformation("Removed conditional (priority 3) ban from Team 1 as they lost the coinflip");
                        }

                        if (validTeam2Bans.Count >= 3 && !team2Won)
                        {
                            // Remove priority 3 ban from team 2's bans if they lost the coinflip
                            validTeam2Bans.RemoveAt(2);
                            _logger.LogInformation("Removed conditional (priority 3) ban from Team 2 as they lost the coinflip");
                        }
                    }
                    else if (matchLength == 5) // Bo5 match
                    {
                        // For Bo5, only apply priority 2 bans for the team that won the coinflip
                        if (validTeam1Bans.Count >= 2 && !team1Won)
                        {
                            // Remove priority 2 ban from team 1's bans if they lost the coinflip
                            validTeam1Bans.RemoveAt(1);
                            _logger.LogInformation("Removed conditional (priority 2) ban from Team 1 as they lost the coinflip");
                        }

                        if (validTeam2Bans.Count >= 2 && !team2Won)
                        {
                            // Remove priority 2 ban from team 2's bans if they lost the coinflip
                            validTeam2Bans.RemoveAt(1);
                            _logger.LogInformation("Removed conditional (priority 2) ban from Team 2 as they lost the coinflip");
                        }
                    }
                }

                // Process valid team 1 bans
                foreach (var ban in validTeam1Bans)
                {
                    availableMaps.Remove(ban);
                }

                // Process valid team 2 bans
                foreach (var ban in validTeam2Bans)
                {
                    availableMaps.Remove(ban);
                }

                // If too many maps are banned and not enough remain, handle this gracefully
                if (availableMaps.Count < matchLength)
                {
                    _logger.LogWarning($"After processing bans, only {availableMaps.Count} maps remain, but {matchLength} are needed");

                    // Option 1: Add back some banned maps if necessary
                    if (availableMaps.Count == 0)
                    {
                        _logger.LogWarning("No maps left after bans, using original map pool");
                        availableMaps = mapPool.ToList();
                    }
                    else
                    {
                        // Option 2: Duplicate existing maps to reach the required count
                        _logger.LogWarning("Duplicating available maps to reach required match length");
                        while (availableMaps.Count < matchLength)
                        {
                            // Get maps to duplicate (up to what we need)
                            var mapsToDuplicate = availableMaps.Take(Math.Min(availableMaps.Count, matchLength - availableMaps.Count));
                            availableMaps.AddRange(mapsToDuplicate);
                        }
                    }
                }

                // Shuffle and take required number of maps
                var selectedMaps = availableMaps.OrderBy(_ => _randomProvider.Instance.Next()).Take(matchLength).ToList();
                _logger.LogInformation($"Generated map list with {selectedMaps.Count} maps");
                return selectedMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating map list");
                return Enumerable.Repeat("Default Map", matchLength).ToList();
            }
        }

        /// <summary>
        /// Gets the list of available maps for the next game in a match
        /// </summary>
        public List<string> GetAvailableMapsForNextGame(Round round)
        {
            try
            {
                if (round == null)
                {
                    _logger.LogError("Cannot get available maps: round is null");
                    return _fallbackMaps;
                }

                // Get the base map pool
                var mapPool = GetTournamentMapPool(round.OneVOne);

                // Get all banned maps - use the GenerateMapList method to handle conditional bans
                var team1Bans = round.Teams?.Count > 0 ? round.Teams[0].MapBans?.ToList() ?? new List<string>() : new List<string>();
                var team2Bans = round.Teams?.Count > 1 ? round.Teams[1].MapBans?.ToList() ?? new List<string>() : new List<string>();

                // Remove banned maps from the pool (including conditional bans based on coinflip)
                var availableMaps = mapPool.ToList();
                var bannedMaps = new HashSet<string>();

                // Use GenerateMapList to get maps after applying bans
                var mapsAfterBans = GenerateMapList(round.OneVOne, team1Bans, team2Bans, Math.Max(1, mapPool.Count - (team1Bans.Count + team2Bans.Count)), round);

                // Calculate banned maps by finding difference between original map pool and remaining maps
                foreach (var map in mapPool)
                {
                    if (!mapsAfterBans.Contains(map))
                    {
                        bannedMaps.Add(map);
                    }
                }

                // Get global bans from round properties if available
                if (round.CustomProperties != null &&
                    round.CustomProperties.ContainsKey("BannedMaps") &&
                    round.CustomProperties["BannedMaps"] is IEnumerable<string> globalBans)
                {
                    foreach (var ban in globalBans)
                    {
                        if (!string.IsNullOrEmpty(ban))
                        {
                            bannedMaps.Add(ban);
                        }
                    }
                }

                // Get all played maps
                var playedMaps = new HashSet<string>(round.Maps ?? new List<string>());

                // Filter out banned and played maps
                availableMaps = mapPool
                    .Where(map => !bannedMaps.Contains(map) && !playedMaps.Contains(map))
                    .ToList();

                _logger.LogInformation($"Found {availableMaps.Count} available maps for next game (excluded {bannedMaps.Count} banned maps and {playedMaps.Count} played maps)");

                // Handle the case where no maps are available
                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available for next game, using fallback strategy");

                    // Option 1: Use maps that have been played but not banned
                    var unbannedPlayedMaps = mapPool.Where(map => !bannedMaps.Contains(map)).ToList();
                    if (unbannedPlayedMaps.Count > 0)
                    {
                        _logger.LogWarning("Using played maps that are not banned");
                        return unbannedPlayedMaps;
                    }

                    // Option 2: If all maps are banned or played, use the full map pool
                    _logger.LogWarning("All maps are either banned or played, using full map pool");
                    return mapPool.Count > 0 ? mapPool : _fallbackMaps;
                }

                return availableMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available maps for next game");
                return _fallbackMaps;
            }
        }

        /// <summary>
        /// Validates a map ban selection to ensure it contains valid maps
        /// </summary>
        /// <param name="mapBans">The list of map bans to validate</param>
        /// <param name="oneVOne">Whether this is for a 1v1 match</param>
        /// <returns>A tuple containing (isValid, validatedBans, errorMessage)</returns>
        public (bool isValid, List<string> validatedBans, string? errorMessage) ValidateMapBans(List<string> mapBans, bool oneVOne)
        {
            try
            {
                if (mapBans == null || mapBans.Count == 0)
                {
                    return (false, new List<string>(), "No map bans were provided");
                }

                var mapPool = GetTournamentMapPool(oneVOne);
                var validBans = new List<string>();
                var invalidBans = new List<string>();

                foreach (var ban in mapBans)
                {
                    if (string.IsNullOrEmpty(ban))
                    {
                        invalidBans.Add("Empty ban");
                        continue;
                    }

                    if (!mapPool.Contains(ban))
                    {
                        invalidBans.Add(ban);
                        continue;
                    }

                    validBans.Add(ban);
                }

                // Validate for duplicates
                var duplicates = validBans
                    .GroupBy(x => x)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicates.Count > 0)
                {
                    return (false, validBans, $"Duplicate map bans found: {string.Join(", ", duplicates)}");
                }

                if (invalidBans.Count > 0)
                {
                    return (false, validBans, $"Invalid map bans found: {string.Join(", ", invalidBans)}");
                }

                return (true, validBans, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating map bans");
                return (false, new List<string>(), $"Error validating map bans: {ex.Message}");
            }
        }

        public List<string> GenerateMapListBo1(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? globalBans = null, Round? round = null)
        {
            return GenerateMapList(oneVOne, team1Bans, team2Bans, 1, round);
        }

        public List<string> GenerateMapListBo3(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? globalBans = null, Round? round = null)
        {
            return GenerateMapList(oneVOne, team1Bans, team2Bans, 3, round);
        }

        public List<string> GenerateMapListBo5(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? globalBans = null, Round? round = null)
        {
            return GenerateMapList(oneVOne, team1Bans, team2Bans, 5, round);
        }

        public (Map? map, DiscordEmbedBuilder embed) GetRandomMapWithVisualization()
        {
            var map = Maps.MapCollection?.OrderBy(_ => _randomProvider.Instance.Next()).FirstOrDefault();
            if (map != null)
            {
                return (map, _mapService.CreateMapEmbed(map, "Random map selection"));
            }

            var fallbackEmbed = new DiscordEmbedBuilder()
                .WithTitle("Default Map")
                .WithDescription("Random map selection");

            return (null, fallbackEmbed);
        }

        public Map? GetMapByName(string mapName)
        {
            return _mapService.GetMapByName(mapName);
        }

        public Task<(string? url, byte[]? data)> GetMapThumbnailAsync(string mapName)
        {
            return _mapService.GetMapThumbnailAsync(mapName);
        }

        /// <summary>
        /// Gets a random map for the next game, considering banned and played maps
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>A random map name, or null if no maps are available</returns>
        public string? GetRandomMapForNextGame(Round round)
        {
            if (round == null)
            {
                _logger.LogWarning("Cannot get random map: round is null");
                return null;
            }

            try
            {
                var availableMaps = GetAvailableMapsForNextGame(round);

                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available for random selection");
                    return null;
                }

                // Select a random map
                int index = _randomProvider.Instance.Next(availableMaps.Count);
                string selectedMap = availableMaps[index];

                _logger.LogInformation($"Randomly selected map: {selectedMap}");
                return selectedMap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map for next game");
                return null;
            }
        }

        /// <summary>
        /// Sends a map thumbnail as a message that auto-deletes after 5 minutes
        /// </summary>
        /// <param name="channel">The Discord channel to send to</param>
        /// <param name="mapName">The name of the map to display</param>
        /// <param name="client">The Discord client</param>
        /// <returns>The sent message or null if sending failed</returns>
        public async Task<DiscordMessage?> SendMapThumbnailAsync(DiscordChannel channel, string mapName, DiscordClient client)
        {
            try
            {
                // Get the map from our service
                var map = _mapService.GetMapByName(mapName);
                if (map == null)
                {
                    _logger.LogWarning($"Map {mapName} not found");
                    return null;
                }

                // Create an embed for the map
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🗺️ Next Map: {mapName}")
                    .WithDescription($"The next game will be played on **{mapName}**.")
                    .WithColor(new DiscordColor(75, 181, 67))
                    .WithFooter("This message will be automatically deleted in 5 minutes.");

                // Get the thumbnail data
                var (thumbnailUrl, thumbnailData) = await _mapService.GetMapThumbnailAsync(mapName);

                // Create message builder
                var messageBuilder = new DiscordMessageBuilder();
                messageBuilder.AddEmbed(embed);

                // If we have a URL, use it
                if (!string.IsNullOrEmpty(thumbnailUrl))
                {
                    embed.WithImageUrl(thumbnailUrl);
                }
                // If we have data, add as attachment
                else if (thumbnailData != null)
                {
                    string fileName = $"map_{mapName}.png";
                    using var ms = new MemoryStream(thumbnailData);
                    messageBuilder.AddFile(fileName, ms);
                    embed.WithImageUrl($"attachment://{fileName}");
                }

                // Send the message
                var message = await channel.SendMessageAsync(messageBuilder);

                // Schedule auto-deletion after 5 minutes
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    try
                    {
                        await message.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to auto-delete map thumbnail for {mapName}");
                    }
                });

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending map thumbnail for {mapName}");
                return null;
            }
        }
    }
}