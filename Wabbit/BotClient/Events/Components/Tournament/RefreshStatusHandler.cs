using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles refresh button interactions for match status embeds
    /// </summary>
    public class RefreshStatusHandler : ComponentHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly IMatchStatusService _matchStatusService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        public RefreshStatusHandler(
            ILogger<RefreshStatusHandler> logger,
            ITournamentStateService stateService,
            OngoingRounds roundsHolder,
            IMatchStatusService matchStatusService)
            : base(logger, stateService)
        {
            _roundsHolder = roundsHolder;
            _matchStatusService = matchStatusService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("refresh_status_");
        }

        /// <summary>
        /// Handles refresh button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Extract the round ID from the custom ID
                string roundId = e.Id.Replace("refresh_status_", "");

                // Check for cooldown before proceeding
                if (_matchStatusService.IsRefreshButtonOnCooldown(roundId, e.User.Id))
                {
                    // User is on cooldown, send ephemeral message
                    if (!hasBeenDeferred)
                    {
                        await e.Interaction.CreateResponseAsync(
                            DiscordInteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder()
                                .WithContent("Please wait a moment before refreshing again.")
                                .AsEphemeral(true));
                    }
                    else
                    {
                        // If already deferred, we need to send a follow-up
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("Please wait a moment before refreshing again.")
                                .AsEphemeral(true));
                    }

                    _logger.LogInformation($"Refresh cooldown triggered for user {e.User.Username} (ID: {e.User.Id})");
                    return;
                }

                // Try to acknowledge the interaction if it hasn't been already
                if (!hasBeenDeferred)
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        // Interaction might already be acknowledged
                        _logger.LogWarning(ex, "Could not defer interaction in RefreshStatusHandler");
                    }
                }

                // Find the round by ID
                var round = _roundsHolder.TourneyRounds.FirstOrDefault(r => r.Id == roundId);
                if (round is null)
                {
                    _logger.LogWarning($"Could not find round with ID {roundId} for status refresh");
                    await SendErrorResponseAsync(e, "Could not find the associated match", hasBeenDeferred);
                    return;
                }

                // Update the match status in this channel
                await _matchStatusService.UpdateMatchStatusAsync(e.Channel, round, client);

                // Log the refresh action
                _logger.LogInformation($"Match status refreshed by {e.User.Username} in channel {e.Channel.Id} for round {round.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RefreshStatusHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred while refreshing status: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}