using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wabbit.BotClient.Config;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing competitive seasons at runtime
    /// </summary>
    public class SeasonStateService : ISeasonStateService
    {
        private readonly ILogger<SeasonStateService> _logger;
        private readonly ISeasonRepositoryService _seasonRepository;
        private readonly IPlayerRatingRepositoryService _playerRatingRepository;
        private readonly ITeamRepositoryService _teamRepository;
        private readonly Dictionary<string, Season> _seasons;
        private string? _currentSeasonId;
        private readonly List<ulong> _admins;
        private bool _isInitialized;
        private readonly DiscordClient _discordClient;
        private readonly ulong _guildId;

        /// <summary>
        /// Initializes a new instance of the SeasonStateService class
        /// </summary>
        public SeasonStateService(
            ILogger<SeasonStateService> logger,
            ISeasonRepositoryService seasonRepository,
            IPlayerRatingRepositoryService playerRatingRepository,
            ITeamRepositoryService teamRepository,
            DiscordClient discordClient)
        {
            _logger = logger;
            _seasonRepository = seasonRepository;
            _playerRatingRepository = playerRatingRepository;
            _teamRepository = teamRepository;
            _seasons = new Dictionary<string, Season>();
            _admins = new List<ulong>(); // Will be populated during initialization
            _isInitialized = false;
            _discordClient = discordClient;

            // Get the guild ID from config
            // Use the first server ID from the config if available
            var guildId = ConfigManager.Config?.Servers?.FirstOrDefault()?.ServerId ?? 0;
            if (guildId == 0)
            {
                _logger.LogWarning("No guild ID configured for season service. Server owner checks will not work.");
            }
            _guildId = guildId;
        }

        /// <summary>
        /// Initialize season state
        /// </summary>
        public async Task Initialize()
        {
            if (_isInitialized)
                return;

            try
            {
                _logger.LogInformation("Initializing season state...");

                // Load admin list from config (this would typically come from a config service)
                // TODO: Replace with actual admin list from configuration
                _admins.AddRange(new ulong[] { 123456789012345678 }); // Placeholder admin IDs

                // Load seasons from repository
                var seasons = await _seasonRepository.GetAllSeasonsAsync();
                foreach (var season in seasons)
                {
                    _seasons[season.SeasonId] = season;
                    if (season.IsActive)
                    {
                        _currentSeasonId = season.SeasonId;
                    }
                }

                _isInitialized = true;
                _logger.LogInformation($"Season state initialized with {_seasons.Count} seasons. Current active season: {(_currentSeasonId != null ? _seasons[_currentSeasonId].SeasonName : "None")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing season state");
                throw;
            }
        }

        /// <summary>
        /// Get the current active season if one exists
        /// </summary>
        public async Task<Season?> GetCurrentSeasonAsync()
        {
            if (!_isInitialized)
                await Initialize();

            if (_currentSeasonId != null && _seasons.TryGetValue(_currentSeasonId, out var season))
                return season;

            // If not in cache, try to get from repository
            var currentSeason = await _seasonRepository.GetCurrentSeasonAsync();
            if (currentSeason != null)
            {
                _seasons[currentSeason.SeasonId] = currentSeason;
                _currentSeasonId = currentSeason.SeasonId;
            }

            return currentSeason;
        }

        /// <summary>
        /// Check if a season is currently active
        /// </summary>
        public async Task<bool> IsSeasonActiveAsync()
        {
            var currentSeason = await GetCurrentSeasonAsync();
            return currentSeason != null && currentSeason.IsActive;
        }

        /// <summary>
        /// Start a new competitive season
        /// </summary>
        public async Task<Season> StartNewSeasonAsync(string name, string description, DateTime endDate, DiscordUser creator)
        {
            if (!_isInitialized)
                await Initialize();

            // Check if there's already an active season
            var currentSeason = await GetCurrentSeasonAsync();
            if (currentSeason != null && currentSeason.IsActive)
            {
                throw new InvalidOperationException("Cannot start a new season while another season is active");
            }

            // Create the new season
            var newSeason = await _seasonRepository.StartNewSeasonAsync(name, description, endDate, creator);

            // Update in-memory cache
            _seasons[newSeason.SeasonId] = newSeason;
            _currentSeasonId = newSeason.SeasonId;

            _logger.LogInformation($"New season '{name}' started by {creator.Username} (ID: {creator.Id})");
            return newSeason;
        }

        /// <summary>
        /// End the current season
        /// </summary>
        public async Task<Season?> EndCurrentSeasonAsync()
        {
            if (!_isInitialized)
                await Initialize();

            var currentSeason = await GetCurrentSeasonAsync();
            if (currentSeason == null || !currentSeason.IsActive)
            {
                _logger.LogWarning("Attempted to end season, but no active season found");
                return null;
            }

            // Get current player ratings to snapshot
            var playerRatings = await _playerRatingRepository.GetAllPlayerRatingsAsync();

            // Capture final rankings by generating them from current ratings
            var finalRankings = GenerateFinalRankings(playerRatings);

            // End the season in the repository
            currentSeason = await _seasonRepository.EndCurrentSeasonAsync(playerRatings);
            if (currentSeason == null)
            {
                _logger.LogError("Failed to end the current season");
                return null;
            }

            // Update in-memory cache
            _seasons[currentSeason.SeasonId] = currentSeason;
            _currentSeasonId = null;

            _logger.LogInformation($"Season '{currentSeason.SeasonName}' ended");
            return currentSeason;
        }

        /// <summary>
        /// Get a season by ID
        /// </summary>
        public async Task<Season?> GetSeasonByIdAsync(string seasonId)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(seasonId))
                return null;

            // Try to get from in-memory cache
            if (_seasons.TryGetValue(seasonId, out var season))
                return season;

            // If not in cache, try to get from repository
            var repoSeason = await _seasonRepository.GetSeasonByIdAsync(seasonId);
            if (repoSeason != null)
            {
                _seasons[repoSeason.SeasonId] = repoSeason;
            }

            return repoSeason;
        }

        /// <summary>
        /// Get past seasons
        /// </summary>
        public async Task<List<Season>> GetPastSeasonsAsync(int count = 5)
        {
            if (!_isInitialized)
                await Initialize();

            // Get from in-memory cache first
            var pastSeasons = _seasons.Values
                .Where(s => !s.IsActive && s.EndDate.HasValue)
                .OrderByDescending(s => s.EndDate)
                .Take(count)
                .ToList();

            // If not enough in cache, get from repository
            if (pastSeasons.Count < count)
            {
                pastSeasons = await _seasonRepository.GetPastSeasonsAsync(count);
                foreach (var season in pastSeasons)
                {
                    _seasons[season.SeasonId] = season;
                }
            }

            return pastSeasons;
        }

        /// <summary>
        /// Add a related message to a season
        /// </summary>
        public async Task<bool> AddSeasonMessageAsync(string seasonId, DiscordChannel channel, DiscordMessage message, SeasonMessageType messageType)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the season
            var season = await GetSeasonByIdAsync(seasonId);
            if (season == null)
                return false;

            // Add the message to the season
            bool success = await _seasonRepository.AddSeasonMessageAsync(seasonId, channel.Id, message.Id, messageType);
            if (success)
            {
                // If successful, update in-memory cache
                if (_seasons.TryGetValue(seasonId, out var cachedSeason))
                {
                    cachedSeason.AddRelatedMessage(channel.Id, message.Id, messageType.ToString());
                }

                _logger.LogInformation($"Added {messageType} message to season '{season.SeasonName}' (ID: {seasonId})");
            }

            return success;
        }

        /// <summary>
        /// Update a season's end date
        /// </summary>
        public async Task<bool> UpdateSeasonEndDateAsync(string seasonId, DateTime newEndDate, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Only admins can update season end dates
            if (!_admins.Contains(requester.Id))
            {
                _logger.LogWarning($"User {requester.Username} (ID: {requester.Id}) attempted to update season end date without permission");
                return false;
            }

            // Get the season
            var season = await GetSeasonByIdAsync(seasonId);
            if (season == null)
                return false;

            // Can't update end date of inactive seasons
            if (!season.IsActive)
            {
                _logger.LogWarning($"Attempted to update end date of inactive season '{season.SeasonName}'");
                return false;
            }

            // Update the season
            season.EndDate = new DateTimeOffset(newEndDate);

            // Update in repository
            var updatedSeason = await _seasonRepository.UpdateSeasonAsync(season);
            if (updatedSeason)
            {
                // Update in-memory cache
                _seasons[seasonId] = season;

                _logger.LogInformation($"Updated end date of season '{season.SeasonName}' to {newEndDate} by {requester.Username} (ID: {requester.Id})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Delete a season (admin only)
        /// </summary>
        public async Task<bool> DeleteSeasonAsync(string seasonId, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Only admins can delete seasons
            if (!_admins.Contains(requester.Id))
            {
                _logger.LogWarning($"User {requester.Username} (ID: {requester.Id}) attempted to delete season without permission");
                return false;
            }

            // Get the season
            var season = await GetSeasonByIdAsync(seasonId);
            if (season == null)
                return false;

            // Delete from repository
            bool success = await _seasonRepository.DeleteSeasonAsync(seasonId);
            if (success)
            {
                // Update in-memory cache
                if (_seasons.ContainsKey(seasonId))
                {
                    _seasons.Remove(seasonId);
                }

                // If this was the current season, clear the current season ID
                if (_currentSeasonId == seasonId)
                {
                    _currentSeasonId = null;
                }

                _logger.LogInformation($"Season '{season.SeasonName}' (ID: {seasonId}) deleted by {requester.Username} (ID: {requester.Id})");
            }

            return success;
        }

        /// <summary>
        /// Get all seasons
        /// </summary>
        public async Task<List<Season>> GetAllSeasonsAsync()
        {
            if (!_isInitialized)
                await Initialize();

            // Get from in-memory cache first
            var seasons = _seasons.Values.ToList();

            // If no seasons in cache, get from repository
            if (seasons.Count == 0)
            {
                seasons = await _seasonRepository.GetAllSeasonsAsync();
                foreach (var season in seasons)
                {
                    _seasons[season.SeasonId] = season;
                }
            }

            return seasons;
        }

        /// <summary>
        /// Get the final rankings for a specific season
        /// </summary>
        public async Task<List<SeasonRanking>> GetSeasonFinalRankingsAsync(string seasonId, GameType gameType, int count = 10)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the season
            var season = await GetSeasonByIdAsync(seasonId);
            if (season == null)
                return new List<SeasonRanking>();

            // Convert GameType to TeamGameType
            var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;

            // Get rankings from season
            if (season.FinalRankings.TryGetValue(teamGameType, out var rankings))
            {
                return rankings.Take(count).ToList();
            }

            return new List<SeasonRanking>();
        }

        /// <summary>
        /// Check if a user has admin privileges for season management
        /// </summary>
        public async Task<bool> HasSeasonAdminPrivilegesAsync(ulong userId)
        {
            if (!_isInitialized)
                await Initialize();

            try
            {
                // First check if user is in the admin list for backward compatibility
                if (_admins.Contains(userId))
                    return true;

                // Then check Discord's built-in permissions
                if (_guildId == 0)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                    return false;
                }

                var guild = await _discordClient.GetGuildAsync(_guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check Discord permissions for user {userId} - member not found");
                    return false;
                }

                // Check if user is the server owner or has Administrator permission
                return member.IsOwner || member.Permissions.HasFlag(DiscordPermission.Administrator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking admin privileges for user {userId}");
                return false;
            }
        }

        /// <summary>
        /// Generate a preview of what the final rankings would be if the season ended now
        /// </summary>
        public async Task<List<SeasonRanking>> PreviewSeasonFinalRankingsAsync(GameType gameType, int count = 10)
        {
            if (!_isInitialized)
                await Initialize();

            // Check if a season is active
            var currentSeason = await GetCurrentSeasonAsync();
            if (currentSeason == null || !currentSeason.IsActive)
            {
                return new List<SeasonRanking>();
            }

            // Get current player ratings
            var playerRatings = await _playerRatingRepository.GetAllPlayerRatingsAsync();

            // Convert GameType to TeamGameType
            var teamGameType = (Wabbit.Models.TeamGameType)(int)gameType;

            // Generate rankings for this game type
            var rankings = GenerateRankingsForType(teamGameType, playerRatings);

            return rankings.Take(count).ToList();
        }

        /// <summary>
        /// Helper method to generate final rankings for all game types
        /// </summary>
        private Dictionary<TeamGameType, List<SeasonRanking>> GenerateFinalRankings(List<PlayerRating> playerRatings)
        {
            var rankings = new Dictionary<TeamGameType, List<SeasonRanking>>();

            // Generate rankings for each game type
            foreach (TeamGameType gameType in Enum.GetValues(typeof(TeamGameType)))
            {
                // For team types, we'll add those from the team service
                var rankingsForType = GenerateRankingsForType(gameType, playerRatings);
                rankings[gameType] = rankingsForType;
            }

            return rankings;
        }

        /// <summary>
        /// Helper method to generate rankings for a specific game type
        /// </summary>
        private List<SeasonRanking> GenerateRankingsForType(TeamGameType gameType, List<PlayerRating> playerRatings)
        {
            var rankings = new List<SeasonRanking>();

            // For 1v1, include individual players (from PlayerRating)
            if (gameType == TeamGameType.OneVOne)
            {
                var playersForGameType = playerRatings
                    .Where(p => p.Rating > 0)
                    .OrderByDescending(p => p.Rating)
                    .ToList();

                int rank = 1;
                foreach (var player in playersForGameType)
                {
                    rankings.Add(new SeasonRanking
                    {
                        Rank = rank++,
                        IsTeam = false,
                        PlayerId = player.PlayerId,
                        PlayerUsername = player.Username,
                        Rating = player.Rating,
                        Wins = player.Wins,
                        Losses = player.Losses
                    });
                }
            }

            return rankings;
        }
    }
}