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

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

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

                // Update the signup message
                await UpdateSignupMessage(client, signup);

                // Send confirmation message to the user (ephemeral)
                var successMessage = $"You have successfully signed up for the '{signup.Name}' tournament!";
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

                // Schedule auto-deletion of the confirmation message after 10 seconds
                if (e.Message is not null)
                {
                    await AutoDeleteMessageAsync(e.Message, 10);
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

                // Delete the original message after handling
                if (e.Message is not null)
                {
                    try
                    {
                        await e.Message.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to delete message after cancel signup: {ex.Message}");
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

                    // Update the embed with the new participant list
                    DiscordEmbed updatedEmbed = CreateStandardizedSignupEmbed(signup);

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

        /// <summary>
        /// Creates a standardized signup embed with the required format
        /// </summary>
        /// <param name="signup">The tournament signup</param>
        /// <returns>A Discord embed for the signup</returns>
        private DiscordEmbed CreateStandardizedSignupEmbed(TournamentSignup signup)
        {
            var builder = new DiscordEmbedBuilder()
                .WithTitle($"🏆 Tournament Signup: {signup.Name}")
                .WithColor(new DiscordColor(75, 181, 67));

            // Add Format field
            builder.AddField("Format", signup.Format.ToString(), true);

            // Add Game Type field - show 1v1 or 2v2
            string gameType = signup.Format.ToString().Contains("OneVsOne") ? "OneVsOne" : "TwoVsTwo";
            builder.AddField("Game Type", gameType, true);

            // Add Scheduled Start Time field if available
            if (signup.ScheduledStartTime.HasValue)
            {
                string formattedTime = $"<t:{((DateTimeOffset)signup.ScheduledStartTime).ToUnixTimeSeconds()}:F>";
                builder.AddField("Scheduled Start Time", formattedTime, false);
            }

            // Process the participants list in the standardized format
            List<object> sortedParticipants;
            int participantsCount = signup.Participants?.Count ?? 0;
            int participantInfoCount = signup.ParticipantInfo?.Count ?? 0;

            // Use the list with more participants (usually Participants, but fall back to ParticipantInfo)
            if (participantsCount >= participantInfoCount && participantsCount > 0)
            {
                // Use Discord Members
                sortedParticipants = (signup.Participants ?? new List<DiscordMember>())
                    .Select(p => new
                    {
                        Username = $"@{p.Username}",
                        Seed = FindSeedValue(signup, p.Id)
                    })
                    .OrderBy(p => p.Seed == 0) // Seeded players first
                    .ThenBy(p => p.Seed) // Then by seed value
                    .ThenBy(p => p.Username) // Then alphabetically
                    .Cast<object>()
                    .ToList();
            }
            else if (participantInfoCount > 0)
            {
                // Use ParticipantInfo
                sortedParticipants = (signup.ParticipantInfo ?? new List<ParticipantInfo>())
                    .Select(p => new
                    {
                        Username = $"@{p.Username}",
                        Seed = FindSeedValue(signup, p.Id)
                    })
                    .OrderBy(p => p.Seed == 0)
                    .ThenBy(p => p.Seed)
                    .ThenBy(p => p.Username)
                    .Cast<object>()
                    .ToList();
            }
            else
            {
                // No participants
                builder.AddField("Participants (0)", "No participants yet", false);
                builder.WithDescription("Sign up for this tournament by clicking the button below.")
                       .WithTimestamp(signup.CreatedAt)
                       .WithFooter($"Created by @{signup.CreatedBy?.Username ?? signup.CreatorUsername}");
                return builder.Build();
            }

            // Format participants into columns as per the user's request
            StringBuilder participantsText = new StringBuilder();
            int count = sortedParticipants.Count;

            // Add two participants per row
            for (int i = 0; i < count; i += 2)
            {
                // Left column - always present
                participantsText.Append($"{i + 1}. {GetUsername(sortedParticipants[i])}");

                // Right column - may not be present for odd number of participants
                if (i + 1 < count)
                {
                    participantsText.Append($"     {i + 2}. {GetUsername(sortedParticipants[i + 1])}");
                }

                if (i + 2 < count) // Add newline if not the last row
                {
                    participantsText.AppendLine();
                }
            }

            // Add the participants field
            builder.AddField($"Participants ({count})", participantsText.ToString(), false);

            // Add creator and timestamp
            builder.WithDescription("Sign up for this tournament by clicking the button below•")
                   .WithFooter($"Created by @{signup.CreatedBy?.Username ?? signup.CreatorUsername}");

            if (signup.CreatedAt != default)
            {
                builder.WithTimestamp(signup.CreatedAt);
            }

            return builder.Build();
        }

        /// <summary>
        /// Gets a username from a participant object
        /// </summary>
        /// <param name="participant">The participant object</param>
        /// <returns>The username as a string</returns>
        private string GetUsername(object participant)
        {
            // Handle null participant
            if (participant is null)
            {
                return "Unknown";
            }

            // Handle different types of participant objects
            if (participant is IDictionary<string, object> dict)
            {
                // Try to access the Username property through dictionary
                if (dict.TryGetValue("Username", out var username))
                    return username?.ToString() ?? "Unknown";
            }
            else if (participant?.GetType()?.GetProperty("Username") is System.Reflection.PropertyInfo usernameProperty)
            {
                return usernameProperty.GetValue(participant)?.ToString() ?? "Unknown";
            }

            // Fallback for other object types
            try
            {
                // Double-check for null again to be extra defensive
                if (participant is null)
                {
                    return "Unknown";
                }

                // Explicitly handle the fact that ToString() could return null in some implementations
                string? result = participant.ToString();
                return string.IsNullOrEmpty(result) ? "Unknown" : result;
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Finds the seed value for a player
        /// </summary>
        /// <param name="signup">The tournament signup</param>
        /// <param name="playerId">The player's ID</param>
        /// <returns>The seed value (0 if not seeded)</returns>
        private int FindSeedValue(TournamentSignup signup, ulong playerId)
        {
            // First check in Seeds collection (with Player references)
            var seedFromSeeds = signup.Seeds?.FirstOrDefault(s => s.Player?.Id == playerId || s.PlayerId == playerId)?.Seed ?? 0;
            if (seedFromSeeds > 0)
                return seedFromSeeds;

            // Then check in SeedInfo collection (with just Ids)
            return signup.SeedInfo?.FirstOrDefault(s => s.Id == playerId)?.Seed ?? 0;
        }
    }
}