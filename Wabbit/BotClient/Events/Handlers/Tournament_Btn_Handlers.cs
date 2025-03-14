using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Models;

namespace Wabbit.BotClient.Events.Handlers
{
    public class Tournament_Btn_Handlers(TournamentManager tournamentManager, ILogger<Event_Button> logger)
    {
        private readonly TournamentManager _tournamentManager = tournamentManager;
        private readonly ILogger<Event_Button> _logger = logger;

        public async Task HandleSignupButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                // Immediately acknowledge the interaction to prevent timeouts
                // Give Discord some time to prepare for the interaction
                await Task.Delay(500);

                try
                {
                    // Then try to create the response
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                    // Give Discord some more time after the response
                    await Task.Delay(1000);
                }
                catch (Exception deferEx)
                {
                    // Failed to defer signup button response, will try to continue
                    _logger.LogWarning($"Failed to defer signup button response: {deferEx.Message}");
                }

                // Extract the tournament name from the button ID (format: signup_TournamentName)
                string tournamentName = e.Id.Substring("signup_".Length);

                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

                if (signup == null)
                {
                    try
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                            .WithContent($"Signup '{tournamentName}' not found. It may have been removed.")
                            .AsEphemeral(true));
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send followup message: {followupEx.Message}");
                        // Try direct channel message as fallback
                        await e.Channel.SendMessageAsync($"Signup '{tournamentName}' not found. It may have been removed.");
                    }
                    return;
                }

                if (!signup.IsOpen)
                {
                    try
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                            .WithContent($"Signup for '{tournamentName}' is closed.")
                            .AsEphemeral(true));
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send followup message: {followupEx.Message}");
                        await e.Channel.SendMessageAsync($"Signup for '{tournamentName}' is closed.");
                    }
                    return;
                }

                // Check if the user is already signed up
                var member = (DiscordMember)e.User;
                var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == member.Id);

                if (existingParticipant is not null)
                {
                    // User is already signed up - handle cancellation
                    // Create a confirmation message with buttons
                    try
                    {
                        var confirmationMessage = new DiscordFollowupMessageBuilder()
                            .WithContent($"You are already signed up for tournament '{tournamentName}'. Would you like to cancel your signup?")
                            .AddComponents(
                                new DiscordButtonComponent(DiscordButtonStyle.Danger, $"cancel_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Cancel Signup"),
                                new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"keep_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Keep Signup")
                            )
                            .AsEphemeral(true);

                        await e.Interaction.CreateFollowupMessageAsync(confirmationMessage);
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send confirmation message: {followupEx.Message}");
                        await e.Channel.SendMessageAsync($"You are already signed up for tournament '{tournamentName}'.");
                    }
                    return;
                }

                // Add the user to the signup
                var newParticipantsList = new List<DiscordMember>(signup.Participants);

                // Add the new player
                newParticipantsList.Add(member);

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Log signup using logger instead of console
                _logger.LogInformation($"Added participant {member.Username} to signup '{signup.Name}' (now has {signup.Participants.Count} participants)");

                // Save the updated signup
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(sender, signup);

                try
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You have been added to the tournament '{tournamentName}'.")
                        .AsEphemeral(true));
                }
                catch (Exception followupEx)
                {
                    _logger.LogWarning($"Failed to send confirmation message: {followupEx.Message}");
                    await e.Channel.SendMessageAsync($"You have been added to the tournament '{tournamentName}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling signup button: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"An error occurred: {ex.Message}")
                        .AsEphemeral(true));
                }
                catch (Exception msgEx)
                {
                    _logger.LogWarning($"Failed to send error message: {msgEx.Message}");
                    try
                    {
                        // Direct message as a final fallback
                        await e.Channel.SendMessageAsync($"An error occurred processing your signup: {ex.Message}");
                    }
                    catch
                    {
                        _logger.LogError("Failed to send any error messages");
                    }
                }
            }
        }

        // Add a method to handle the cancel/keep signup buttons
        public async Task HandleCancelSignupButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                // Immediately acknowledge the interaction
                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                // Parse the button ID to get tournament name and user ID
                // Format: cancel_signup_TournamentName_UserId
                string[] parts = e.Id.Split('_');
                if (parts.Length < 4)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Invalid button ID format.")
                        .AsEphemeral(true));
                    return;
                }

                // Get tournament name (may have underscores in it)
                string tournamentName = string.Join(" ", parts.Skip(2).Take(parts.Length - 3));

                // Get user ID from the last part
                if (!ulong.TryParse(parts[parts.Length - 1], out ulong userId))
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Invalid user ID in button.")
                        .AsEphemeral(true));
                    return;
                }

                // Make sure the user who clicked is the same as the user in the button
                if (e.User.Id != userId)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("You can only cancel your own signup.")
                        .AsEphemeral(true));
                    return;
                }

                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

                if (signup == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"Signup '{tournamentName}' not found. It may have been removed.")
                        .AsEphemeral(true));
                    return;
                }

                // Remove the user from the signup
                var participant = signup.Participants.FirstOrDefault(p => p.Id == userId);
                if (participant is not null)
                {
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

                    // Save the updated signup
                    _tournamentManager.UpdateSignup(signup);

                    // Update the signup message
                    await UpdateSignupMessage(sender, signup);

                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You have been removed from the tournament '{tournamentName}'.")
                        .AsEphemeral(true));
                }
                else
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You were not found in the participants list for '{tournamentName}'.")
                        .AsEphemeral(true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling cancel signup button: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"An error occurred: {ex.Message}")
                        .AsEphemeral(true));
                }
                catch (Exception msgEx)
                {
                    _logger.LogError($"Failed to send error message: {msgEx.Message}");
                }
            }
        }

        public async Task UpdateSignupMessage(DiscordClient sender, TournamentSignup signup)
        {
            if (signup.SignupChannelId != 0 && signup.MessageId != 0)
            {
                try
                {
                    Console.WriteLine($"Updating signup message for '{signup.Name}' with {signup.Participants.Count} participants");

                    // Get the channel and message
                    var channel = await sender.GetChannelAsync(signup.SignupChannelId);
                    var message = await channel.GetMessageAsync(signup.MessageId);

                    // Update the embed with the new participant list
                    DiscordEmbed updatedEmbed = CreateSignupEmbed(signup);

                    // Create components based on signup status
                    var components = new List<DiscordComponent>();
                    if (signup.IsOpen)
                    {
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_{signup.Name}",
                            "Sign Up"
                        ));
                    }

                    // Update the message
                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(updatedEmbed)
                        .AddComponents(components);

                    await message.ModifyAsync(builder);
                    Console.WriteLine($"Successfully updated signup message for '{signup.Name}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating signup message: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                Console.WriteLine($"Cannot update signup message: Missing channel ID ({signup.SignupChannelId}) or message ID ({signup.MessageId})");
            }
        }

        public DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
        {
            var builder = new DiscordEmbedBuilder()
                .WithTitle($"🏆 Tournament Signup: {signup.Name}")
                .WithDescription("Sign up for this tournament by clicking the button below.")
                .WithColor(new DiscordColor(75, 181, 67))
                .AddField("Format", signup.Format.ToString(), true)
                .AddField("Status", signup.IsOpen ? "Open" : "Closed", true)
                .AddField("Created By", signup.CreatedBy?.Username ?? signup.CreatorUsername, true)
                .WithTimestamp(signup.CreatedAt);

            if (signup.ScheduledStartTime.HasValue)
            {
                // Convert DateTime to Unix timestamp
                long unixTimestamp = ((DateTimeOffset)signup.ScheduledStartTime.Value).ToUnixTimeSeconds();
                string formattedTime = $"<t:{unixTimestamp}:F>";
                builder.AddField("Scheduled Start", formattedTime, false);
            }

            // Log the number of participants for debugging
            Console.WriteLine($"Creating embed for signup '{signup.Name}' with {signup.Participants.Count} participants");

            if (signup.Participants != null && signup.Participants.Count > 0)
            {
                // Create a list of participant usernames
                var participantNames = new List<string>();
                foreach (var participant in signup.Participants)
                {
                    try
                    {
                        participantNames.Add($"{participantNames.Count + 1}. {participant.Username}");
                        Console.WriteLine($"  - Adding participant to embed: {participant.Username} (ID: {participant.Id})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error adding participant to embed: {ex.Message}");
                    }
                }

                // Join the names with newlines
                string participants = string.Join("\n", participantNames);

                // Add the field with all participants
                builder.AddField($"Participants ({signup.Participants.Count})", participants, false);
            }
            else
            {
                builder.AddField("Participants (0)", "No participants yet", false);
            }

            return builder.Build();
        }

        public async Task HandleWithdrawButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            // Extract tournament name from customId
            string tournamentName = e.Id.Substring("withdraw_".Length);

            // Find the tournament and ensure participants are loaded
            var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

            if (signup == null)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"Tournament signup '{tournamentName}' not found.")
                        .AsEphemeral(true)
                );
                return;
            }

            if (!signup.IsOpen)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"This tournament signup is closed and no longer accepting changes.")
                        .AsEphemeral(true)
                );
                return;
            }

            var user = e.User as DiscordMember;

            // Check if user is already signed up
            var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == user?.Id);
            if (existingParticipant is null)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"You're not signed up for the '{signup.Name}' tournament.")
                        .AsEphemeral(true)
                );
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

            // Save the updated signup
            _tournamentManager.UpdateSignup(signup);

            // Update the signup message
            await UpdateSignupMessage(sender, signup);

            // Send confirmation message
            await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                .WithContent($"You have been removed from the '{signup.Name}' tournament.")
                .AsEphemeral(true));

            // Log the withdrawal
            _logger.LogInformation($"User {e.Interaction.User.Username} withdrawn from tournament '{signup.Name}'");
        }
    }
}
