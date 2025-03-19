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
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for competitive season data storage and retrieval
    /// </summary>
    public class SeasonRepositoryService : ISeasonRepositoryService
    {
        private readonly string _dataDirectory;
        private readonly string _seasonsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<SeasonRepositoryService> _logger;
        private readonly DiscordClient _client;

        private List<Season> _seasons = new List<Season>();
        private string? _currentSeasonId;
        private readonly object _lockObject = new object();

        public SeasonRepositoryService(
            ILogger<SeasonRepositoryService> logger,
            DiscordClient client)
        {
            _logger = logger;
            _client = client;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _seasonsFilePath = Path.Combine(_dataDirectory, "seasons.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };
        }

        /// <summary>
        /// Initialize the repository by loading seasons
        /// </summary>
        public async Task Initialize()
        {
            await LoadSeasonsAsync();
        }

        /// <inheritdoc/>
        public Task<Season?> GetCurrentSeasonAsync()
        {
            lock (_lockObject)
            {
                if (string.IsNullOrEmpty(_currentSeasonId))
                {
                    return Task.FromResult<Season?>(null);
                }

                return Task.FromResult(_seasons.FirstOrDefault(s => s.SeasonId == _currentSeasonId));
            }
        }

        /// <inheritdoc/>
        public async Task<Season> StartNewSeasonAsync(string name, string description, DateTime endDate, DiscordUser creator)
        {
            // Check if there's already an active season
            var currentSeason = await GetCurrentSeasonAsync();
            if (currentSeason != null)
            {
                throw new InvalidOperationException("Cannot start a new season while another season is active. End the current season first.");
            }

            // Create new season
            var season = new Season
            {
                SeasonName = name,
                Description = description,
                StartDate = DateTime.UtcNow,
                EndDate = endDate,
                IsActive = true,
                CreatorId = creator.Id,
                CreatorUsername = creator.Username
            };

            lock (_lockObject)
            {
                _seasons.Add(season);
                _currentSeasonId = season.SeasonId;
            }

            await SaveSeasonsAsync();
            return season;
        }

        /// <inheritdoc/>
        public async Task<Season?> EndCurrentSeasonAsync(List<PlayerRating> playerRatings)
        {
            Season? season;
            lock (_lockObject)
            {
                if (string.IsNullOrEmpty(_currentSeasonId))
                {
                    _logger.LogWarning("No active season to end");
                    return Task.FromResult<Season?>(null).Result;
                }

                // Find current season
                season = _seasons.Find(s => s.SeasonId == _currentSeasonId);
                if (season == null)
                {
                    _logger.LogWarning($"Current season ID {_currentSeasonId} not found in seasons list");
                    return Task.FromResult<Season?>(null).Result;
                }
            }

            // Get the season end time
            var endTime = DateTimeOffset.UtcNow;

            // Convert player ratings to final rankings dictionary
            var finalRankings = new Dictionary<Wabbit.Models.TeamGameType, List<SeasonRanking>>();

            // Initialize for all team types
            foreach (Wabbit.Models.TeamGameType teamGameType in Enum.GetValues(typeof(Wabbit.Models.TeamGameType)))
            {
                finalRankings[teamGameType] = new List<SeasonRanking>();
            }

            // Add 1v1 player rankings
            finalRankings[Wabbit.Models.TeamGameType.OneVOne] = playerRatings
                .OrderByDescending(p => p.GetRating(Wabbit.Models.TeamGameType.OneVOne))
                .Select((p, i) => new SeasonRanking
                {
                    Rank = i + 1,
                    PlayerId = p.PlayerId,
                    PlayerUsername = p.Username,
                    Rating = p.GetRating(Wabbit.Models.TeamGameType.OneVOne),
                    Wins = p.Wins.GetValueOrDefault(Wabbit.Models.TeamGameType.OneVOne, 0),
                    Losses = p.Losses.GetValueOrDefault(Wabbit.Models.TeamGameType.OneVOne, 0)
                }).ToList();

            // End the season with the calculated rankings
            season.EndSeason(finalRankings);

            // Clear current season ID
            lock (_lockObject)
            {
                _currentSeasonId = null;
            }

            // Save changes
            await SaveSeasonsAsync();

            _logger.LogInformation($"Ended season {season.SeasonId} ({season.SeasonName})");
            return season;
        }

        /// <inheritdoc/>
        public Task<Season?> GetSeasonByIdAsync(string seasonId)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_seasons.FirstOrDefault(s => s.SeasonId == seasonId));
            }
        }

        /// <inheritdoc/>
        public Task<List<Season>> GetAllSeasonsAsync()
        {
            lock (_lockObject)
            {
                return Task.FromResult(_seasons.ToList());
            }
        }

        /// <inheritdoc/>
        public Task<List<Season>> GetPastSeasonsAsync(int count = 5)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_seasons
                    .Where(s => !s.IsActive)
                    .OrderByDescending(s => s.EndDate)
                    .Take(count)
                    .ToList());
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateSeasonAsync(Season season)
        {
            bool updated = false;

            lock (_lockObject)
            {
                var index = _seasons.FindIndex(s => s.SeasonId == season.SeasonId);
                if (index >= 0)
                {
                    _seasons[index] = season;
                    updated = true;
                }
            }

            if (updated)
            {
                await SaveSeasonsAsync();
            }

            return updated;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteSeasonAsync(string seasonId)
        {
            bool deleted = false;

            lock (_lockObject)
            {
                var index = _seasons.FindIndex(s => s.SeasonId == seasonId);
                if (index >= 0)
                {
                    // If we're deleting the current season, clear the current season ID
                    if (_currentSeasonId == seasonId)
                    {
                        _currentSeasonId = null;
                    }

                    _seasons.RemoveAt(index);
                    deleted = true;
                }
            }

            if (deleted)
            {
                await SaveSeasonsAsync();
            }

            return deleted;
        }

        /// <inheritdoc/>
        public async Task<bool> AddSeasonMessageAsync(string seasonId, ulong channelId, ulong messageId, SeasonMessageType type)
        {
            Season? season;

            lock (_lockObject)
            {
                season = _seasons.FirstOrDefault(s => s.SeasonId == seasonId);
                if (season == null)
                {
                    return false;
                }
            }

            // Add message
            season.AddRelatedMessage(channelId, messageId, type.ToString());

            await SaveSeasonsAsync();
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> SaveSeasonsAsync()
        {
            try
            {
                var seasonsToSave = new List<Season>();
                string? currentSeasonId;

                lock (_lockObject)
                {
                    seasonsToSave = _seasons.ToList();
                    currentSeasonId = _currentSeasonId;
                }

                var seasonWrapper = new SeasonWrapper
                {
                    Seasons = seasonsToSave,
                    CurrentSeason = currentSeasonId
                };

                string json = JsonSerializer.Serialize(seasonWrapper, _serializerOptions);
                await File.WriteAllTextAsync(_seasonsFilePath, json);

                _logger.LogInformation("Saved {Count} seasons to {FilePath}", seasonsToSave.Count, _seasonsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving seasons to {FilePath}: {Message}", _seasonsFilePath, ex.Message);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> LoadSeasonsAsync()
        {
            if (!File.Exists(_seasonsFilePath))
            {
                _logger.LogInformation("Seasons file {FilePath} doesn't exist yet, starting with empty list", _seasonsFilePath);
                return true;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_seasonsFilePath);

                // Check if the file is empty
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Seasons file {FilePath} is empty, starting with empty list", _seasonsFilePath);
                    return true;
                }

                var seasonWrapper = JsonSerializer.Deserialize<SeasonWrapper>(json, _serializerOptions);

                if (seasonWrapper == null)
                {
                    _logger.LogWarning("Failed to deserialize seasons from {FilePath}", _seasonsFilePath);
                    return false;
                }

                lock (_lockObject)
                {
                    _seasons = seasonWrapper.Seasons ?? new List<Season>();
                    _currentSeasonId = seasonWrapper.CurrentSeason;
                }

                _logger.LogInformation("Loaded {Count} seasons from {FilePath}", _seasons.Count, _seasonsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading seasons from {FilePath}: {Message}", _seasonsFilePath, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Wrapper class for serializing seasons
        /// </summary>
        private class SeasonWrapper
        {
            public List<Season> Seasons { get; set; } = new List<Season>();
            public string? CurrentSeason { get; set; }
        }
    }
}