using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Service for managing leaderboard data and displays
    /// </summary>
    public class LeaderboardService : ILeaderboardService
    {
        private readonly ILogger<LeaderboardService> _logger;
        private readonly IPlayerRatingRepositoryService _playerRatingRepository;
        private readonly ITeamRepositoryService _teamRepository;
        private readonly ISeasonStateService _seasonStateService;
        private readonly ITeamStateService _teamStateService;
        private readonly Dictionary<string, Dictionary<Wabbit.Models.TeamGameType, List<LeaderboardEntry>>> _cachedLeaderboards;
        private readonly Dictionary<string, DateTimeOffset> _cacheExpiration;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15); // Cache leaderboards for 15 minutes

        /// <summary>
        /// Initialize a new LeaderboardService
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="playerRatingRepository">Player rating repository</param>
        /// <param name="teamRepository">Team repository</param>
        /// <param name="seasonStateService">Season state service</param>
        /// <param name="teamStateService">Team state service</param>
        public LeaderboardService(
            ILogger<LeaderboardService> logger,
            IPlayerRatingRepositoryService playerRatingRepository,
            ITeamRepositoryService teamRepository,
            ISeasonStateService seasonStateService,
            ITeamStateService teamStateService)
        {
            _logger = logger;
            _playerRatingRepository = playerRatingRepository;
            _teamRepository = teamRepository;
            _seasonStateService = seasonStateService;
            _teamStateService = teamStateService;
            _cachedLeaderboards = new Dictionary<string, Dictionary<Wabbit.Models.TeamGameType, List<LeaderboardEntry>>>();
            _cacheExpiration = new Dictionary<string, DateTimeOffset>();
        }

        /// <inheritdoc/>
        public async Task<(List<LeaderboardEntry> Rankings, int TotalPages, int TotalEntries)> GetLeaderboardAsync(
            Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1, int itemsPerPage = 25)
        {
            // Validate parameters
            if (page < 1)
                page = 1;
            if (itemsPerPage < 1)
                itemsPerPage = 25;

            // Get current season if not specified
            if (seasonId == null)
            {
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                seasonId = currentSeason?.SeasonId ?? "current"; // Use "current" for live leaderboard
            }

            // Check cache
            if (!IsCached(seasonId, gameType))
            {
                // Generate and cache leaderboard data
                var leaderboardData = await GenerateLeaderboardDataAsync(gameType, seasonId);
                CacheLeaderboard(seasonId, gameType, leaderboardData);
            }

            // Get leaderboard data from cache
            var entries = _cachedLeaderboards[seasonId][gameType];
            var totalEntries = entries.Count;
            var totalPages = (int)Math.Ceiling((double)totalEntries / itemsPerPage);

            // Paginate
            var paginatedEntries = entries
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();

            return (paginatedEntries, totalPages, totalEntries);
        }

        /// <inheritdoc/>
        public async Task<(DiscordEmbed LeaderboardEmbed, DiscordButtonComponent[] NavigationButtons, DiscordSelectComponent SeasonSelector)>
            CreateLeaderboardEmbedAsync(Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1, int itemsPerPage = 25)
        {
            // Get leaderboard data
            var (rankings, totalPages, totalEntries) = await GetLeaderboardAsync(gameType, seasonId, page, itemsPerPage);

            // Get season info
            Season? season = null;
            string seasonName = "Current Season";
            bool isCurrentSeason = true;

            if (!string.IsNullOrEmpty(seasonId) && seasonId != "current")
            {
                season = await _seasonStateService.GetSeasonByIdAsync(seasonId);
                if (season != null)
                {
                    seasonName = season.SeasonName;
                    isCurrentSeason = season.IsActive;
                }
            }

            // Create embed
            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle($"{GetGameTypeDisplayName(gameType)} Leaderboard - {seasonName}")
                .WithDescription($"Showing {Math.Min(rankings.Count, totalEntries)} out of {totalEntries} entries (Page {page}/{totalPages})")
                .WithColor(DiscordColor.Blurple)
                .WithFooter($"Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

            // Add leaderboard entries to embed
            if (rankings.Any())
            {
                var leaderboardText = "";
                foreach (var entry in rankings)
                {
                    string rankEmoji = GetRankEmoji(entry.Rank);
                    string teamIndicator = entry.IsTeam ? " [Team]" : "";
                    leaderboardText += $"{rankEmoji} `{entry.Rank.ToString().PadLeft(2)}` | **{entry.DisplayName}**{teamIndicator} | Rating: **{entry.Rating}** | W/L: **{entry.Wins}/{entry.Losses}** | Win Rate: **{entry.WinRate:0.0}%**\n";
                }
                embedBuilder.AddField("Rankings", leaderboardText);
            }
            else
            {
                embedBuilder.AddField("Rankings", "No entries found for this leaderboard.");
            }

            // Create navigation buttons
            var navigationButtons = CreateNavigationButtons(gameType, seasonId, page, totalPages);

            // Create season selector
            var seasons = await _seasonStateService.GetAllSeasonsAsync();
            var seasonOptions = new List<DiscordSelectComponentOption>();

            // Add current season option
            seasonOptions.Add(new DiscordSelectComponentOption("Current Live Rankings", "current", "View current ratings for all players", isCurrentSeason && (seasonId == null || seasonId == "current")));

            // Add past seasons
            foreach (var s in seasons.OrderByDescending(s => s.StartDate))
            {
                seasonOptions.Add(new DiscordSelectComponentOption(
                    s.SeasonName,
                    s.SeasonId,
                    s.IsActive ? "Active season" : $"Ended: {s.EndDate:yyyy-MM-dd}",
                    seasonId == s.SeasonId
                ));
            }

            var seasonSelector = new DiscordSelectComponent(
                $"season_selector_{gameType}",
                "Select Season",
                seasonOptions
            );

            return (embedBuilder.Build(), navigationButtons, seasonSelector);
        }

        /// <inheritdoc/>
        public async Task<DiscordMessage> PostLeaderboardAsync(DiscordChannel channel, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            var (embedLeaderboard, navigationButtons, seasonSelector) = await CreateLeaderboardEmbedAsync(gameType, seasonId);

            // Create game type buttons
            var gameTypeButtons = CreateGameTypeButtons(gameType);

            // Create message builder
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embedLeaderboard)
                .AddComponents(navigationButtons)
                .AddComponents(gameTypeButtons)
                .AddComponents(seasonSelector);

            // Send message
            return await messageBuilder.SendAsync(channel);
        }

        /// <inheritdoc/>
        public async Task<DiscordMessage> UpdateLeaderboardAsync(DiscordMessage message, Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1)
        {
            var (embedLeaderboard, navigationButtons, seasonSelector) = await CreateLeaderboardEmbedAsync(gameType, seasonId, page);

            // Create game type buttons
            var gameTypeButtons = CreateGameTypeButtons(gameType);

            // Create message builder
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embedLeaderboard)
                .AddComponents(navigationButtons)
                .AddComponents(gameTypeButtons)
                .AddComponents(seasonSelector);

            // Update message
            return await messageBuilder.ModifyAsync(message);
        }

        /// <inheritdoc/>
        public async Task<LeaderboardEntry?> GetPlayerRankingAsync(ulong playerId, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            // Get leaderboard data
            var (rankings, _, _) = await GetLeaderboardAsync(gameType, seasonId);

            // Find player ranking
            return rankings.FirstOrDefault(r => !r.IsTeam && r.PlayerId == playerId);
        }

        /// <inheritdoc/>
        public async Task<LeaderboardEntry?> GetTeamRankingAsync(string teamId, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            // Get leaderboard data
            var (rankings, _, _) = await GetLeaderboardAsync(gameType, seasonId);

            // Find team ranking
            return rankings.FirstOrDefault(r => r.IsTeam && r.TeamId == teamId);
        }

        /// <summary>
        /// Generate leaderboard data for a specific game type and season
        /// </summary>
        private async Task<List<LeaderboardEntry>> GenerateLeaderboardDataAsync(Wabbit.Models.TeamGameType gameType, string seasonId)
        {
            List<LeaderboardEntry> entries = new List<LeaderboardEntry>();

            if (seasonId == "current" || (await _seasonStateService.GetSeasonByIdAsync(seasonId)) == null)
            {
                // Current leaderboard - get from player and team repositories
                var allPlayerRatings = await _playerRatingRepository.GetAllPlayerRatingsAsync();
                var teams = await _teamRepository.GetTeamsByTypeAsync(gameType);

                // For 1v1, add both player and team entries
                if (gameType == Wabbit.Models.TeamGameType.OneVOne)
                {
                    // Add players
                    foreach (var player in allPlayerRatings)
                    {
                        // Only add players with a rating
                        if (player.Rating > 0 && player.Wins + player.Losses > 0)
                        {
                            entries.Add(new LeaderboardEntry
                            {
                                IsTeam = false,
                                PlayerId = player.PlayerId,
                                PlayerUsername = player.Username,
                                Rating = player.Rating,
                                Wins = player.Wins,
                                Losses = player.Losses
                            });
                        }
                    }
                }

                // Add teams for all game types
                foreach (var team in teams)
                {
                    if (team.GameType == gameType && team.Rating > 0)
                    {
                        entries.Add(new LeaderboardEntry
                        {
                            IsTeam = true,
                            TeamId = team.TeamId,
                            TeamName = team.TeamName,
                            Rating = team.Rating,
                            Wins = team.Wins,
                            Losses = team.Losses
                        });
                    }
                }
            }
            else
            {
                // Historical season leaderboard - get from season repository
                var season = await _seasonStateService.GetSeasonByIdAsync(seasonId);
                if (season != null && season.FinalRankings.ContainsKey(gameType))
                {
                    // Convert season rankings to leaderboard entries
                    foreach (var ranking in season.FinalRankings[gameType])
                    {
                        entries.Add(new LeaderboardEntry
                        {
                            Rank = ranking.Rank,
                            IsTeam = ranking.IsTeam,
                            PlayerId = ranking.IsTeam ? null : ranking.PlayerId,
                            PlayerUsername = ranking.IsTeam ? null : ranking.PlayerUsername,
                            TeamId = ranking.IsTeam ? ranking.TeamId : null,
                            TeamName = ranking.IsTeam ? ranking.TeamName : null,
                            Rating = ranking.Rating,
                            Wins = ranking.Wins,
                            Losses = ranking.Losses
                        });
                    }
                }
            }

            // Sort by rating (descending) and assign ranks
            entries = entries.OrderByDescending(e => e.Rating).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = i + 1;
            }

            return entries;
        }

        /// <summary>
        /// Get a player's rating for a specific game type
        /// </summary>
        private int GetPlayerRating(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            // For 1v1 games, return the direct Rating property
            if (gameType == Wabbit.Models.TeamGameType.OneVOne)
                return player.Rating;

            // For other game types, PlayerRating doesn't track those anymore
            return 0;
        }

        /// <summary>
        /// Get a player's win count for a specific game type
        /// </summary>
        private int GetPlayerWins(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            // For 1v1 games, return the direct Wins property
            if (gameType == Wabbit.Models.TeamGameType.OneVOne)
                return player.Wins;

            // For other game types, PlayerRating doesn't track those anymore
            return 0;
        }

        /// <summary>
        /// Get a player's loss count for a specific game type
        /// </summary>
        private int GetPlayerLosses(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            // For 1v1 games, return the direct Losses property
            if (gameType == Wabbit.Models.TeamGameType.OneVOne)
                return player.Losses;

            // For other game types, PlayerRating doesn't track those anymore
            return 0;
        }

        /// <summary>
        /// Check if a leaderboard is cached and still valid
        /// </summary>
        private bool IsCached(string seasonId, Wabbit.Models.TeamGameType gameType)
        {
            // Check if we have a cached leaderboard for this season and game type
            if (!_cachedLeaderboards.ContainsKey(seasonId))
                return false;

            if (!_cachedLeaderboards[seasonId].ContainsKey(gameType))
                return false;

            // Check if the cache has expired
            if (!_cacheExpiration.ContainsKey(seasonId) || _cacheExpiration[seasonId] < DateTimeOffset.UtcNow)
                return false;

            return true;
        }

        /// <summary>
        /// Cache a leaderboard for quick retrieval
        /// </summary>
        private void CacheLeaderboard(string seasonId, Wabbit.Models.TeamGameType gameType, List<LeaderboardEntry> entries)
        {
            // Ensure we have a dictionary for this season
            if (!_cachedLeaderboards.ContainsKey(seasonId))
                _cachedLeaderboards[seasonId] = new Dictionary<Wabbit.Models.TeamGameType, List<LeaderboardEntry>>();

            // Cache the leaderboard
            _cachedLeaderboards[seasonId][gameType] = entries;

            // Set expiration
            _cacheExpiration[seasonId] = DateTimeOffset.UtcNow.Add(_cacheDuration);

            _logger.LogDebug($"Cached leaderboard for season {seasonId}, game type {gameType} with {entries.Count} entries");
        }

        /// <summary>
        /// Get a friendly display name for a game type
        /// </summary>
        private string GetGameTypeDisplayName(Wabbit.Models.TeamGameType gameType)
        {
            return gameType switch
            {
                Wabbit.Models.TeamGameType.OneVOne => "1v1",
                Wabbit.Models.TeamGameType.TwoVTwo => "2v2",
                Wabbit.Models.TeamGameType.ThreeVThree => "3v3",
                Wabbit.Models.TeamGameType.FourVFour => "4v4",
                _ => gameType.ToString()
            };
        }

        /// <summary>
        /// Get an emoji to display next to a rank
        /// </summary>
        private string GetRankEmoji(int rank)
        {
            return rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => "🏅"
            };
        }

        /// <summary>
        /// Create navigation buttons for the leaderboard
        /// </summary>
        private DiscordButtonComponent[] CreateNavigationButtons(Wabbit.Models.TeamGameType gameType, string? seasonId, int page, int totalPages)
        {
            var buttonPrev = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_prev_{gameType}_{seasonId}_{page}",
                "Previous",
                page <= 1
            );

            var buttonNext = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_next_{gameType}_{seasonId}_{page}",
                "Next",
                page >= totalPages
            );

            var buttonFirst = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_first_{gameType}_{seasonId}_{page}",
                "First",
                page <= 1
            );

            var buttonLast = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_last_{gameType}_{seasonId}_{page}",
                "Last",
                page >= totalPages
            );

            var buttonRefresh = new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                $"leaderboard_refresh_{gameType}_{seasonId}_{page}",
                "Refresh"
            );

            return new[] { buttonFirst, buttonPrev, buttonRefresh, buttonNext, buttonLast };
        }

        /// <summary>
        /// Create game type selector buttons
        /// </summary>
        private DiscordButtonComponent[] CreateGameTypeButtons(Wabbit.Models.TeamGameType currentGameType)
        {
            var button1v1 = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.OneVOne ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_OneVOne",
                "1v1"
            );

            var button2v2 = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.TwoVTwo ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_TwoVTwo",
                "2v2"
            );

            var button3v3 = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.ThreeVThree ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_ThreeVThree",
                "3v3"
            );

            var button4v4 = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.FourVFour ? DiscordButtonStyle.Success : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_FourVFour",
                "4v4"
            );

            return new[] { button1v1, button2v2, button3v3, button4v4 };
        }

        /// <summary>
        /// Check if a user has admin privileges for leaderboard management
        /// </summary>
        public async Task<bool> HasLeaderboardAdminPrivilegesAsync(ulong userId)
        {
            try
            {
                // We'll delegate this to the season service since it already has a robust permission system
                // Both season and leaderboard management should have the same level of privileges
                return await _seasonStateService.HasSeasonAdminPrivilegesAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking leaderboard admin privileges for user {userId}");
                return false;
            }
        }
    }
}