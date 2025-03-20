using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Data;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;

namespace Wabbit.BotClient.Events.Components.Scrimmage
{
    /// <summary>
    /// Handles scrimmage-related component interactions
    /// </summary>
    public class ScrimmageComponentHandler : ComponentHandlerBase
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly IScrimmageStatusService _scrimmageStatusService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        public ScrimmageComponentHandler(
            ILogger<ScrimmageComponentHandler> logger,
            ITournamentStateService stateService,
            OngoingRounds ongoingRounds,
            IScrimmageStatusService scrimmageStatusService)
            : base(logger, stateService)
        {
            _ongoingRounds = ongoingRounds;
            _scrimmageStatusService = scrimmageStatusService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("map_ban_") ||
                   customId.StartsWith("confirm_map_bans_") ||
                   customId.StartsWith("revise_map_bans_") ||
                   customId.StartsWith("select_maps_") ||
                   customId.StartsWith("submit_deck_") ||
                   customId.StartsWith("ready_up_") ||
                   customId.StartsWith("refresh_status_") ||
                   customId.StartsWith("report_win_");
        }

        /// <summary>
        /// Handles scrimmage-related component interactions
        /// </summary>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                if (e.Channel is null)
                {
                    await SendErrorResponseAsync(e, "Invalid channel context for scrimmage interaction", hasBeenDeferred);
                    return;
                }

                // Get the scrimmage for this thread
                var scrimmage = await _scrimmageStatusService.GetScrimmageByThreadIdAsync(e.Channel.Id);
                if (scrimmage == null)
                {
                    await SendErrorResponseAsync(e, "Could not find an active scrimmage in this thread", hasBeenDeferred);
                    return;
                }

                // First defer the interaction before doing any processing
                if (!hasBeenDeferred)
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not defer interaction");
                        // Continue anyway, the interaction might be already deferred
                    }
                }

                // Handle different button types
                if (e.Id.StartsWith("refresh_status_"))
                {
                    await _scrimmageStatusService.UpdateScrimmageStatusAsync(scrimmage);
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Status refreshed!")
                        .AsEphemeral(true));
                }
                else if (e.Id.StartsWith("report_win_"))
                {
                    // Extract winning team number from component ID
                    string winningTeamStr = e.Id.Replace("report_win_", "").Split('_')[0];
                    if (int.TryParse(winningTeamStr, out int winningTeam) && (winningTeam == 1 || winningTeam == 2))
                    {
                        await _scrimmageStatusService.RecordGameResultAsync(scrimmage, winningTeam);
                        await _scrimmageStatusService.UpdateScrimmageStatusAsync(scrimmage);
                    }
                }
                // Add more handlers as implementation progresses
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScrimmageComponentHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}