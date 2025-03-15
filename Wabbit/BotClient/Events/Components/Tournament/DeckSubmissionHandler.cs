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

                // Clean up map ban messages
                await CleanupMapBanMessages(e.Channel);

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
                // Only try to defer if not already deferred
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

                // Find the tournament round
                var round = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                    r.Teams is not null &&
                    r.Teams.Any(t => t.Thread?.Id == e.Channel.Id));

                if (round == null)
                {
                    await SendErrorResponseAsync(e, "Could not find an active tournament round for this channel.", hasBeenDeferred);
                    return;
                }

                // Find the team and participant
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                if (team == null)
                {
                    await SendErrorResponseAsync(e, "Could not find your team data.", hasBeenDeferred);
                    return;
                }

                var participant = team.Participants?.FirstOrDefault(p =>
                    p is not null && p.Player is not null && p.Player.Id == userId);

                if (participant == null || participant.TempDeckCode == null)
                {
                    await SendErrorResponseAsync(e, "Could not find your participant data or temporary deck code.", hasBeenDeferred);
                    return;
                }

                // Only allow the user who submitted the deck to confirm it
                if (e.User.Id != userId)
                {
                    await SendErrorResponseAsync(e, "Only the user who submitted the deck can confirm it.", hasBeenDeferred);
                    return;
                }

                // Store the confirmed deck code
                participant.Deck = participant.TempDeckCode;

                // Also store in deck history with the current map if available
                if (round.Maps != null && round.Maps.Count > 0 && round.Cycle < round.Maps.Count)
                {
                    // Get the current map based on the cycle
                    int mapIndex = Math.Min(round.Cycle, round.Maps.Count - 1);
                    if (mapIndex >= 0 && mapIndex < round.Maps.Count)
                    {
                        string mapName = round.Maps[mapIndex];
                        if (participant.DeckHistory == null)
                        {
                            participant.DeckHistory = new Dictionary<string, string>();
                        }
                        participant.DeckHistory[mapName] = participant.Deck;
                    }
                }

                // Clear temporary deck code
                participant.TempDeckCode = null;

                // Update the message to show it's been confirmed
                var confirmedEmbed = new DiscordEmbedBuilder()
                    .WithTitle("Deck Code Confirmed")
                    .WithDescription("Your deck code has been successfully confirmed and submitted.")
                    .WithColor(DiscordColor.Green);

                // Send a confirmation message using the appropriate method
                DiscordMessage? confirmedMessage = null;
                if (hasBeenDeferred)
                {
                    var response = await e.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder()
                            .WithContent($"{e.User.Mention} Your deck code has been confirmed!")
                            .AddEmbed(confirmedEmbed));

                    // Get the actual message for auto-deletion
                    if (e.Channel is not null)
                    {
                        try
                        {
                            confirmedMessage = await e.Channel.GetMessageAsync(response.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get message for auto-deletion");
                        }
                    }
                }
                else
                {
                    confirmedMessage = await e.Channel.SendMessageAsync(
                        new DiscordMessageBuilder()
                            .WithContent($"**{e.User.Username}** has confirmed their deck submission!")
                            .AddEmbed(confirmedEmbed));
                }

                // Don't auto-delete the confirmation message - this is important tournament information
                // that should remain visible to all participants

                // Delete the confirmation message with the buttons (this is the message with the confirm/revise buttons)
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete confirmation message");
                }

                // Save both tournament state and tournament data files
                // This ensures deck codes are preserved in both runtime and persistent storage
                bool saveSuccess = await _stateService.SafeSaveTournamentStateAsync(client, "DeckSubmissionHandler.HandleConfirmDeckButton");
                if (!saveSuccess)
                {
                    _logger.LogWarning("Failed to save tournament state after deck confirmation. " +
                        "The deck has been confirmed but may not persist through a restart.");
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    try
                    {
                        var tournamentManager = scope.ServiceProvider.GetRequiredService<ITournamentManagerService>();
                        await tournamentManager.SaveAllDataAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save tournament data after deck confirmation");
                    }
                }

                // Check if all participants have submitted their decks
                bool allSubmitted = round.Teams?.All(t =>
                    t.Participants is not null &&
                    t.Participants.All(p => p is not null && !string.IsNullOrEmpty(p.Deck))) ?? false;

                if (allSubmitted)
                {
                    // All decks submitted - set InGame to true
                    round.InGame = true;

                    // Notify players
                    foreach (var t in round.Teams ?? new List<Round.Team>())
                    {
                        if (t.Thread is not null)
                        {
                            await t.Thread.SendMessageAsync("**All decks have been submitted!** The game will now proceed.");
                        }
                    }

                    // Save both tournament state and tournament data files
                    saveSuccess = await _stateService.SafeSaveTournamentStateAsync(client, "DeckSubmissionHandler.HandleConfirmDeckButton (all decks submitted)");
                    if (!saveSuccess)
                    {
                        _logger.LogError("Failed to save tournament state after all decks were submitted. " +
                            "This may affect match progression.");
                    }

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        try
                        {
                            var tournamentManager = scope.ServiceProvider.GetRequiredService<ITournamentManagerService>();
                            await tournamentManager.SaveAllDataAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to save tournament data after all decks were submitted");
                        }
                    }
                }

                // Update match status with the new deck submission
                try
                {
                    if (e.Channel is not null && participant?.Deck != null)
                    {
                        await _matchStatusService.RecordDeckSubmissionAsync(
                            e.Channel,
                            round,
                            e.User.Id,
                            participant.Deck,
                            round.Cycle,
                            client);

                        string username = participant.Player?.Username ?? "Unknown Player";
                        _logger.LogInformation(
                            $"Updated match status for deck submission by {username} in thread {e.Channel.Id}");
                    }
                }
                catch (Exception ex)
                {
                    ulong channelId = e.Channel?.Id ?? 0;
                    _logger.LogError(ex, $"Failed to update match status for deck submission in thread {channelId}");
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

                // Clean up any existing deck submission prompts in the channel
                await CleanupDeckSubmissionMessages(e.Channel, userId);

                // Prompt the user to submit a new deck code
                var promptContent = $"{e.User.Mention} Please use the `/tournament submit_deck` command to submit your revised deck code.\n\n" +
                                  "After submitting, you'll be able to review and confirm your deck code.";

                // Send the message directly with the string content
                var reviseDeckMessage = await e.Channel.SendMessageAsync(promptContent);

                // Auto-delete the revision prompt after 10 seconds
                await AutoDeleteMessageAsync(reviseDeckMessage, 10);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising deck");
                await SendErrorResponseAsync(e, $"There was an error revising your deck: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Cleans up deck submission related messages in a channel for a specific user
        /// </summary>
        /// <param name="channel">The Discord channel</param>
        /// <param name="userId">The user ID to clean up messages for</param>
        private async Task CleanupDeckSubmissionMessages(DiscordChannel channel, ulong userId)
        {
            try
            {
                // Get recent messages in the channel
                var messages = channel.GetMessagesAsync(50);
                var messageList = new List<DiscordMessage>();

                // Manually collect messages from the async enumerable
                await foreach (var message in messages)
                {
                    messageList.Add(message);
                }

                // Find and delete deck submission related messages for this user
                var messagesToDelete = messageList.Where(m =>
                    (m.Author?.IsBot == true && m.Content?.Contains("Please enter your") == true && m.Content?.Contains(userId.ToString()) == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Please review your deck code") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("deck code submission") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("map ban selections") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Please submit your deck code") == true)
                ).ToList();

                foreach (var message in messagesToDelete)
                {
                    try
                    {
                        await message.DeleteAsync();
                        // Add a small delay to avoid rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete message during cleanup");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up deck submission messages");
            }
        }

        /// <summary>
        /// Cleans up map ban related messages in a channel
        /// </summary>
        /// <param name="channel">The Discord channel</param>
        private async Task CleanupMapBanMessages(DiscordChannel channel)
        {
            try
            {
                // Get recent messages in the channel
                var messages = channel.GetMessagesAsync(50);
                var messageList = new List<DiscordMessage>();

                // Manually collect messages from the async enumerable
                await foreach (var message in messages)
                {
                    messageList.Add(message);
                }

                // Find and delete map ban related messages
                var messagesToDelete = messageList.Where(m =>
                    (m.Author?.IsBot == true && m.Content?.Contains("map ban") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Map Ban") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("scroll to see all map options") == true) ||
                    (m.Author?.IsBot == true && m.Embeds?.Any(e => e.Title?.Contains("Map Ban") == true) == true)
                ).ToList();

                foreach (var message in messagesToDelete)
                {
                    try
                    {
                        await message.DeleteAsync();
                        // Add a small delay to avoid rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete map ban message");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up map ban messages");
            }
        }
    }
}