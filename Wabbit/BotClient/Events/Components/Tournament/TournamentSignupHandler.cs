using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles tournament signup-related component interactions
    /// </summary>
    public class TournamentSignupHandler : ComponentHandlerBase
    {
        private readonly ITournamentSignupService _signupService;
        private readonly ITournamentService _tournamentService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="signupService">Service for managing tournament signups</param>
        /// <param name="tournamentService">Service for accessing tournament data</param>
        public TournamentSignupHandler(
            ILogger<TournamentSignupHandler> logger,
            ITournamentStateService stateService,
            ITournamentSignupService signupService,
            ITournamentService tournamentService)
            : base(logger, stateService)
        {
            _signupService = signupService;
            _tournamentService = tournamentService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("signup_tournament_") ||
                   customId.StartsWith("cancel_signup_") ||
                   customId.StartsWith("keep_signup_") ||
                   customId.StartsWith("withdraw_");
        }

        /// <summary>
        /// Handles tournament signup-related component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                if (e.Id.StartsWith("signup_tournament_"))
                {
                    await HandleSignupButton(client, e, hasBeenDeferred);
                }
                else if (e.Id.StartsWith("cancel_signup_"))
                {
                    await HandleCancelSignupButton(client, e, hasBeenDeferred);
                }
                else if (e.Id.StartsWith("keep_signup_"))
                {
                    await HandleKeepSignupButton(client, e, hasBeenDeferred);
                }
                else if (e.Id.StartsWith("withdraw_"))
                {
                    await HandleWithdrawButton(client, e, hasBeenDeferred);
                }
                else
                {
                    await SendErrorResponseAsync(e, "Unknown signup component interaction", hasBeenDeferred);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentSignupHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred while processing tournament signup: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles signup button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleSignupButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // Only try to defer if not already deferred
            if (!hasBeenDeferred)
            {
                try
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                    hasBeenDeferred = true;
                }
                catch (Exception ex)
                {
                    // Failed to defer signup button response, will try to continue
                    _logger.LogWarning($"Failed to defer signup button response: {ex.Message}");
                }
            }

            // Extract the tournament name from the button ID (format: signup_tournament_TournamentName)
            string tournamentName = e.Id.Substring("signup_tournament_".Length);

            // Find the signup using the SignupService and ensure participants are loaded
            var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);

            if (signup is null)
            {
                await SendErrorResponseAsync(e, $"Signup '{tournamentName}' not found. It may have been removed.", hasBeenDeferred);
                return;
            }

            if (!signup.IsOpen)
            {
                await SendErrorResponseAsync(e, $"Signup for '{tournamentName}' is closed.", hasBeenDeferred);
                return;
            }

            // Get the user as a Discord member
            var member = e.User as DiscordMember;
            if (member is null)
            {
                await SendErrorResponseAsync(e, "Unable to retrieve your member information.", hasBeenDeferred);
                return;
            }

            // Check if the user is already signed up
            var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == member.Id);
            if (existingParticipant is not null)
            {
                // User is already signed up, ask if they want to withdraw
                var withdrawConfirmBuilder = new DiscordWebhookBuilder()
                    .WithContent($"You are already signed up for tournament '{tournamentName}'. Would you like to cancel your signup?")
                    .AddComponents(
                        new DiscordButtonComponent(DiscordButtonStyle.Danger, $"cancel_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Cancel Signup"),
                        new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"keep_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Keep Signup")
                    );

                if (hasBeenDeferred)
                {
                    await e.Interaction.EditOriginalResponseAsync(withdrawConfirmBuilder);
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder(withdrawConfirmBuilder).AsEphemeral());
                }
                return;
            }

            try
            {
                // Add the user to the signup
                var newParticipantsList = new List<DiscordMember>(signup.Participants);
                newParticipantsList.Add(member);

                // Update the signup with the new participant
                signup.Participants = newParticipantsList;
                _signupService.UpdateSignup(signup);

                // Update the signup message with the new participant list
                await UpdateSignupMessage(client, signup);

                // Make sure ParticipantInfo exists and is updated for persistence
                if (signup.ParticipantInfo is null)
                {
                    signup.ParticipantInfo = new List<ParticipantInfo>();
                }

                // Add the user to ParticipantInfo if not already there
                if (!signup.ParticipantInfo.Any(p => p.Id == member.Id))
                {
                    signup.ParticipantInfo.Add(new ParticipantInfo { Id = member.Id, Username = member.Username });
                    _logger.LogInformation($"Added {member.Username} (ID: {member.Id}) to ParticipantInfo list, now: {signup.ParticipantInfo.Count}");
                }

                // Log signup
                _logger.LogInformation($"Added participant {member.Username} to signup '{signup.Name}' (now has {signup.Participants.Count} participants, ParticipantInfo: {signup.ParticipantInfo.Count})");

                // Save the updated signup
                _signupService.UpdateSignup(signup);
                await _signupService.SaveSignupsAsync();

                // Send confirmation message to the user (ephemeral)
                if (hasBeenDeferred)
                {
                    // Create a new interaction response for the success message
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You have successfully signed up for the '{signup.Name}' tournament!")
                        .AsEphemeral());
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent($"You have successfully signed up for the '{signup.Name}' tournament!")
                            .AsEphemeral()
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling signup button: {ex.Message}\n{ex.StackTrace}");
                await SendErrorResponseAsync(e, $"An error occurred while signing you up: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles cancel signup button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleCancelSignupButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // Only try to defer if not already deferred
            if (!hasBeenDeferred)
            {
                try
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                    hasBeenDeferred = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to defer cancel signup button response: {ex.Message}");
                }
            }

            try
            {
                // Format: cancel_signup_TournamentName_UserId
                string[] parts = e.Id.Split('_');
                if (parts.Length < 4)
                {
                    await SendErrorResponseAsync(e, "Invalid button ID format.", hasBeenDeferred);
                    return;
                }

                // Parse tournament name and user ID
                string tournamentName = parts[2].Replace("_", " ");
                ulong userId = ulong.Parse(parts[3]);

                // Check if the user is trying to cancel someone else's signup
                if (e.User.Id != userId)
                {
                    await SendErrorResponseAsync(e, "You can only cancel your own signup.", hasBeenDeferred);
                    return;
                }

                // Find the signup using the SignupService and ensure participants are loaded
                var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);

                if (signup is null)
                {
                    await SendErrorResponseAsync(e, $"Signup '{tournamentName}' not found. It may have been removed.", hasBeenDeferred);
                    return;
                }

                // Remove the user from the signup
                var newParticipantsList = new List<DiscordMember>();

                // Add all participants except the one to be removed
                foreach (var p in signup.Participants)
                {
                    if (p.Id != userId)
                    {
                        newParticipantsList.Add(p);
                    }
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Also remove from ParticipantInfo list for persistence
                if (signup.ParticipantInfo is not null)
                {
                    signup.ParticipantInfo.RemoveAll(p => p.Id == userId);
                    _logger.LogInformation($"Removed user {userId} from ParticipantInfo list, remaining: {signup.ParticipantInfo.Count}");
                }

                // Save the updated signup
                _signupService.UpdateSignup(signup);
                await _signupService.SaveSignupsAsync();

                // Update the signup message
                await UpdateSignupMessage(client, signup);

                // Send confirmation message (ephemeral)
                var successMessage = $"You have been removed from the '{signup.Name}' tournament.";
                if (hasBeenDeferred)
                {
                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(successMessage));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent(successMessage).AsEphemeral()
                    );
                }

                // Delete the original confirmation dialog if it exists
                if (e.Message is not null)
                {
                    try
                    {
                        await e.Message.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete confirmation dialog: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling cancel signup button: {ex.Message}\n{ex.StackTrace}");
                await SendErrorResponseAsync(e, $"An error occurred while canceling your signup: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles keep signup button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleKeepSignupButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // Simple handler to just acknowledge that the user wants to keep their signup
            if (hasBeenDeferred)
            {
                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent("Your signup has been kept."));
            }
            else
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().WithContent("Your signup has been kept.").AsEphemeral()
                );
            }

            // Delete the original message after handling
            if (e.Message is not null)
            {
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete message after keep signup: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles withdraw button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleWithdrawButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // Only try to defer if not already deferred
            if (!hasBeenDeferred)
            {
                try
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                    hasBeenDeferred = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to defer withdraw button response: {ex.Message}");
                }
            }

            try
            {
                // Extract tournament name from customId
                string tournamentName = e.Id.Substring("withdraw_".Length);

                // Find the tournament and ensure participants are loaded
                var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);
                if (signup is null)
                {
                    await SendErrorResponseAsync(e, $"Tournament signup '{tournamentName}' not found.", hasBeenDeferred);
                    return;
                }

                if (!signup.IsOpen)
                {
                    await SendErrorResponseAsync(e, $"This tournament signup is closed and no longer accepting changes.", hasBeenDeferred);
                    return;
                }

                var user = e.User as DiscordMember;

                // Check if user is already signed up
                var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == user?.Id);
                if (existingParticipant is null)
                {
                    await SendErrorResponseAsync(e, $"You're not signed up for the '{signup.Name}' tournament.", hasBeenDeferred);
                    return;
                }

                // Remove the participant
                var newParticipantsList = new List<DiscordMember>();

                // Add all participants except the one to be removed
                foreach (var p in signup.Participants)
                {
                    if (p.Id != user?.Id)
                    {
                        newParticipantsList.Add(p);
                    }
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Also remove from ParticipantInfo list for persistence
                if (signup.ParticipantInfo is not null)
                {
                    signup.ParticipantInfo.RemoveAll(p => p.Id == user?.Id);
                    _logger.LogInformation($"Removed user {user?.Username} (ID: {user?.Id}) from ParticipantInfo list, remaining: {signup.ParticipantInfo.Count}");
                }

                // Save the updated signup
                _signupService.UpdateSignup(signup);
                await _signupService.SaveSignupsAsync();

                // Update the signup message
                await UpdateSignupMessage(client, signup);

                // Send confirmation message (ephemeral)
                await SendResponseAsync(e, $"You have been withdrawn from the '{signup.Name}' tournament.", hasBeenDeferred, DiscordColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleWithdrawButton");
                await SendErrorResponseAsync(e, $"An error occurred while withdrawing from the tournament: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Updates the signup message with current participants
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="signup">The tournament signup to update</param>
        private async Task UpdateSignupMessage(DiscordClient client, TournamentSignup signup)
        {
            if (signup.SignupChannelId != 0 && signup.MessageId != 0)
            {
                try
                {
                    // Make sure participants are loaded
                    await _signupService.LoadParticipantsAsync(signup, client);

                    _logger.LogInformation($"Updating signup message for '{signup.Name}' with {signup.Participants.Count} participants");

                    // Get the channel and message
                    var channel = await client.GetChannelAsync(signup.SignupChannelId);
                    var message = await channel.GetMessageAsync(signup.MessageId);

                    // Update the embed with the new participant list using the centralized method
                    DiscordEmbed updatedEmbed = _signupService.CreateSignupEmbed(signup);

                    // Create components based on signup status
                    var components = new List<DiscordComponent>();
                    if (signup.IsOpen)
                    {
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_tournament_{signup.Name}",
                            "Sign Up"
                        ));

                        // Add withdraw button so participants can withdraw
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Danger,
                            $"withdraw_{signup.Name}",
                            "Withdraw"
                        ));
                    }

                    // Update the message
                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(updatedEmbed)
                        .AddComponents(components);

                    await message.ModifyAsync(builder);
                    _logger.LogInformation($"Successfully updated signup message for '{signup.Name}'");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating signup message for '{signup.Name}'");
                }
            }
            else
            {
                _logger.LogWarning($"Cannot update signup message: Missing channel ID ({signup.SignupChannelId}) or message ID ({signup.MessageId})");
            }
        }
    }
}