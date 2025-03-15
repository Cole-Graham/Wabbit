using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles tournament join-related component interactions
    /// </summary>
    public class TournamentJoinHandler : ComponentHandlerBase
    {
        private readonly ITournamentService _tournamentService;
        private readonly ITournamentSignupService _signupService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="tournamentService">Service for accessing tournament data</param>
        /// <param name="signupService">Service for managing tournament signups</param>
        public TournamentJoinHandler(
            ILogger<TournamentJoinHandler> logger,
            ITournamentStateService stateService,
            ITournamentService tournamentService,
            ITournamentSignupService signupService)
            : base(logger, stateService)
        {
            _tournamentService = tournamentService;
            _signupService = signupService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("join_tournament_");
        }

        /// <summary>
        /// Handles tournament join-related component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling tournament join component: {ComponentId}", e.Id);

                if (e.Id.StartsWith("join_tournament_"))
                {
                    await HandleJoinTournamentButton(client, e, hasBeenDeferred);
                }
                else
                {
                    await SendErrorResponseAsync(e, "Unknown tournament join component", hasBeenDeferred);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentJoinHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred while handling tournament join: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles join tournament button interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleJoinTournamentButton(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
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
                        _logger.LogWarning($"Failed to defer join tournament button response: {ex.Message}");
                    }
                }

                // Extract tournament name from button ID
                string tournamentName = e.Id.Replace("join_tournament_", "");

                // Get the signup from the SignupService
                var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);
                if (signup is null)
                {
                    await SendErrorResponseAsync(e, "Tournament signup not found.", hasBeenDeferred);
                    return;
                }

                // Check if signup is closed
                if (!signup.IsOpen)
                {
                    await SendErrorResponseAsync(e, "This tournament signup is closed and no longer accepting new participants.", hasBeenDeferred);
                    return;
                }

                // Check if the player is already in the tournament
                if (signup.Participants.Any(p => p.Id == e.User.Id))
                {
                    await SendErrorResponseAsync(e, "You're already participating in this tournament.", hasBeenDeferred);
                    return;
                }

                // Cast the user to DiscordMember with null check
                var member = e.User as DiscordMember;
                if (member is null)
                {
                    await SendErrorResponseAsync(e, "Unable to add you to the tournament: You must be a server member.", hasBeenDeferred);
                    return;
                }

                // Add the member to the tournament
                signup.Participants.Add(member);

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

                // Update the signup using the SignupService
                _signupService.UpdateSignup(signup);
                await _signupService.SaveSignupsAsync();

                // Update the signup message
                await UpdateSignupMessage(client, signup);

                // Notify the user with a success message (ephemeral)
                await SendResponseAsync(e, $"You've successfully joined the tournament '{tournamentName}'!", hasBeenDeferred, DiscordColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling join tournament button");
                await SendErrorResponseAsync(e, $"Error joining tournament: {ex.Message}", hasBeenDeferred);
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

                    // Create a standardized signup embed
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

                    // Add participants list
                    if (signup.Participants.Count > 0)
                    {
                        // Format participants list with numbering
                        var participantsFormatted = new System.Text.StringBuilder();
                        for (int i = 0; i < signup.Participants.Count; i++)
                        {
                            participantsFormatted.AppendLine($"{i + 1}. @{signup.Participants[i].Username}");
                        }

                        builder.AddField($"Participants ({signup.Participants.Count})", participantsFormatted.ToString(), false);
                    }
                    else
                    {
                        builder.AddField("Participants (0)", "No participants yet", false);
                    }

                    // Add tournament description and footer
                    builder.WithDescription("Sign up for this tournament by clicking the button below.")
                           .WithTimestamp(signup.CreatedAt)
                           .WithFooter($"Created by @{signup.CreatedBy?.Username ?? signup.CreatorUsername}");

                    // Create components based on signup status
                    var components = new List<DiscordComponent>();
                    if (signup.IsOpen)
                    {
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"join_tournament_{signup.Name}",
                            "Join Tournament"
                        ));
                    }

                    // Update the message
                    var messageBuilder = new DiscordMessageBuilder()
                        .AddEmbed(builder.Build());

                    if (components.Count > 0)
                    {
                        messageBuilder.AddComponents(components);
                    }

                    await message.ModifyAsync(messageBuilder);
                    _logger.LogInformation($"Successfully updated signup message for '{signup.Name}'");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating signup message for '{signup.Name}'");
                }
            }
        }
    }
}