using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Commands.ContextChecks;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.BotClient.Config;
using System.ComponentModel;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using System.Reflection;
using System.Dynamic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Wabbit.BotClient.Commands
{
    [Command("tournament_manager")]
    [Description("Commands for managing tournaments")]
    public class TournamentManagementGroup
    {
        private const int autoDeleteSeconds = 30;

        private readonly TournamentManager _tournamentManager;
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentManagementGroup> _logger;

        public TournamentManagementGroup(OngoingRounds ongoingRounds, TournamentManager tournamentManager, ILogger<TournamentManagementGroup> logger)
        {
            _ongoingRounds = ongoingRounds;
            _tournamentManager = tournamentManager;
            _logger = logger;
        }

        [Command("create")]
        [Description("Create a new tournament")]
        public async Task CreateTournament(
            CommandContext context,
            [Description("Tournament name")] string name,
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format)
        {
            // Defer the response immediately to prevent timeout
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Check if a tournament with this name already exists
                if (_ongoingRounds.Tournaments.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        await context.EditResponseAsync($"A tournament with the name '{name}' already exists.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error responding to command: {ex.Message}");
                        await context.Channel.SendMessageAsync($"A tournament with the name '{name}' already exists.");
                    }
                    return;
                }

                // Inform user about next steps
                try
                {
                    await context.EditResponseAsync(
                        $"Tournament '{name}' creation started. Please @mention all players that should participate, separated by spaces. " +
                        $"For example: @Player1 @Player2 @Player3...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error responding to command: {ex.Message}");
                    await context.Channel.SendMessageAsync(
                        $"Tournament '{name}' creation started. Please @mention all players that should participate, separated by spaces. " +
                        $"For example: @Player1 @Player2 @Player3...");
                }

                // Tournament creation will be handled by user mentioning players in follow-up message
                // which will be processed in an event handler
            }, "Failed to create tournament");
        }

        [Command("create_from_signup")]
        [Description("Create a tournament from an existing signup")]
        public async Task CreateTournamentFromSignup(
            CommandContext context,
            [Description("Signup name")] string signupName)
        {
            await SafeExecute(context, async () =>
            {
                // Get the signup
                var client = context.Client;
                TournamentSignup? signup = null;

                try
                {
                    // Use GetSignupWithParticipants instead of GetSignup to load participants
                    signup = await _tournamentManager.GetSignupWithParticipants(signupName, client);
                }
                catch (Exception ex)
                {
                    await context.EditResponseAsync($"Error loading signup: {ex.Message}");
                    return;
                }

                if (signup == null)
                {
                    // Try to find a signup with a similar name
                    var allSignups = _tournamentManager.GetAllSignups();
                    var similarSignup = allSignups.FirstOrDefault(s => s.Name.Equals(signupName, StringComparison.OrdinalIgnoreCase));

                    if (similarSignup != null)
                    {
                        try
                        {
                            signup = await _tournamentManager.GetSignupWithParticipants(similarSignup.Name, client);
                        }
                        catch (Exception ex)
                        {
                            await context.EditResponseAsync($"Error loading signup: {ex.Message}");
                            return;
                        }
                    }
                    else
                    {
                        try
                        {
                            await context.EditResponseAsync($"Signup '{signupName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error responding to command: {ex.Message}");
                            await context.Channel.SendMessageAsync($"Signup '{signupName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}");
                        }
                        return;
                    }
                }

                // Check if a tournament with this name already exists (with null check)
                if (signup?.Name != null && _tournamentManager.GetTournament(signup.Name) != null)
                {
                    await context.EditResponseAsync($"A tournament with the name '{signup.Name}' already exists. Please delete it first or use a different name for the signup.");
                    return;
                }

                // Get the players who have signed up
                var players = signup?.Participants ?? new List<DiscordMember>();

                // Ensure signup is not null before continuing with tournament creation
                if (signup == null)
                {
                    await context.EditResponseAsync("Error: Signup could not be loaded properly.");
                    return;
                }

                // Log number of participants for debugging
                _logger.LogInformation($"Creating tournament from signup '{signup.Name}' with {players.Count} players");

                // Check if we have enough players
                if (players.Count < 4)
                {
                    await context.EditResponseAsync($"Not enough players have signed up for '{signup.Name}'. Need at least 4 players, but only have {players.Count}.");
                    return;
                }

                // Create the tournament
                string creator = context.User.Username;
                var tournamentFormat = signup.Format;

                // Determine group count based on player count and format
                int numGroups = players.Count < 9 ? 2 : players.Count < 17 ? 4 : 8;

                try
                {
                    var tournament = _tournamentManager.CreateTournament(signup.Name, players, tournamentFormat, context.Channel);

                    // Close the signup now that tournament is created
                    if (signup.IsOpen)
                    {
                        signup.IsOpen = false;
                        _tournamentManager.UpdateSignup(signup);
                        await UpdateSignupMessage(signup, context.Client);
                    }

                    // Create a basic embed to show the tournament
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"Tournament Created: {tournament.Name}")
                        .WithDescription($"Format: {tournament.Format}")
                        .WithColor(DiscordColor.Green)
                        .AddField("Players", $"{players.Count} players")
                        .AddField("Groups", $"{tournament.Groups.Count} groups");

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    await context.Channel.SendMessageAsync($"Tournament '{tournament.Name}' has been created from signup '{signup.Name}' with {players.Count} players. Format: {tournament.Format}, Groups: {tournament.Groups.Count}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create tournament from signup: {ex.Message}");
                    await context.EditResponseAsync($"Failed to create tournament from signup: {ex.Message}");
                }
            }, "Failed to create tournament from signup");
        }

        [Command("show_standings")]
        [Description("Display tournament standings")]
        public async Task ShowStandings(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await context.DeferResponseAsync();

            // Find the tournament
            var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                t.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

            if (tournament == null)
            {
                await context.EditResponseAsync($"Tournament '{tournamentName}' not found.");
                return;
            }

            try
            {
                // Generate the standings image and post it to the standings channel if configured
                string imagePath = await TournamentVisualization.GenerateStandingsImage(tournament, context.Client);

                // Send the image with the tournament standings
                var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"📊 **{tournament.Name}** Standings")
                    .AddFile(Path.GetFileName(imagePath), fileStream);

                await context.EditResponseAsync(messageBuilder);
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Failed to generate tournament standings: {ex.Message}");
            }
        }

        [Command("list")]
        [Description("List all tournaments")]
        public async Task ListTournaments(CommandContext context)
        {
            // Defer the response immediately to prevent timeout
            await context.DeferResponseAsync();

            try
            {
                // Get all tournaments from both sources
                var activeTournaments = _tournamentManager.GetAllTournaments() ?? new List<Tournament>();
                var signups = _tournamentManager.GetAllSignups() ?? new List<TournamentSignup>();

                // Create an embed to display all tournaments and signups
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Tournament Manager")
                    .WithDescription("List of all tournaments and signups")
                    .WithColor(DiscordColor.Blurple)
                    .WithTimestamp(DateTime.Now);

                // Add active tournaments section
                if (activeTournaments.Any())
                {
                    string tournamentList = "";
                    foreach (var tournament in activeTournaments)
                    {
                        // Calculate progress
                        int totalMatches = tournament.Groups.Sum(g => g.Matches.Count) + tournament.PlayoffMatches.Count;
                        int completedMatches = tournament.Groups.Sum(g => g.Matches.Count(m => m.IsComplete)) +
                                              tournament.PlayoffMatches.Count(m => m.IsComplete);

                        // Determine status
                        string status = tournament.IsComplete ? "Complete" :
                                       tournament.CurrentStage == TournamentStage.Playoffs ? "In Progress - Playoffs" :
                                       "In Progress - Groups";

                        tournamentList += $"**{tournament.Name}**\n" +
                                         $"Status: {status}\n" +
                                         $"Format: {tournament.Format}\n" +
                                         $"Progress: {completedMatches}/{totalMatches} matches completed\n" +
                                         $"Groups: {tournament.Groups.Count}\n\n";
                    }

                    embed.AddField("Active Tournaments", tournamentList.Trim());
                }

                // Add signups section
                if (signups.Any())
                {
                    string signupList = "";
                    foreach (var signup in signups)
                    {
                        string status = signup.IsOpen ? "Open for Signups" : "Signups Closed";
                        string scheduledTime = "Not scheduled";

                        if (signup.ScheduledStartTime.HasValue)
                        {
                            // Ensure we're getting a proper UTC timestamp
                            DateTime utcTime = signup.ScheduledStartTime.Value.ToUniversalTime();
                            long unixTimestamp = ((DateTimeOffset)utcTime).ToUnixTimeSeconds();
                            scheduledTime = $"<t:{unixTimestamp}:F>";
                        }

                        // Use the helper method to get the effective participant count
                        int participantCount = _tournamentManager.GetParticipantCount(signup);

                        signupList += $"**{signup.Name}**\n" +
                                     $"Status: {status}\n" +
                                     $"Format: {signup.Format}\n" +
                                     $"Participants: {participantCount}\n" +
                                     $"Scheduled Start: {scheduledTime}\n\n";
                    }

                    embed.AddField("Tournament Signups", signupList.Trim());
                }

                // If no tournaments or signups, add a message
                if (!activeTournaments.Any() && !signups.Any())
                {
                    embed.AddField("No Tournaments", "There are no active tournaments or signups.");
                }

                // Send the embed with proper error handling
                try
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                catch (Exception responseEx)
                {
                    Console.WriteLine($"Error sending tournament list response: {responseEx.Message}");
                    // Fallback to channel message if interaction response fails
                    await context.Channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing tournaments: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await context.EditResponseAsync($"Error listing tournaments: {ex.Message}");
                }
                catch
                {
                    await context.Channel.SendMessageAsync($"Error listing tournaments: {ex.Message}");
                }
            }
        }

        [Command("signup_create")]
        [Description("Create a new tournament signup")]
        public async Task CreateSignup(
            CommandContext context,
            [Description("Tournament name")] string name,
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format,
            [Description("Scheduled start time (Unix timestamp, 0 for none)")] long startTimeUnix = 0)
        {
            await SafeExecute(context, async () =>
            {
                // Check if a signup with this name already exists
                if (_ongoingRounds.TournamentSignups.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    await context.EditResponseAsync($"A signup with the name '{name}' already exists.");
                    return;
                }

                // Get the signup channel ID
                ulong? signupChannelId = GetSignupChannelId(context);
                if (!signupChannelId.HasValue)
                {
                    await context.EditResponseAsync("No signup channel configured. Using current channel.");
                    signupChannelId = context.Channel.Id;
                }

                // Convert Unix timestamp to DateTime if provided
                DateTime? scheduledStartTime = null;
                if (startTimeUnix > 0)
                {
                    // Convert Unix timestamp to UTC DateTime
                    DateTimeOffset utcTime = DateTimeOffset.FromUnixTimeSeconds(startTimeUnix);
                    scheduledStartTime = utcTime.UtcDateTime;

                    Console.WriteLine($"Signup timestamp conversion: Unix {startTimeUnix} -> UTC {scheduledStartTime.Value.ToString("yyyy-MM-dd HH:mm:ss")}");
                }

                // Create the signup using TournamentManager
                var signup = _tournamentManager.CreateSignup(
                    name,
                    Enum.Parse<TournamentFormat>(format),
                    context.User,
                    signupChannelId.Value,
                    scheduledStartTime
                );

                try
                {
                    // Create and send the signup message
                    var signupChannel = await context.Client.GetChannelAsync(signupChannelId.Value);
                    DiscordEmbed embed = CreateSignupEmbed(signup);

                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(embed)
                        .AddComponents(
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Success,
                                $"signup_{name}",
                                "Sign Up"
                            ),
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Danger,
                                $"withdraw_{name}",
                                "Withdraw"
                            )
                        );

                    var message = await signupChannel.SendMessageAsync(builder);

                    // Store the message ID
                    signup.MessageId = message.Id;
                    Console.WriteLine($"Set MessageId to {message.Id} for signup '{name}'");

                    // Save updated MessageId - this is critical for future updates
                    _tournamentManager.UpdateSignup(signup);

                    // Verify the MessageId was saved - this can be logged but doesn't need participants
                    var savedSignup = _tournamentManager.GetSignup(name);
                    if (savedSignup == null || savedSignup.MessageId == 0)
                    {
                        _logger.LogWarning($"MessageId was not saved correctly for '{name}'. Current value: {savedSignup?.MessageId ?? 0}");
                    }
                    else
                    {
                        _logger.LogInformation($"Successfully saved MessageId {savedSignup.MessageId} for signup '{name}'");
                    }

                    // Send a simple confirmation without repeating the tournament details
                    await context.EditResponseAsync($"Tournament signup '{name}' created successfully. Check {signupChannel.Mention} for the signup form.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending signup message: {ex.Message}\n{ex.StackTrace}");
                    await context.EditResponseAsync($"Tournament signup '{name}' was created but there was an error creating the signup message: {ex.Message}");
                }
            }, "Failed to create tournament signup");
        }

        [Command("signup")]
        [Description("Sign up for a tournament")]
        public async Task Signup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, context.Client);

                if (signup == null)
                {
                    await context.EditResponseAsync($"Signup '{tournamentName}' not found.");
                    return;
                }

                if (!signup.IsOpen)
                {
                    await context.EditResponseAsync($"Signup '{tournamentName}' is closed.");
                    return;
                }

                // Check if the user is already signed up
                if (signup.Participants.Any(p => p.Id == context.User.Id))
                {
                    await context.EditResponseAsync($"You are already signed up for tournament '{tournamentName}'.");
                    return;
                }

                // Add the user to the signup
                var newParticipantsList = new List<DiscordMember>(signup.Participants);

                // Add the new player
                newParticipantsList.Add((DiscordMember)context.User);

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                Console.WriteLine($"User {context.User.Username} (ID: {context.User.Id}) has signed up for tournament '{tournamentName}'");
                Console.WriteLine($"Signup now has {signup.Participants.Count} participants");

                // Save the updated signup 
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                await context.EditResponseAsync($"You have been added to the tournament '{tournamentName}'.");
            }, "Failed to sign up for tournament");
        }

        [Command("signup_cancel")]
        [Description("Cancel your signup for a tournament")]
        public async Task CancelSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, context.Client);

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

                // Check if the user is signed up
                var participant = signup.Participants.FirstOrDefault(p => p.Id == context.User.Id);
                if (participant is null)
                {
                    await context.EditResponseAsync($"You are not signed up for tournament '{tournamentName}'.");
                    return;
                }

                // Remove the user from the signup
                var newParticipantsList = new List<DiscordMember>();

                // Add all participants except the one to be removed
                foreach (var p in signup.Participants)
                {
                    if (p.Id != context.User.Id)
                    {
                        newParticipantsList.Add(p);
                    }
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                Console.WriteLine($"User {context.User.Username} (ID: {context.User.Id}) has canceled signup for tournament '{tournamentName}'");
                Console.WriteLine($"Signup now has {signup.Participants.Count} participants");

                // Save the updated signup
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                await context.EditResponseAsync($"You have been removed from the tournament '{tournamentName}'.");
            }, "Failed to cancel signup");
        }

        [Command("signup_close")]
        [Description("Close signups for a tournament")]
        public async Task CloseSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // DeferResponseAsync is already called in SafeExecute

                // Find the signup and ensure participants are loaded
                var client = context.Client;
                TournamentSignup? signup = null;

                try
                {
                    signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, client);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error loading signup: {ex.Message}");
                }

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _tournamentManager.GetAllSignups();
                    var similarSignup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (similarSignup != null)
                    {
                        try
                        {
                            signup = await _tournamentManager.GetSignupWithParticipants(similarSignup.Name, client);
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
                _tournamentManager.UpdateSignup(signup);

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
                // DeferResponseAsync is already called in SafeExecute

                // Find the signup and ensure participants are loaded
                var client = context.Client;
                TournamentSignup? signup = null;

                try
                {
                    signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, client);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error loading signup: {ex.Message}");
                }

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _tournamentManager.GetAllSignups();
                    var similarSignup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (similarSignup != null)
                    {
                        try
                        {
                            signup = await _tournamentManager.GetSignupWithParticipants(similarSignup.Name, client);
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
                _tournamentManager.UpdateSignup(signup);

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
                            _tournamentManager.UpdateSignup(signup);
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
                Console.WriteLine($"Adding player to signup '{tournamentName}': {player.Username} (ID: {player.Id})");

                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, context.Client);

                if (signup == null)
                {
                    await context.EditResponseAsync($"Signup '{tournamentName}' not found.");
                    return;
                }

                // Remove all the debug logging that was added for troubleshooting

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
                // Create a new list initialized with the existing participants
                // Create a new list and explicitly copy over each participant
                // Create a new list from the existing participants
                Console.WriteLine($"Current participants in signup '{signup.Name}':");
                foreach (var p in signup.Participants)
                {
                    Console.WriteLine($"  - {p.Username} (ID: {p.Id})");
                }
                var newParticipantsList = signup.Participants.ToList();

                // Log the initial state of the list
                Console.WriteLine($"Initial participants list after initialization contains {newParticipantsList.Count} players:");
                foreach (var p in newParticipantsList)
                {
                    Console.WriteLine($"  - {p.Username} (ID: {p.Id})");
                }

                // Add the new player
                newParticipantsList.Add(player);

                // Log the final state after adding new player
                Console.WriteLine($"Final participants list contains {newParticipantsList.Count} players:");
                foreach (var p in newParticipantsList)
                {
                    Console.WriteLine($"  - {p.Username} (ID: {p.Id})");
                }

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                Console.WriteLine($"Successfully added {player.Username} (ID: {player.Id}) to signup '{tournamentName}'");
                Console.WriteLine($"Signup now has {signup.Participants.Count} participants");

                // Save the updated signup
                _tournamentManager.UpdateSignup(signup);

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
                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, context.Client);

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

                Console.WriteLine($"Successfully removed {player.Username} (ID: {player.Id}) from signup '{tournamentName}'");
                Console.WriteLine($"Signup now has {signup.Participants.Count} participants");

                // Save the updated signup
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                // Send confirmation message
                await SafeResponse(context, $"{player.DisplayName} has been removed from the tournament '{tournamentName}'.", null, true, 10);
            }, "Failed to remove player from signup");
        }

        [Command("delete")]
        [Description("Delete a tournament or signup")]
        public async Task DeleteTournament(
            CommandContext context,
            [Description("Tournament/signup name")] string name)
        {
            await SafeExecute(context, async () =>
            {
                // DeferResponseAsync is already called in SafeExecute
                bool found = false;

                // First try to delete tournament
                var tournament = _tournamentManager.GetTournament(name);
                if (tournament != null)
                {
                    // Delete the tournament and its related messages
                    await _tournamentManager.DeleteTournament(name, context.Client);
                    await context.EditResponseAsync($"Tournament '{name}' has been deleted.");
                    found = true;
                }
                else
                {
                    // If tournament not found, try to find a signup
                    var signup = _tournamentManager.GetSignup(name);
                    if (signup != null)
                    {
                        // Delete the signup and its related messages
                        await _tournamentManager.DeleteSignup(name, context.Client);
                        await context.EditResponseAsync($"Tournament signup '{name}' has been deleted.");
                        found = true;
                    }
                }

                if (!found)
                {
                    // Debug message showing available tournaments/signups
                    var allTournaments = _tournamentManager.GetAllTournaments();
                    var allSignups = _tournamentManager.GetAllSignups();

                    string availableTournaments = allTournaments.Any()
                        ? string.Join(", ", allTournaments.Select(t => t.Name))
                        : "none";

                    string availableSignups = allSignups.Any()
                        ? string.Join(", ", allSignups.Select(s => s.Name))
                        : "none";

                    await context.EditResponseAsync(
                        $"Could not find tournament or signup with name '{name}'.\n" +
                        $"Available tournaments: {availableTournaments}\n" +
                        $"Available signups: {availableSignups}\n\n" +
                        "Please use exact names for deletion."
                    );
                }
            }, "Failed to delete tournament/signup");
        }

        [Command("set_standings_channel")]
        [Description("Set the channel where tournament standings will be displayed")]
        public async Task SetStandingsChannel(
            CommandContext context,
            [Description("Channel to use for standings")] DiscordChannel channel)
        {
            await SafeExecute(context, async () =>
            {
                if (context.Guild is null)
                {
                    await SafeResponse(context, "This command must be used in a server.", null, true, autoDeleteSeconds);
                    return;
                }

                // Get the server config
                var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == context.Guild.Id);
                if (server == null)
                {
                    await SafeResponse(context, "Server configuration not found.", null, true, autoDeleteSeconds);
                    return;
                }

                // Update the standings channel
                server.StandingsChannelId = channel.Id;
                await ConfigManager.SaveConfig();

                await SafeResponse(context,
                    $"Tournament standings channel has been set to {channel.Mention}. Standings visualizations will be posted here.",
                    null, true, autoDeleteSeconds);

                // Post a confirmation in the standings channel
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("📊 Tournament Standings Channel")
                    .WithDescription("This channel has been designated for tournament standings visualizations.")
                    .WithColor(DiscordColor.Green)
                    .WithFooter("Standings will be posted and updated here automatically");

                await channel.SendMessageAsync(embed);
            }, "Failed to set standings channel");
        }

        [Command("resume")]
        [Description("Resume a tournament after bot restart")]
        public async Task ResumeTournament(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find tournament
                var tournament = _tournamentManager.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await context.EditResponseAsync($"Tournament '{tournamentName}' not found.");
                    return;
                }

                // Find active rounds for this tournament
                var activeRounds = _tournamentManager.GetActiveRoundsForTournament(tournament.Name);

                // Display status
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Tournament: {tournament.Name}")
                    .WithDescription("Tournament has been resumed.")
                    .AddField("Status", tournament.IsComplete ? "Complete" : $"In Progress - {tournament.CurrentStage}")
                    .AddField("Format", tournament.Format.ToString())
                    .AddField("Groups", tournament.Groups.Count.ToString())
                    .AddField("Active Rounds", activeRounds.Count.ToString());

                if (activeRounds.Any())
                {
                    string roundsInfo = "";
                    foreach (var round in activeRounds)
                    {
                        roundsInfo += $"• Round {round.Id}: {round.Status}, Cycle {round.Cycle + 1}/{round.Length}\n";
                    }
                    embed.AddField("Round Details", roundsInfo);
                }

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }, "Failed to resume tournament");
        }

        private ulong? GetSignupChannelId(CommandContext context)
        {
            if (ConfigManager.Config?.Servers == null) return null;

            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild?.Id);
            return server?.SignupChannelId;
        }

        private DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
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
                // Convert DateTime to PST correctly
                // First convert to UTC to ensure we're working from a common reference point
                DateTime utcTime = signup.ScheduledStartTime.Value.ToUniversalTime();

                // Pacific Standard Time is UTC-8 (or UTC-7 during daylight saving)
                TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, pstZone);

                // Format with proper AM/PM indicator
                string pstFormatted = pstTime.ToString("MMM d, yyyy h:mm tt") + " PST";

                // Create Discord timestamp using the original UTC time to ensure accuracy
                long unixTimestamp = ((DateTimeOffset)utcTime).ToUnixTimeSeconds();
                string discordTimestampFull = $"<t:{unixTimestamp}:F>";
                string discordTimestampFriendly = $"<t:{unixTimestamp}:f>";

                builder.AddField("Scheduled Start (PST)", pstFormatted, false);
                builder.AddField("Scheduled Start (Local Time)", discordTimestampFriendly, false);
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

        private async Task UpdateSignupMessage(TournamentSignup signup, DiscordClient client)
        {
            try
            {
                if (signup.MessageId == 0 || signup.SignupChannelId == 0)
                {
                    Console.WriteLine($"Cannot update signup message for '{signup.Name}' - missing message ID or channel ID");
                    return;
                }

                // Load participants if needed
                await _tournamentManager.LoadParticipantsAsync(signup, (DSharpPlus.DiscordClient)client);

                // Get the channel
                var channel = await client.GetChannelAsync(signup.SignupChannelId);
                if (channel is null)
                {
                    Console.WriteLine($"Cannot update signup message for '{signup.Name}' - channel {signup.SignupChannelId} not found");
                    return;
                }

                // Get the message
                var message = await channel.GetMessageAsync(signup.MessageId);
                if (message == null)
                {
                    Console.WriteLine($"Cannot update signup message for '{signup.Name}' - message {signup.MessageId} not found in channel {signup.SignupChannelId}");
                    return;
                }

                // Use our existing CreateSignupEmbed method for consistency
                var embed = CreateSignupEmbed(signup);

                // Create components based on signup status
                var components = new List<DiscordComponent>();
                if (signup.IsOpen)
                {
                    // Add signup/withdraw buttons
                    components.Add(new DiscordButtonComponent(
                        DiscordButtonStyle.Success,
                        $"signup_{signup.Name.Replace(" ", "_")}",
                        "Sign Up"
                    ));
                    components.Add(new DiscordButtonComponent(
                        DiscordButtonStyle.Danger,
                        $"withdraw_{signup.Name.Replace(" ", "_")}",
                        "Withdraw"
                    ));
                }

                // Update the message
                await message.ModifyAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(components)
                );

                Console.WriteLine($"Updated signup message for '{signup.Name}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating signup message for '{signup.Name}': {ex.Message}");
            }
        }

        private async Task SafeResponse(CommandContext context, string message, Action? action = null, bool ephemeral = false, int autoDeleteSeconds = 0)
        {
            try
            {
                // For ephemeral messages, we need to use a different approach
                if (ephemeral)
                {
                    // First respond with a regular message
                    var response = await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
                    action?.Invoke();

                    // Then delete it after a short delay
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds > 0 ? autoDeleteSeconds : 10));
                        try
                        {
                            await response.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to auto-delete message: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Regular response
                    var response = await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
                    action?.Invoke();

                    // Auto-delete if requested
                    if (autoDeleteSeconds > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds));
                            try
                            {
                                await response.DeleteAsync();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to auto-delete message: {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SafeResponse: {ex.Message}");
                try
                {
                    var msg = await context.Channel.SendMessageAsync(message);
                    action?.Invoke();

                    // Auto-delete if requested
                    if ((ephemeral || autoDeleteSeconds > 0) && msg is not null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds > 0 ? autoDeleteSeconds : 10));
                            try
                            {
                                await msg.DeleteAsync();
                            }
                            catch (Exception delEx)
                            {
                                Console.WriteLine($"Failed to auto-delete fallback message: {delEx.Message}");
                            }
                        });
                    }
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"Failed to send fallback message: {innerEx.Message}");
                }
            }
        }

        private async Task SafeExecute(CommandContext context, Func<Task> action, string errorPrefix = "Command failed")
        {
            try
            {
                // Give the API a much longer delay to be ready - this helps with "Unknown interaction" errors
                await Task.Delay(500);  // Increased from 200ms

                // Try to defer the interaction, but ignore if it fails
                try
                {
                    // Call DeferResponseAsync early to avoid timeouts
                    await context.DeferResponseAsync();
                }
                catch (Exception deferEx)
                {
                    // If deferring fails, log it but continue - the interaction might already be deferred
                    Console.WriteLine($"Failed to defer response: {deferEx.Message}. Continuing execution...");
                }

                // Execute the action
                await action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{errorPrefix}: {ex.Message}\n{ex.StackTrace}");

                // Try to respond with the error message
                try
                {
                    await context.EditResponseAsync($"{errorPrefix}: {ex.Message}");
                }
                catch (Exception responseEx)
                {
                    Console.WriteLine($"Failed to send error response via interaction: {responseEx.Message}");

                    // Fallback to channel message if interaction response fails
                    try
                    {
                        await context.Channel.SendMessageAsync($"{errorPrefix}: {ex.Message}");
                    }
                    catch (Exception channelEx)
                    {
                        Console.WriteLine($"Failed to send error message to channel: {channelEx.Message}");
                    }
                }
            }
        }
    }

    public class TournamentFormatChoiceProvider : IChoiceProvider
    {
        private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> formats = new DiscordApplicationCommandOptionChoice[]
        {
            new("Group Stage + Playoffs", "GroupStageWithPlayoffs"),
            new("Single Elimination", "SingleElimination"),
            new("Double Elimination", "DoubleElimination"),
            new("Round Robin", "RoundRobin"),
        };

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(formats);
        }
    }
}