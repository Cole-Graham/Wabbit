using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;
using Wabbit.Models;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for player rating data storage and retrieval
    /// </summary>
    public class PlayerRatingRepositoryService : IPlayerRatingRepositoryService
    {
        private readonly string _dataDirectory;
        private readonly string _ratingsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<PlayerRatingRepositoryService> _logger;
        private readonly DiscordClient _client;

        private List<PlayerRating> _playerRatings = new List<PlayerRating>();
        private readonly object _lockObject = new object();

        public PlayerRatingRepositoryService(
            ILogger<PlayerRatingRepositoryService> logger,
            DiscordClient client)
        {
            _logger = logger;
            _client = client;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _ratingsFilePath = Path.Combine(_dataDirectory, "ratings.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };
        }

        /// <summary>
        /// Initialize the repository by loading ratings
        /// </summary>
        public async Task Initialize()
        {
            await LoadPlayerRatingsAsync();
        }

        /// <inheritdoc/>
        public Task<PlayerRating?> GetPlayerRatingAsync(ulong userId)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_playerRatings.FirstOrDefault(p => p.PlayerId == userId));
            }
        }

        /// <inheritdoc/>
        public async Task<PlayerRating> GetOrCreatePlayerRatingAsync(DiscordUser user)
        {
            PlayerRating? rating;

            lock (_lockObject)
            {
                rating = _playerRatings.FirstOrDefault(p => p.PlayerId == user.Id);
            }

            if (rating == null)
            {
                rating = new PlayerRating
                {
                    PlayerId = user.Id,
                    Username = user.Username
                };

                lock (_lockObject)
                {
                    _playerRatings.Add(rating);
                }

                await SavePlayerRatingsAsync();
            }
            else if (rating.Username != user.Username)
            {
                // Update username if it changed
                rating.Username = user.Username;
                await UpdatePlayerRatingAsync(rating);
            }

            return rating;
        }

        /// <inheritdoc/>
        public async Task<bool> UpdatePlayerRatingAsync(PlayerRating rating)
        {
            bool updated = false;

            lock (_lockObject)
            {
                var index = _playerRatings.FindIndex(p => p.PlayerId == rating.PlayerId);
                if (index >= 0)
                {
                    _playerRatings[index] = rating;
                    updated = true;
                }
                else
                {
                    _playerRatings.Add(rating);
                    updated = true;
                }
            }

            if (updated)
            {
                await SavePlayerRatingsAsync();
            }

            return updated;
        }

        /// <inheritdoc/>
        public Task<List<PlayerRating>> GetAllPlayerRatingsAsync()
        {
            lock (_lockObject)
            {
                return Task.FromResult(_playerRatings.ToList());
            }
        }

        /// <inheritdoc/>
        public Task<List<PlayerRating>> GetTopPlayersByRatingAsync(GameType gameType, int count = 10, bool tournamentRatings = false)
        {
            lock (_lockObject)
            {
                // Convert GameType to TeamGameType using simple cast
                var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;
                var result = _playerRatings
                    .Where(p => tournamentRatings
                        ? p.TournamentRatings.ContainsKey(teamGameType) && p.TournamentRatings[teamGameType] > 0
                        : p.Ratings.ContainsKey(teamGameType) && p.Ratings[teamGameType] > 0)
                    .OrderByDescending(p => tournamentRatings
                        ? p.TournamentRatings.GetValueOrDefault(teamGameType, 0)
                        : p.Ratings.GetValueOrDefault(teamGameType, 0))
                    .Take(count)
                    .ToList();

                return Task.FromResult(result);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> SavePlayerRatingsAsync()
        {
            try
            {
                var ratingsToSave = new List<PlayerRating>();

                lock (_lockObject)
                {
                    ratingsToSave = _playerRatings.ToList();
                }

                var ratingWrapper = new PlayerRatingListWrapper { Players = ratingsToSave };

                string json = JsonSerializer.Serialize(ratingWrapper, _serializerOptions);
                await File.WriteAllTextAsync(_ratingsFilePath, json);

                _logger.LogInformation("Saved {Count} player ratings to {FilePath}", ratingsToSave.Count, _ratingsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving player ratings to {FilePath}: {Message}", _ratingsFilePath, ex.Message);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> LoadPlayerRatingsAsync()
        {
            if (!File.Exists(_ratingsFilePath))
            {
                _logger.LogInformation("Ratings file {FilePath} doesn't exist yet, starting with empty list", _ratingsFilePath);
                return true;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_ratingsFilePath);

                // Check if the file is empty
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Ratings file {FilePath} is empty, starting with empty list", _ratingsFilePath);
                    return true;
                }

                var ratingWrapper = JsonSerializer.Deserialize<PlayerRatingListWrapper>(json, _serializerOptions);

                if (ratingWrapper == null || ratingWrapper.Players == null)
                {
                    _logger.LogWarning("Failed to deserialize player ratings from {FilePath}", _ratingsFilePath);
                    return false;
                }

                lock (_lockObject)
                {
                    _playerRatings = ratingWrapper.Players;
                }

                _logger.LogInformation("Loaded {Count} player ratings from {FilePath}", _playerRatings.Count, _ratingsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading player ratings from {FilePath}: {Message}", _ratingsFilePath, ex.Message);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<int> ResetRatingsAsync(GameType gameType, bool tournamentRatings = false)
        {
            int affectedCount = 0;
            List<PlayerRating> playersToUpdate = new List<PlayerRating>();
            var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;
            int defaultRating = 1200; // Default rating value

            lock (_lockObject)
            {
                foreach (var player in _playerRatings)
                {
                    bool updated = false;

                    if (tournamentRatings)
                    {
                        if (player.TournamentRatings.ContainsKey(teamGameType))
                        {
                            player.TournamentRatings[teamGameType] = defaultRating;
                            updated = true;
                        }
                    }
                    else
                    {
                        if (player.Ratings.ContainsKey(teamGameType))
                        {
                            player.Ratings[teamGameType] = defaultRating;
                            updated = true;
                        }
                    }

                    // Also reset the wins/losses for this game type
                    if (player.Wins.ContainsKey(teamGameType))
                    {
                        player.Wins[teamGameType] = 0;
                        updated = true;
                    }

                    if (player.Losses.ContainsKey(teamGameType))
                    {
                        player.Losses[teamGameType] = 0;
                        updated = true;
                    }

                    if (updated)
                    {
                        affectedCount++;
                        playersToUpdate.Add(player);
                    }
                }
            }

            // Save changes
            if (affectedCount > 0)
            {
                await SavePlayerRatingsAsync();
            }

            return affectedCount;
        }

        /// <inheritdoc/>
        public async Task<bool> DeletePlayerRatingAsync(ulong userId)
        {
            bool removed = false;

            lock (_lockObject)
            {
                var index = _playerRatings.FindIndex(p => p.PlayerId == userId);
                if (index >= 0)
                {
                    _playerRatings.RemoveAt(index);
                    removed = true;
                }
            }

            if (removed)
            {
                await SavePlayerRatingsAsync();
            }

            return removed;
        }

        /// <inheritdoc/>
        public Task<LeaderboardStats> GetLeaderboardStatsAsync()
        {
            var stats = new LeaderboardStats
            {
                TotalPlayers = _playerRatings.Count
            };

            lock (_lockObject)
            {
                // Initialize dictionaries
                foreach (var gameType in Enum.GetValues(typeof(GameType)).Cast<GameType>())
                {
                    stats.PlayerCountByGameType[gameType] = 0;
                    stats.HighestRatingByGameType[gameType] = 0;
                    stats.AverageRatingByGameType[gameType] = 0;
                    stats.TotalMatchesByGameType[gameType] = 0;
                }

                // Process all players
                foreach (var player in _playerRatings)
                {
                    // Check ratings for each game type
                    foreach (var gameType in Enum.GetValues(typeof(GameType)).Cast<GameType>())
                    {
                        var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;

                        // Check if player has this rating
                        if (player.Ratings.ContainsKey(teamGameType))
                        {
                            var rating = player.Ratings[teamGameType];
                            if (rating > 0)
                            {
                                stats.PlayerCountByGameType[gameType]++;
                                stats.HighestRatingByGameType[gameType] = Math.Max(stats.HighestRatingByGameType[gameType], rating);
                                stats.AverageRatingByGameType[gameType] += rating;
                            }
                        }
                    }

                    // Count total matches
                    foreach (var gameType in Enum.GetValues(typeof(GameType)).Cast<GameType>())
                    {
                        var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;
                        int wins = player.Wins.GetValueOrDefault(teamGameType, 0);
                        int losses = player.Losses.GetValueOrDefault(teamGameType, 0);
                        stats.TotalMatchesByGameType[gameType] += wins + losses;
                    }
                }

                // Calculate averages
                foreach (var gameType in Enum.GetValues(typeof(GameType)).Cast<GameType>())
                {
                    if (stats.PlayerCountByGameType[gameType] > 0)
                    {
                        stats.AverageRatingByGameType[gameType] /= stats.PlayerCountByGameType[gameType];
                    }
                }

                return Task.FromResult(stats);
            }
        }

        /// <summary>
        /// Wrapper class for serializing player ratings
        /// </summary>
        private class PlayerRatingListWrapper
        {
            public List<PlayerRating> Players { get; set; } = new List<PlayerRating>();
        }
    }
}