using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles deck submission-related component interactions
    /// </summary>
    public class DeckSubmissionHandler : ComponentHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITournamentManagerService _tournamentManagerService;
        private readonly IMatchStatusService _matchStatusService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="scopeFactory">Factory for creating service scopes</param>
        /// <param name="tournamentManagerService">Service for managing tournaments</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        public DeckSubmissionHandler(
            ILogger<DeckSubmissionHandler> logger,
            ITournamentStateService stateService,
            OngoingRounds roundsHolder,
            IServiceScopeFactory scopeFactory,
            ITournamentManagerService tournamentManagerService,
            IMatchStatusService matchStatusService)
            : base(logger, stateService)
        {
            _roundsHolder = roundsHolder;
            _scopeFactory = scopeFactory;
            _tournamentManagerService = tournamentManagerService;
            _matchStatusService = matchStatusService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("confirm_deck_") ||
                   customId.StartsWith("revise_deck_") ||
                   customId == "submit_deck_button";
        }

        /// <summary>
        /// Handles deck submission-related component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            _logger.LogInformation("Handling deck submission component: {ComponentId}", e.Id);

            // Handle different deck-related buttons
            if (e.Id.StartsWith("confirm_deck_"))
            {
                await HandleConfirmDeckButton(client, e, hasBeenDeferred);
            }
            else if (e.Id.StartsWith("revise_deck_"))
            {
                await HandleReviseDeckButton(client, e, hasBeenDeferred);
            }
            else if (e.Id == "submit_deck_button")
            {
                await HandleSubmitDeckButton(client, e, hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles the submit deck button interaction
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleSubmitDeckButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Try to acknowledge the interaction if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // Find the tournament round and user's team/participant info
                var currentRound = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                    r.Teams?.Any(t => t.Thread?.Id == e.Channel.Id) == true);

                if (currentRound == null)
                {
                    await SendErrorResponseAsync(e, "No active tournament round found for this channel.", hasBeenDeferred);
                    return;
                }

                // Check if we're in the correct stage for deck submission
                if (currentRound.CurrentStage != MatchStage.DeckSubmission)
                {
                    string errorMessage = currentRound.CurrentStage == MatchStage.MapBan
                        ? "Deck submission is not yet available. Please complete the map ban stage first."
                        : currentRound.CurrentStage == MatchStage.GameResults
                            ? "Deck submission is no longer available. The match has progressed to game results."
                            : "Deck submission is not available at this time.";

                    await SendErrorResponseAsync(e, errorMessage, hasBeenDeferred);
                    return;
                }

                // Get the team information
                var currentTeam = currentRound.Teams?.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                if (currentTeam == null)
                {
                    await SendErrorResponseAsync(e, "Could not find your team in this channel.", hasBeenDeferred);
                    return;
                }

                // Check if user is a participant
                if (!currentTeam.Participants?.Any(p => p.Player?.Id == e.User.Id) == true)
                {
                    await SendErrorResponseAsync(e, "You are not a participant in this tournament round.", hasBeenDeferred);
                    return;
                }

                // Delete the message with the button
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete message with button");
                }

                // Send the text-based deck submission prompt
                DiscordMessage promptMessage = await e.Channel.SendMessageAsync($"{e.User.Mention} Please use the `/tournament submit_deck` command to submit your deck code.");

                // Auto-delete after a reasonable time
                await AutoDeleteMessageAsync(promptMessage, 60);

                // Notify the user that their deck has been submitted and is ready for confirmation
                if (hasBeenDeferred)
                {
                    // Use SendResponseAsync for success messages with green color
                    await SendResponseAsync(e,
                        "Your deck has been submitted! Please confirm or revise your selection.",
                        true,
                        DiscordColor.Green);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling submit deck button");
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles confirm deck button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleConfirmDeckButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Check if the interaction is already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // Extract the user ID from the button ID
                string userIdStr = e.Id.Replace("confirm_deck_", "");
                if (!ulong.TryParse(userIdStr, out ulong userId))
                {
                    _logger.LogError("Failed to parse user ID from confirm_deck button: {UserId}", userIdStr);
                    await SendErrorResponseAsync(e, "Error processing deck confirmation: Invalid user ID", hasBeenDeferred);
                    return;
                }

                // Only allow the user who submitted the deck to confirm it
                if (e.User.Id != userId)
                {
                    await SendErrorResponseAsync(e, "Only the user who submitted the deck can confirm it.", hasBeenDeferred);
                    return;
                }

                // Get channel, round, and participant info
                var channel = e.Channel;
                if (channel is null)
                {
                    _logger.LogError("Channel is null for deck confirmation");
                    await SendErrorResponseAsync(e, "Error processing deck confirmation: Channel not found", hasBeenDeferred);
                    return;
                }

                // Find the round and participant
                var round = _roundsHolder.GetRoundByThreadIdOrDefault(channel.Id);
                if (round == null)
                {
                    _logger.LogError("Could not find round for channel {ChannelId}", channel.Id);
                    await SendErrorResponseAsync(e, "Error processing deck confirmation: Match not found", hasBeenDeferred);
                    return;
                }

                // Call the MatchStatusService to handle the deck confirmation
                await _matchStatusService.ConfirmDeckAsync(channel, round, userId, client);

                // Get TournamentMatchService to handle deck submission
                using (var scope = _scopeFactory.CreateScope())
                {
                    var tournamentMatchService = scope.ServiceProvider.GetRequiredService<ITournamentMatchService>();

                    // Call HandleDeckSubmissionAsync to manage map selection and other game logic
                    try
                    {
                        // The HandleDeckSubmissionAsync method checks if all decks are submitted
                        // and handles map selection and preparation for the next stage
                        await tournamentMatchService.HandleDeckSubmissionAsync(round, channel, client);
                        _logger.LogInformation($"TournamentMatchService.HandleDeckSubmissionAsync called for channel {channel.Id}");
                    }
                    catch (Exception matchEx)
                    {
                        _logger.LogError(matchEx, "Error while handling deck submission via TournamentMatchService");
                        // Continue anyway - the match status should still be updated
                    }
                }

                // Delete the confirmation message to reduce clutter
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete the confirmation message");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming deck");
                await SendErrorResponseAsync(e, $"There was an error confirming your deck: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles revise deck button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleReviseDeckButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Only try to defer if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // Extract the user ID from the button ID
                string userIdStr = e.Id.Replace("revise_deck_", "");
                if (!ulong.TryParse(userIdStr, out ulong userId))
                {
                    _logger.LogError("Failed to parse user ID from revise_deck button: {UserId}", userIdStr);
                    await SendErrorResponseAsync(e, "Error processing deck revision: Invalid user ID", hasBeenDeferred);
                    return;
                }

                // Only allow the user who submitted the deck to revise it
                if (e.User.Id != userId)
                {
                    await SendErrorResponseAsync(e, "Only the user who submitted the deck can revise it.", hasBeenDeferred);
                    return;
                }

                // Delete the old confirmation message to reduce clutter
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete the confirmation message");
                }

                // Find the round
                var channel = e.Channel;
                if (channel is null)
                {
                    _logger.LogError("Channel is null for deck revision");
                    await SendErrorResponseAsync(e, "Error processing deck revision: Channel not found", hasBeenDeferred);
                    return;
                }

                var round = _roundsHolder.GetRoundByThreadIdOrDefault(channel.Id);
                if (round != null)
                {
                    // Call the MatchStatusService to handle the deck revision
                    await _matchStatusService.ReviseDeckAsync(channel, round, userId, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising deck");
                await SendErrorResponseAsync(e, $"There was an error revising your deck: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}