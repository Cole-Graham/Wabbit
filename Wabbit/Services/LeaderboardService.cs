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
        /// Initializes a new instance of the LeaderboardService class
        /// </summary>
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

        /// <summary>
        /// Get player and team rankings for a specific game type and season
        /// </summary>
        public async Task<(List<LeaderboardEntry> Rankings, int TotalPages, int TotalEntries)> GetLeaderboardAsync(
            Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1, int itemsPerPage = 25)
        {
            // Default to current season if not specified
            if (seasonId == null)
            {
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                seasonId = currentSeason?.SeasonId ?? "current";
            }

            // Check if we have cached data
            bool isCached = IsCached(seasonId, gameType);
            List<LeaderboardEntry> allEntries;

            if (!isCached)
            {
                // Generate leaderboard data
                allEntries = await GenerateLeaderboardDataAsync(gameType, seasonId);

                // Cache the results
                CacheLeaderboard(seasonId, gameType, allEntries);
            }
            else
            {
                // Use cached data
                allEntries = _cachedLeaderboards[seasonId][gameType];
            }

            // Calculate pagination
            int totalEntries = allEntries.Count;
            int totalPages = (int)Math.Ceiling(totalEntries / (double)itemsPerPage);

            // Adjust page if it's out of range
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            // Get the entries for the requested page
            var pagedEntries = allEntries
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();

            return (pagedEntries, totalPages, totalEntries);
        }

        /// <summary>
        /// Create a leaderboard embed for display
        /// </summary>
        public async Task<(DiscordEmbed LeaderboardEmbed, DiscordButtonComponent[] NavigationButtons, DiscordSelectComponent SeasonSelector)>
            CreateLeaderboardEmbedAsync(Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1, int itemsPerPage = 25)
        {
            // Get leaderboard data
            var (rankings, totalPages, totalEntries) = await GetLeaderboardAsync(gameType, seasonId, page, itemsPerPage);

            // Get season information
            string seasonName = "Current Season";
            if (seasonId != null && seasonId != "current")
            {
                var season = await _seasonStateService.GetSeasonByIdAsync(seasonId);
                seasonName = season?.SeasonName ?? "Unknown Season";
            }

            // Create embed
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"{GetGameTypeDisplayName(gameType)} Leaderboard - {seasonName}")
                .WithDescription($"Page {page} of {totalPages} • {totalEntries} entries")
                .WithColor(DiscordColor.Blurple)
                .WithFooter($"Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

            // Add rankings to embed
            if (rankings.Count == 0)
            {
                embed.AddField("No Entries", "There are no rankings to display for this game type.", false);
            }
            else
            {
                var playerSection = new List<string>();
                var teamSection = new List<string>();

                foreach (var entry in rankings)
                {
                    var line = $"{GetRankEmoji(entry.Rank)} **{entry.DisplayName}** • {entry.Rating} RP • " +
                        $"{entry.Wins}W {entry.Losses}L ({entry.WinRate:0.0}%)";

                    if (entry.IsTeam)
                        teamSection.Add(line);
                    else
                        playerSection.Add(line);
                }

                if (playerSection.Any())
                {
                    embed.AddField("Players", string.Join('\n', playerSection), false);
                }

                if (teamSection.Any())
                {
                    embed.AddField("Teams", string.Join('\n', teamSection), false);
                }
            }

            // Create navigation buttons
            var navigationButtons = CreateNavigationButtons(gameType, seasonId, page, totalPages);

            // Create season selector
            var seasons = await _seasonStateService.GetAllSeasonsAsync();
            var seasonSelector = new DiscordSelectComponent(
                $"leaderboard_season_{gameType}",
                "Select Season",
                seasons.Select(s => new DiscordSelectComponentOption(
                    s.SeasonName,
                    s.SeasonId,
                    "",
                    s.SeasonId == seasonId)).Append(
                        new DiscordSelectComponentOption(
                            "Current Season",
                            "current",
                            "",
                            seasonId == null || seasonId == "current")).ToArray());

            return (embed.Build(), navigationButtons, seasonSelector);
        }

        /// <summary>
        /// Post a leaderboard to a channel
        /// </summary>
        public async Task<DiscordMessage> PostLeaderboardAsync(DiscordChannel channel, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            var (embed, navigationButtons, seasonSelector) = await CreateLeaderboardEmbedAsync(gameType, seasonId);

            // Create game type buttons
            var gameTypeButtons = CreateGameTypeButtons(gameType);

            // Build the message
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embed)
                .AddComponents(seasonSelector)
                .AddComponents(gameTypeButtons)
                .AddComponents(navigationButtons);

            // Send the message
            return await channel.SendMessageAsync(messageBuilder);
        }

        /// <summary>
        /// Update an existing leaderboard message
        /// </summary>
        public async Task<DiscordMessage> UpdateLeaderboardAsync(DiscordMessage message, Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1)
        {
            var (embed, navigationButtons, seasonSelector) = await CreateLeaderboardEmbedAsync(gameType, seasonId, page);

            // Create game type buttons
            var gameTypeButtons = CreateGameTypeButtons(gameType);

            // Build the message
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embed)
                .AddComponents(seasonSelector)
                .AddComponents(gameTypeButtons)
                .AddComponents(navigationButtons);

            // Update the message
            return await message.ModifyAsync(messageBuilder);
        }

        /// <summary>
        /// Get player ranking for a specific player
        /// </summary>
        public async Task<LeaderboardEntry?> GetPlayerRankingAsync(ulong playerId, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            // Get the full leaderboard
            var (rankings, _, _) = await GetLeaderboardAsync(gameType, seasonId);

            // Find the player entry
            return rankings.FirstOrDefault(e => !e.IsTeam && e.PlayerId == playerId);
        }

        /// <summary>
        /// Get team ranking for a specific team
        /// </summary>
        public async Task<LeaderboardEntry?> GetTeamRankingAsync(string teamId, Wabbit.Models.TeamGameType gameType, string? seasonId = null)
        {
            // Get the full leaderboard
            var (rankings, _, _) = await GetLeaderboardAsync(gameType, seasonId);

            // Find the team entry
            return rankings.FirstOrDefault(e => e.IsTeam && e.TeamId == teamId);
        }

        /// <summary>
        /// Generate leaderboard data from player and team ratings
        /// </summary>
        private async Task<List<LeaderboardEntry>> GenerateLeaderboardDataAsync(Wabbit.Models.TeamGameType gameType, string seasonId)
        {
            var entries = new List<LeaderboardEntry>();

            // Get all player ratings
            var playerRatings = await _playerRatingRepository.GetAllPlayerRatingsAsync();

            // Add player entries
            int playerRank = 1;
            foreach (var player in playerRatings
                .Where(p => GetPlayerRating(p, gameType) > 0 || (GetPlayerWins(p, gameType) + GetPlayerLosses(p, gameType)) > 0)
                .OrderByDescending(p => GetPlayerRating(p, gameType)))
            {
                entries.Add(new LeaderboardEntry
                {
                    Rank = playerRank++,
                    IsTeam = false,
                    PlayerId = player.PlayerId,
                    PlayerUsername = player.Username,
                    Rating = GetPlayerRating(player, gameType),
                    Wins = GetPlayerWins(player, gameType),
                    Losses = GetPlayerLosses(player, gameType),
                    RatingChange = 0 // TODO: Calculate rating change
                });
            }

            // Add team entries
            // Get teams of this type
            var teams = await _teamStateService.GetTopTeamsByRatingAsync(gameType, 100);

            // Add team entries
            int teamRank = 1;
            foreach (var team in teams
                .OrderByDescending(t => t.Rating))
            {
                entries.Add(new LeaderboardEntry
                {
                    Rank = teamRank++,
                    IsTeam = true,
                    TeamId = team.TeamId,
                    TeamName = team.TeamName,
                    Rating = team.Rating,
                    Wins = team.Wins,
                    Losses = team.Losses,
                    RatingChange = 0 // TODO: Calculate rating change
                });
            }

            // Sort all entries by rating
            return entries.OrderByDescending(e => e.Rating).Select((e, i) => { e.Rank = i + 1; return e; }).ToList();
        }

        /// <summary>
        /// Get player rating for a specific game type
        /// </summary>
        private int GetPlayerRating(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            // This assumes PlayerRating has a method to get rating by game type
            return player.GetRating(gameType);
        }

        /// <summary>
        /// Get player wins for a specific game type
        /// </summary>
        private int GetPlayerWins(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            return player.Wins.TryGetValue(gameType, out int wins) ? wins : 0;
        }

        /// <summary>
        /// Get player losses for a specific game type
        /// </summary>
        private int GetPlayerLosses(PlayerRating player, Wabbit.Models.TeamGameType gameType)
        {
            return player.Losses.TryGetValue(gameType, out int losses) ? losses : 0;
        }

        /// <summary>
        /// Check if leaderboard data is cached and not expired
        /// </summary>
        private bool IsCached(string seasonId, Wabbit.Models.TeamGameType gameType)
        {
            // Check if we have a cache for this season
            if (!_cachedLeaderboards.TryGetValue(seasonId, out var typeCache))
                return false;

            // Check if we have a cache for this game type
            if (!typeCache.ContainsKey(gameType))
                return false;

            // Check if cache has expired
            if (!_cacheExpiration.TryGetValue(seasonId, out var expiration))
                return false;

            return DateTimeOffset.UtcNow < expiration;
        }

        /// <summary>
        /// Cache leaderboard data
        /// </summary>
        private void CacheLeaderboard(string seasonId, Wabbit.Models.TeamGameType gameType, List<LeaderboardEntry> entries)
        {
            // Ensure we have a dictionary for this season
            if (!_cachedLeaderboards.TryGetValue(seasonId, out var typeCache))
            {
                typeCache = new Dictionary<Wabbit.Models.TeamGameType, List<LeaderboardEntry>>();
                _cachedLeaderboards[seasonId] = typeCache;
            }

            // Store the entries
            typeCache[gameType] = entries;

            // Set expiration
            _cacheExpiration[seasonId] = DateTimeOffset.UtcNow.Add(_cacheDuration);
        }

        /// <summary>
        /// Get a user-friendly display name for a game type
        /// </summary>
        private string GetGameTypeDisplayName(Wabbit.Models.TeamGameType gameType)
        {
            return GameTypeHelpers.GetDisplayName((Wabbit.Models.GameType)(int)gameType);
        }

        /// <summary>
        /// Get emoji for top 3 ranks
        /// </summary>
        private string GetRankEmoji(int rank)
        {
            return rank switch
            {
                1 => "🥇 1",
                2 => "🥈 2",
                3 => "🥉 3",
                _ => rank.ToString()
            };
        }

        /// <summary>
        /// Create navigation buttons
        /// </summary>
        private DiscordButtonComponent[] CreateNavigationButtons(Wabbit.Models.TeamGameType gameType, string? seasonId, int page, int totalPages)
        {
            // Default to "current" for null season ID
            string seasonIdParam = seasonId ?? "current";

            // Create navigation buttons
            var firstPageButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_first_{gameType}_{seasonIdParam}",
                "⏮️",
                disabled: page <= 1);

            var prevPageButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_prev_{gameType}_{seasonIdParam}_page{page}",
                "◀️",
                disabled: page <= 1);

            var nextPageButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_next_{gameType}_{seasonIdParam}_page{page}",
                "▶️",
                disabled: page >= totalPages);

            var lastPageButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"leaderboard_last_{gameType}_{seasonIdParam}",
                "⏭️",
                disabled: page >= totalPages);

            return new[] { firstPageButton, prevPageButton, nextPageButton, lastPageButton };
        }

        /// <summary>
        /// Create game type buttons
        /// </summary>
        private DiscordButtonComponent[] CreateGameTypeButtons(Wabbit.Models.TeamGameType currentGameType)
        {
            var oneVOneButton = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.OneVOne ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_{Wabbit.Models.TeamGameType.OneVOne}",
                "1v1");

            var twoVTwoButton = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.TwoVTwo ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_{Wabbit.Models.TeamGameType.TwoVTwo}",
                "2v2");

            var threeVThreeButton = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.ThreeVThree ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_{Wabbit.Models.TeamGameType.ThreeVThree}",
                "3v3");

            var fourVFourButton = new DiscordButtonComponent(
                currentGameType == Wabbit.Models.TeamGameType.FourVFour ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary,
                $"leaderboard_gametype_{Wabbit.Models.TeamGameType.FourVFour}",
                "4v4");

            return new[] { oneVOneButton, twoVTwoButton, threeVThreeButton, fourVFourButton };
        }
    }
}