using DSharpPlus.Commands;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Wabbit.BotClient.Commands
{
    public partial class TournamentManagementGroup
    {
        [Command("signup_create")]
        [Description("Create a new tournament signup")]
        public async Task CreateSignup(
            CommandContext context,
            [Description("Tournament name")] string name,
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format,
            [Description("Game type (1v1 or 2v2)")][SlashChoiceProvider<GameTypeChoiceProvider>] string gameType = "OneVsOne",
            [Description("Scheduled start time (Unix timestamp, 0 for none)")] long startTimeUnix = 0)
        {
            await SafeExecute(context, async () =>
            {
                _logger.LogInformation($"Starting signup creation process for tournament '{name}'");

                // Check if signup already exists
                var existingSignup = _signupService.GetSignup(name);
                if (existingSignup != null)
                {
                    _logger.LogWarning($"Signup '{name}' already exists");
                    throw new InvalidOperationException($"A signup with the name '{name}' already exists.");
                }

                // Get signup channel ID
                var signupChannelId = GetSignupChannelId(context);
                if (!signupChannelId.HasValue)
                {
                    _logger.LogError("Failed to get signup channel ID");
                    throw new InvalidOperationException("Could not determine signup channel. Please ensure the command is used in the correct channel.");
                }
                _logger.LogInformation($"Using signup channel ID: {signupChannelId}");

                // Parse format
                if (!Enum.TryParse<TournamentFormat>(format, out var tournamentFormat))
                {
                    _logger.LogError($"Invalid tournament format: {format}");
                    throw new InvalidOperationException($"Invalid tournament format: {format}");
                }
                _logger.LogInformation($"Parsed tournament format: {tournamentFormat}");

                // Parse game type
                if (!Enum.TryParse<GameType>(gameType, out var parsedGameType))
                {
                    _logger.LogError($"Invalid game type: {gameType}");
                    throw new InvalidOperationException($"Invalid game type: {gameType}");
                }
                _logger.LogInformation($"Parsed game type: {parsedGameType}");

                // Convert Unix timestamp to DateTime if provided
                DateTime? scheduledStartTime = null;
                if (startTimeUnix > 0)
                {
                    try
                    {
                        scheduledStartTime = DateTimeOffset.FromUnixTimeSeconds(startTimeUnix).DateTime;
                        _logger.LogInformation($"Parsed scheduled start time: {scheduledStartTime}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to parse Unix timestamp: {startTimeUnix}");
                        throw new InvalidOperationException($"Invalid start time timestamp: {startTimeUnix}");
                    }
                }

                _logger.LogInformation($"Creating signup with parameters: Name='{name}', Format={tournamentFormat}, GameType={parsedGameType}, ScheduledStartTime={scheduledStartTime}");

                // Create the signup
                var signup = _signupService.CreateSignup(
                    name,
                    tournamentFormat,
                    context.User,
                    signupChannelId.Value,
                    parsedGameType,
                    scheduledStartTime
                );

                _logger.LogInformation($"Created signup object with name: {signup.Name}");

                // Get the signup channel
                var signupChannel = await context.Client.GetChannelAsync(signupChannelId.Value);
                if (signupChannel is null)
                {
                    _logger.LogError($"Failed to get channel with ID {signupChannelId.Value}");
                    throw new InvalidOperationException("Could not access signup channel.");
                }

                _logger.LogInformation("Creating signup embed and message builder");
                // Create and send the signup message
                try
                {
                    var embed = _signupService.CreateSignupEmbed(signup);
                    _logger.LogInformation("Created signup embed successfully");

                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(embed)
                        .AddComponents(
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Success,
                                $"signup_tournament_{signup.Name}",
                                "Sign Up"
                            ),
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Danger,
                                $"withdraw_{signup.Name}",
                                "Withdraw"
                            )
                        );
                    _logger.LogInformation("Created message builder with embed and buttons");

                    _logger.LogInformation($"Attempting to send message to channel {signupChannel.Name} ({signupChannel.Id})");
                    var message = await signupChannel.SendMessageAsync(builder);
                    _logger.LogInformation($"Successfully sent message with ID: {message.Id}");

                    // Store the message ID
                    signup.MessageId = message.Id;
                    _logger.LogInformation($"Set MessageId to {message.Id} for signup '{name}'");

                    // Save updated MessageId - this is critical for future updates
                    _logger.LogInformation("Updating signup with new message ID");
                    _signupService.UpdateSignup(signup);
                    _logger.LogInformation("Saving signups to persistent storage");
                    await _signupService.SaveSignupsAsync();

                    // Verify the MessageId was saved
                    var savedSignup = _signupService.GetSignup(name);
                    if (savedSignup == null || savedSignup.MessageId == 0)
                    {
                        _logger.LogWarning($"MessageId was not saved correctly for '{name}'. Current value: {savedSignup?.MessageId ?? 0}");
                    }
                    else
                    {
                        _logger.LogInformation($"Successfully saved MessageId {savedSignup.MessageId} for signup '{name}'");
                    }

                    // Send a simple confirmation without repeating the tournament details
                    await SafeResponse(context, $"Tournament signup '{name}' created successfully. Check {signupChannel.Mention} for the signup form.", null, true, 10);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create or send signup message");
                    throw;
                }
            });
        }

        [Command("signup_close")]
        [Description("Close signups for a tournament")]
        public async Task CloseSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the signup and ensure participants are loaded
                var client = context.Client;
                TournamentSignup? signup = null;

                try
                {
                    signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error loading signup: {ex.Message}");
                }

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _signupService.GetAllSignups();
                    var similarSignup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (similarSignup != null)
                    {
                        try
                        {
                            signup = await _signupService.GetSignupWithParticipantsAsync(similarSignup.Name, client);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error loading signup with similar name: {ex.Message}");
                        }
                    }

                    if (signup == null)
                    {
                        await SafeResponse(context, $"Signup '{tournamentName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}", null, true, 10);
                        return;
                    }
                }

                if (!signup.IsOpen)
                {
                    await SafeResponse(context, $"Signup '{signup.Name}' is already closed.", null, true, 10);
                    return;
                }

                // Close the signup
                signup.IsOpen = false;
                _signupService.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                await SafeResponse(context, $"Signup '{signup.Name}' has been closed. Use '/tournament_manager create_from_signup' to create the tournament.", null, true, 10);
            }, "Failed to close signup");
        }

        [Command("signup_reopen")]
        [Description("Reopen a closed tournament signup")]
        public async Task ReopenSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Duration in minutes to keep open (0 = indefinite)")] int durationMinutes = 0)
        {
            await SafeExecute(context, async () =>
            {
                // Find the signup and ensure participants are loaded
                var client = context.Client;
                TournamentSignup? signup = null;

                try
                {
                    signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, client);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error loading signup: {ex.Message}");
                }

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _signupService.GetAllSignups();
                    var similarSignup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (similarSignup != null)
                    {
                        try
                        {
                            signup = await _signupService.GetSignupWithParticipantsAsync(similarSignup.Name, client);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error loading signup with similar name: {ex.Message}");
                        }
                    }

                    if (signup == null)
                    {
                        await SafeResponse(context, $"Signup '{tournamentName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}", null, true);
                        return;
                    }
                }

                // Reopen the signup
                signup.IsOpen = true;
                _signupService.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                string durationMessage = durationMinutes > 0
                    ? $" It will remain open for {durationMinutes} minutes."
                    : " It will remain open indefinitely.";

                await SafeResponse(context, $"Tournament signup '{signup.Name}' has been reopened.{durationMessage}", null, true, 10);

                // Schedule auto-close if duration is specified
                if (durationMinutes > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromMinutes(durationMinutes));

                        // Make sure the signup is still open before closing it
                        if (signup.IsOpen)
                        {
                            signup.IsOpen = false;
                            _signupService.UpdateSignup(signup);
                            await UpdateSignupMessage(signup, context.Client);
                            await context.Channel.SendMessageAsync($"Tournament signup '{signup.Name}' has been automatically closed after {durationMinutes} minutes.");
                        }
                    });
                }
            }, "Failed to reopen signup");
        }

        [Command("signup_add")]
        [Description("Add a player to a tournament signup (admins only)")]
        public async Task AddToSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to add")] DiscordMember player)
        {
            await SafeExecute(context, async () =>
            {
                // Check permissions
                if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageMessages))
                {
                    await context.EditResponseAsync("You don't have permission to add players to signups.");
                    return;
                }

                // Log player information for debugging (minimal)
                _logger.LogInformation($"Adding player to signup '{tournamentName}': {player.Username} (ID: {player.Id})");

                // Find the signup using the SignupService and ensure participants are loaded
                var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, context.Client);

                if (signup == null)
                {
                    await context.EditResponseAsync($"Signup '{tournamentName}' not found.");
                    return;
                }

                if (!signup.IsOpen)
                {
                    await context.EditResponseAsync($"Signup '{tournamentName}' is closed and cannot be modified.");
                    return;
                }

                // Check if the player is already signed up
                if (signup.Participants.Any(p => p.Id == player.Id))
                {
                    await SafeResponse(context, $"{player.DisplayName} is already signed up for tournament '{tournamentName}'.", null, true, 10);
                    return;
                }

                // Add the player to the signup
                // Create a new list from the existing participants
                _logger.LogInformation($"Current participants in signup '{signup.Name}':");
                foreach (var p in signup.Participants)
                {
                    _logger.LogInformation($"  - {p.Username} (ID: {p.Id})");
                }
                var newParticipantsList = signup.Participants.ToList();

                // Log the initial state of the list
                _logger.LogInformation($"Initial participants list after initialization contains {newParticipantsList.Count} players:");
                foreach (var p in newParticipantsList)
                {
                    _logger.LogInformation($"  - {p.Username} (ID: {p.Id})");
                }

                // Add the new player
                newParticipantsList.Add(player);

                // Log the final state after adding new player
                _logger.LogInformation($"Final participants list contains {newParticipantsList.Count} players:");
                foreach (var p in newParticipantsList)
                {
                    _logger.LogInformation($"  - {p.Username} (ID: {p.Id})");
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Also update the ParticipantInfo list for persistence
                if (signup.ParticipantInfo == null)
                {
                    signup.ParticipantInfo = new List<ParticipantInfo>();
                }

                // Add to ParticipantInfo if not already there
                if (!signup.ParticipantInfo.Any(p => p.Id == player.Id))
                {
                    signup.ParticipantInfo.Add(new ParticipantInfo { Id = player.Id, Username = player.Username });
                    _logger.LogInformation($"Added {player.Username} (ID: {player.Id}) to ParticipantInfo list, now has {signup.ParticipantInfo.Count} entries");
                }

                _logger.LogInformation($"Successfully added {player.Username} (ID: {player.Id}) to signup '{tournamentName}'");
                _logger.LogInformation($"Signup now has {signup.Participants.Count} participants (ParticipantInfo: {signup.ParticipantInfo.Count})");

                // Save the updated signup
                _signupService.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                // Send confirmation message
                await SafeResponse(context, $"{player.DisplayName} has been added to the tournament '{tournamentName}'.", null, true, 10);
            }, "Failed to add player to signup");
        }

        [Command("signup_remove")]
        [Description("Remove a player from a tournament signup (admins only)")]
        public async Task RemoveFromSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to remove")] DiscordMember player)
        {
            await SafeExecute(context, async () =>
            {
                // Find the signup using the SignupService and ensure participants are loaded
                var signup = await _signupService.GetSignupWithParticipantsAsync(tournamentName, context.Client);

                if (signup == null)
                {
                    await SafeResponse(context, $"Signup '{tournamentName}' not found.", null, true, 10);
                    return;
                }

                // Check if the player is signed up
                var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == player.Id);
                if (existingParticipant is null)
                {
                    await SafeResponse(context, $"{player.DisplayName} is not signed up for tournament '{tournamentName}'.", null, true, 10);
                    return;
                }

                // Remove the player from the signup
                var newParticipantsList = new List<DiscordMember>();

                // Add all participants except the one to be removed
                foreach (var participant in signup.Participants)
                {
                    if (participant.Id != player.Id)
                    {
                        newParticipantsList.Add(participant);
                    }
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Also remove from ParticipantInfo list for persistence
                if (signup.ParticipantInfo != null)
                {
                    signup.ParticipantInfo.RemoveAll(p => p.Id == player.Id);
                    _logger.LogInformation($"Removed {player.Username} (ID: {player.Id}) from ParticipantInfo list, remaining: {signup.ParticipantInfo.Count}");
                }

                _logger.LogInformation($"Successfully removed {player.DisplayName} (ID: {player.Id}) from signup '{tournamentName}'");
                _logger.LogInformation($"Signup now has {signup.Participants.Count} participants (ParticipantInfo: {signup.ParticipantInfo?.Count ?? 0})");

                // Save the updated signup
                _signupService.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                // Send confirmation message
                await SafeResponse(context, $"{player.DisplayName} has been removed from the tournament '{tournamentName}'.", null, true, 10);
            }, "Failed to remove player from signup");
        }

        [Command("signup_set_seed")]
        [Description("Set a seed value for a player in a tournament signup (admins only)")]
        public async Task SetSeed(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to seed")] DiscordMember player,
            [Description("Seed value (1-999, 0 to remove seeding)")] int seed)
        {
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Validate the seed value
                if (seed < 0 || seed > 999)
                {
                    await SafeResponse(context, "Seed value must be between 0 and 999. Use 0 to remove seeding.", null, true);
                    return;
                }

                // Get the signup
                var signup = _signupService.GetSignup(tournamentName);
                if (signup == null)
                {
                    await SafeResponse(context, $"No signup found with name '{tournamentName}'", null, true);
                    return;
                }

                // Check if the player is in the tournament
                bool isInTournament = signup.Participants.Any(p => p.Id == player.Id);
                if (!isInTournament)
                {
                    await SafeResponse(context, $"{player.DisplayName} is not signed up for this tournament.", null, true);
                    return;
                }

                // Initialize Seeds list if needed
                if (signup.Seeds == null)
                {
                    signup.Seeds = [];
                }

                // Initialize SeedInfo list if needed
                if (signup.SeedInfo == null)
                {
                    signup.SeedInfo = [];
                }

                // Remove any existing seed for this player from both collections
                signup.Seeds.RemoveAll(s => s.Player?.Id == player.Id || s.PlayerId == player.Id);
                signup.SeedInfo.RemoveAll(s => s.Id == player.Id);

                // If seed is not 0, add the new seed to both collections
                if (seed > 0)
                {
                    // Add to Seeds collection
                    var participantSeed = new ParticipantSeed();
                    participantSeed.SetPlayer(player);
                    participantSeed.Seed = seed;
                    signup.Seeds.Add(participantSeed);

                    // Add to SeedInfo collection
                    signup.SeedInfo.Add(new SeedInfo { Id = player.Id, Seed = seed });
                }

                // Update the signup
                _signupService.UpdateSignup(signup);

                // Prepare the response message
                string responseMessage = seed > 0
                    ? $"✅ {player.DisplayName} has been assigned seed #{seed} in tournament '{tournamentName}'"
                    : $"✅ Seeding removed for {player.DisplayName} in tournament '{tournamentName}'";

                await SafeResponse(context, responseMessage, null, true);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);
            }, "Failed to set seed");
        }

        [Command("set_tournament_seed")]
        [Description("Set a seed value for a player in a tournament (admins only)")]
        public async Task SetTournamentSeed(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to seed")] DiscordMember player,
            [Description("Seed value (1-999, 0 to remove seeding)")] int seed)
        {
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Validate the seed value
                if (seed < 0 || seed > 999)
                {
                    await SafeResponse(context, "Seed value must be between 0 and 999. Use 0 to remove seeding.", null, true);
                    return;
                }

                // Get the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"No tournament found with name '{tournamentName}'", null, true);
                    return;
                }

                // Check if the player is in the tournament
                bool isInTournament = false;
                Tournament.GroupParticipant? participantToSeed = null;

                foreach (var group in tournament.Groups ?? Enumerable.Empty<Tournament.Group>())
                {
                    var matchingParticipant = group.Participants.FirstOrDefault(p =>
                        p.Player is DiscordMember member && member.Id == player.Id);

                    if (matchingParticipant != null)
                    {
                        isInTournament = true;
                        participantToSeed = matchingParticipant;
                        break;
                    }
                }

                if (!isInTournament || participantToSeed == null)
                {
                    await SafeResponse(context, $"{player.DisplayName} is not in this tournament.", null, true);
                    return;
                }

                // Set the seed value directly
                participantToSeed.Seed = seed;

                string responseMessage = seed > 0
                    ? $"✅ {player.DisplayName} has been assigned seed #{seed} in tournament '{tournamentName}'"
                    : $"✅ Seeding removed for {player.DisplayName} in tournament '{tournamentName}'";

                await SafeResponse(context, responseMessage, null, true);

                // Save the tournament state
                await _stateService.SaveTournamentStateAsync(context.Client);
            }, "Failed to set tournament seed");
        }

        private async Task UpdateSignupMessage(TournamentSignup signup, DiscordClient client)
        {
            try
            {
                if (signup.MessageId == 0 || signup.SignupChannelId == 0)
                {
                    _logger.LogWarning($"Cannot update signup message for '{signup.Name}' - missing message ID or channel ID");
                    return;
                }

                // Load participants if needed
                await _signupService.LoadParticipantsAsync(signup, (DSharpPlus.DiscordClient)client);

                // Get the channel
                var channel = await client.GetChannelAsync(signup.SignupChannelId);
                if (channel is null)
                {
                    _logger.LogWarning($"Cannot update signup message for '{signup.Name}' - channel {signup.SignupChannelId} not found");
                    return;
                }

                // Get the message
                var message = await channel.GetMessageAsync(signup.MessageId);
                if (message == null)
                {
                    _logger.LogWarning($"Cannot update signup message for '{signup.Name}' - message {signup.MessageId} not found in channel {signup.SignupChannelId}");
                    return;
                }

                // Use our existing CreateSignupEmbed method for consistency
                var embed = _signupService.CreateSignupEmbed(signup);

                // Create components based on signup status
                var builder = new DiscordMessageBuilder()
                    .AddEmbed(embed);

                if (signup.IsOpen)
                {
                    // Add signup/withdraw buttons
                    builder.AddComponents(
                        new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_tournament_{signup.Name}",
                            "Sign Up"
                        ),
                        new DiscordButtonComponent(
                            DiscordButtonStyle.Danger,
                            $"withdraw_{signup.Name}",
                            "Withdraw"
                        )
                    );
                }
                else
                {
                    // Add a disabled button for closed signups
                    builder.AddComponents(
                        new DiscordButtonComponent(
                            DiscordButtonStyle.Secondary,
                            $"closed_{signup.Name}",
                            "Signups Closed",
                            true // disabled
                        )
                    );
                }

                // Update the message
                await message.ModifyAsync(builder);

                _logger.LogInformation($"Updated signup message for '{signup.Name}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating signup message for '{signup.Name}': {ex.Message}");
            }
        }
    }
}
