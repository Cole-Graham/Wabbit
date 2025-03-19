using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.Collections.Generic;

namespace Wabbit.BotClient.Events.Components
{
    /// <summary>
    /// Handler for leaderboard component interactions
    /// </summary>
    public class LeaderboardHandler : ComponentHandlerBase
    {
        private readonly ILeaderboardService _leaderboardService;
        private readonly string[] _prefixes = { "leaderboard_" };

        public LeaderboardHandler(
            ILogger<LeaderboardHandler> logger,
            ITournamentStateService stateService,
            ILeaderboardService leaderboardService)
            : base(logger, stateService)
        {
            _leaderboardService = leaderboardService;
        }

        /// <inheritdoc />
        public override bool CanHandle(string customId)
        {
            return _prefixes.Any(prefix => customId.StartsWith(prefix));
        }

        /// <inheritdoc />
        public override async Task HandleAsync(
            DiscordClient client,
            ComponentInteractionCreatedEventArgs args,
            bool hasBeenDeferred)
        {
            if (!hasBeenDeferred)
            {
                await SafeDeferAsync(args.Interaction);
            }

            try
            {
                if (args.Id.StartsWith("leaderboard_first_") ||
                    args.Id.StartsWith("leaderboard_prev_") ||
                    args.Id.StartsWith("leaderboard_next_") ||
                    args.Id.StartsWith("leaderboard_last_"))
                {
                    await HandlePaginationAsync(client, args);
                }
                else if (args.Id.StartsWith("leaderboard_gametype_"))
                {
                    await HandleGameTypeChangeAsync(client, args);
                }
                else if (args.Id.StartsWith("leaderboard_season_"))
                {
                    await HandleSeasonChangeAsync(client, args);
                }
                else
                {
                    _logger.LogWarning($"Unknown leaderboard component ID: {args.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling leaderboard component interaction: {args.Id}");

                // Try to send an error message
                try
                {
                    await SendErrorResponseAsync(args, "An error occurred while processing your request. Please try again later.", hasBeenDeferred);
                }
                catch
                {
                    // Ignore failures in error handling
                }
            }
        }

        /// <summary>
        /// Handle pagination button presses
        /// </summary>
        private async Task HandlePaginationAsync(DiscordClient client, ComponentInteractionCreatedEventArgs args)
        {
            // Format: leaderboard_[action]_[gameType]_[seasonId]
            var parts = args.Id.Split('_');
            if (parts.Length < 4)
            {
                _logger.LogWarning($"Invalid pagination component ID format: {args.Id}");
                return;
            }

            string action = parts[1];
            string gameTypeStr = parts[2];
            string? seasonId = parts[3];

            // Parse game type
            if (!Enum.TryParse<TeamGameType>(gameTypeStr, out var gameType))
            {
                _logger.LogWarning($"Invalid game type in component ID: {gameTypeStr}");
                return;
            }

            // Use "current" for null season ID
            if (seasonId == "current")
            {
                seasonId = null;
            }

            // Get current page from button custom ID in the original message
            // The format of the button ID contains the current page: leaderboard_[action]_[gameType]_[seasonId]_page[pageNum]
            int currentPage = 1;

            // Flatten all components to find the buttons
            var allButtons = new List<DiscordButtonComponent>();
            if (args.Message.Components != null)
            {
                foreach (var actionRow in args.Message.Components)
                {
                    if (actionRow is DiscordActionRowComponent row)
                    {
                        foreach (var component in row.Components)
                        {
                            if (component is DiscordButtonComponent button)
                            {
                                allButtons.Add(button);
                            }
                        }
                    }
                }
            }

            var currentPageButton = allButtons
                .FirstOrDefault(b => b.CustomId.StartsWith("leaderboard_next_") || b.CustomId.StartsWith("leaderboard_prev_"));

            if (currentPageButton != null && currentPageButton.CustomId.Contains("_page"))
            {
                var pagePart = currentPageButton.CustomId.Split("_page").Last();
                int.TryParse(pagePart, out currentPage);
            }

            // Calculate new page
            int newPage = action switch
            {
                "first" => 1,
                "prev" => Math.Max(1, currentPage - 1),
                "next" => currentPage + 1,
                "last" => 999, // We'll let the service determine the actual last page
                _ => 1
            };

            // Update the leaderboard
            await _leaderboardService.UpdateLeaderboardAsync(args.Message, gameType, seasonId, newPage);
        }

        /// <summary>
        /// Handle game type button presses
        /// </summary>
        private async Task HandleGameTypeChangeAsync(DiscordClient client, ComponentInteractionCreatedEventArgs args)
        {
            // Format: leaderboard_gametype_[gameType]
            var parts = args.Id.Split('_');
            if (parts.Length < 3)
            {
                _logger.LogWarning($"Invalid game type component ID format: {args.Id}");
                return;
            }

            string gameTypeStr = parts[2];

            // Parse game type
            if (!Enum.TryParse<TeamGameType>(gameTypeStr, out var gameType))
            {
                _logger.LogWarning($"Invalid game type in component ID: {gameTypeStr}");
                return;
            }

            // Get current season ID from select menu
            string? seasonId = null;

            // Flatten all components to find the select menu
            var allSelectMenus = new List<DiscordSelectComponent>();
            if (args.Message.Components != null)
            {
                foreach (var actionRow in args.Message.Components)
                {
                    if (actionRow is DiscordActionRowComponent row)
                    {
                        foreach (var component in row.Components)
                        {
                            if (component is DiscordSelectComponent select)
                            {
                                allSelectMenus.Add(select);
                            }
                        }
                    }
                }
            }

            var seasonSelector = allSelectMenus
                .FirstOrDefault(s => s.CustomId.StartsWith("leaderboard_season_"));

            if (seasonSelector != null && seasonSelector.Options.Any(o => o.Default))
            {
                var selectedOption = seasonSelector.Options.First(o => o.Default);
                if (selectedOption.Value != "current")
                {
                    seasonId = selectedOption.Value;
                }
            }

            // Update the leaderboard with the new game type
            await _leaderboardService.UpdateLeaderboardAsync(args.Message, gameType, seasonId);
        }

        /// <summary>
        /// Handle season select menu changes
        /// </summary>
        private async Task HandleSeasonChangeAsync(DiscordClient client, ComponentInteractionCreatedEventArgs args)
        {
            // Format from select menu: leaderboard_season_[gameType]
            var parts = args.Id.Split('_');
            if (parts.Length < 3)
            {
                _logger.LogWarning($"Invalid season component ID format: {args.Id}");
                return;
            }

            string gameTypeStr = parts[2];

            // Parse game type
            if (!Enum.TryParse<TeamGameType>(gameTypeStr, out var gameType))
            {
                _logger.LogWarning($"Invalid game type in component ID: {gameTypeStr}");
                return;
            }

            // Get selected season ID
            string? seasonId = null;
            if (args.Values != null && args.Values.Any())
            {
                var selectedValue = args.Values[0];
                if (selectedValue != "current")
                {
                    seasonId = selectedValue;
                }
            }

            // Update the leaderboard with the new season
            await _leaderboardService.UpdateLeaderboardAsync(args.Message, gameType, seasonId);
        }
    }
}