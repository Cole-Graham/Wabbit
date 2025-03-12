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
using Wabbit.Data;
using System.ComponentModel;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Net;
using System.Reflection;
using System.Dynamic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MatchType = Wabbit.Models.MatchType;

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
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format,
            [Description("Game type (1v1 or 2v2)")][SlashChoiceProvider<GameTypeChoiceProvider>] string gameType = "OneVsOne",
            [Description("Use seeding for players?")] bool useSeeding = false)
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

                // Parse game type
                GameType parsedGameType;
                try
                {
                    parsedGameType = Enum.Parse<GameType>(gameType);
                }
                catch (Exception)
                {
                    await context.EditResponseAsync($"Invalid game type: {gameType}. Valid options are 'OneVsOne' or 'TwoVsTwo'.");
                    return;
                }

                // Inform user about next steps
                string seedingMessage = useSeeding ?
                    " **Seeding is enabled**. When mentioning players, you can optionally add a number after each mention to set seed values (e.g., @Player1 1 @Player2 2)." :
                    "";

                try
                {
                    await context.EditResponseAsync(
                        $"Tournament '{name}' creation started with format '{format}' and game type '{(parsedGameType == GameType.OneVsOne ? "1v1" : "2v2")}'.{seedingMessage} " +
                        $"Please @mention all players that should participate, separated by spaces. " +
                        $"For example: @Player1 @Player2 @Player3...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error responding to command: {ex.Message}");
                    await context.Channel.SendMessageAsync(
                        $"Tournament '{name}' creation started with format '{format}' and game type '{(parsedGameType == GameType.OneVsOne ? "1v1" : "2v2")}'.{seedingMessage} " +
                        $"Please @mention all players that should participate, separated by spaces. " +
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
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Load the signup with participants
                var signup = await _tournamentManager.GetSignupWithParticipants(signupName, context.Client);
                if (signup == null)
                {
                    await SafeResponse(context, $"No signup found with name '{signupName}'", null, true);
                    return;
                }

                if (signup.Participants.Count < 3)
                {
                    await SafeResponse(context, $"Signup '{signupName}' doesn't have enough participants (minimum 3 required)", null, true);
                    return;
                }

                // Check if signup has seeded players
                bool hasSeededPlayers = signup.Seeds != null && signup.Seeds.Count > 0;

                string createMethod = hasSeededPlayers ? "seeded" : "random";
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Creating tournament with {(hasSeededPlayers ? "**seeded groups**" : "**random groups**")} (based on available seeding data)"));

                // Create the tournament
                Tournament tournament;
                if (hasSeededPlayers)
                {
                    // Create tournament with seeded groups
                    tournament = await CreateTournamentWithSeeding(signup, context.Channel);
                }
                else
                {
                    // Create tournament with default settings
                    tournament = await _tournamentManager.CreateTournament(
                        signup.Name,
                        signup.Participants,
                        signup.Format,
                        context.Channel,
                        signup.Type);
                }

                // Prepare a confirmation message with details
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 Tournament Created: {tournament.Name}")
                    .WithDescription($"Tournament has been created from signup '{signupName}'.")
                    .WithColor(DiscordColor.Green)
                    .AddField("Format", tournament.Format.ToString(), true)
                    .AddField("Players", tournament.Groups.Sum(g => g.Participants.Count).ToString(), true)
                    .AddField("Groups", tournament.Groups.Count.ToString(), true)
                    .WithFooter("Tournament will start automatically in 5 seconds");

                // Send confirmation message
                var confirmation = await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                // Send a public message
                var publicMessage = await context.Channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"**Tournament Created**: {tournament.Name} has been created and will start shortly!")
                        .WithAllowedMentions(new IMention[] { new RoleMention() }));

                // Auto-delete both messages after a short time
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try
                    {
                        await confirmation.DeleteAsync();
                        await publicMessage.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete messages: {ex.Message}");
                    }
                });

                // Delete the signup
                await _tournamentManager.DeleteSignup(signupName, context.Client);

                // Start the tournament matches
                try
                {
                    // Slight delay to allow system to settle
                    await Task.Delay(5000);
                    await StartGroupStageMatches(tournament, context.Client, signup.Type);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error starting matches for tournament {tournament.Name}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }, "Failed to create tournament from signup");
        }

        // New method to create tournament with seeding
        private async Task<Tournament> CreateTournamentWithSeeding(TournamentSignup signup, DiscordChannel channel)
        {
            // Create initial tournament structure
            var tournament = new Tournament
            {
                Name = signup.Name,
                Format = signup.Format,
                GameType = signup.Type,
                CurrentStage = TournamentStage.Groups,
                AnnouncementChannel = channel
            };

            // Determine group count based on player count
            int playerCount = signup.Participants.Count;
            int groupCount = DetermineGroupCount(playerCount, signup.Format);

            // Create the groups
            for (int i = 0; i < groupCount; i++)
            {
                var group = new Tournament.Group { Name = $"Group {(char)('A' + i)}" };
                tournament.Groups.Add(group);
            }

            // Get seeded players
            var seededPlayers = signup.Seeds
                .OrderBy(s => s.Seed)
                .Select(s => s.Player)
                .ToList();

            // Get unseeded players (players not in the seeded list)
            var unseededPlayers = signup.Participants
                .Where(p => !seededPlayers.Any(s => s.Id == p.Id))
                .OrderBy(_ => Guid.NewGuid()) // Randomize unseeded players
                .ToList();

            // Distribute seeded players first using snake draft
            // (1st seed to Group A, 2nd to Group B, ..., last group to last+1, last group-1 to last+2, etc.)
            bool reverseDirection = false;
            int currentGroup = 0;

            foreach (var player in seededPlayers)
            {
                tournament.Groups[currentGroup].Participants.Add(new Tournament.GroupParticipant { Player = player });

                // Move to next group using snake draft pattern
                if (!reverseDirection)
                {
                    currentGroup++;
                    // If we reached the last group, start going backwards
                    if (currentGroup >= groupCount)
                    {
                        currentGroup = groupCount - 1;
                        reverseDirection = true;
                    }
                }
                else
                {
                    currentGroup--;
                    // If we reached the first group, start going forwards
                    if (currentGroup < 0)
                    {
                        currentGroup = 0;
                        reverseDirection = false;
                    }
                }
            }

            // Distribute remaining unseeded players evenly
            for (int i = 0; i < unseededPlayers.Count; i++)
            {
                // Find the group with the fewest players
                var group = tournament.Groups.OrderBy(g => g.Participants.Count).First();
                group.Participants.Add(new Tournament.GroupParticipant { Player = unseededPlayers[i] });
            }

            // Create the matches within each group
            foreach (var group in tournament.Groups)
            {
                // Create round-robin matches
                for (int i = 0; i < group.Participants.Count; i++)
                {
                    for (int j = i + 1; j < group.Participants.Count; j++)
                    {
                        var match = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(group.Participants[i].Player)} vs {GetPlayerDisplayName(group.Participants[j].Player)}",
                            Type = MatchType.GroupStage,
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new() { Player = group.Participants[i].Player },
                                new() { Player = group.Participants[j].Player }
                            }
                        };
                        group.Matches.Add(match);
                    }
                }
            }

            // Add tournament to ongoing tournaments
            _ongoingRounds.Tournaments.Add(tournament);

            // Save state
            await _tournamentManager.SaveAllData();

            return tournament;
        }

        // Helper method to get player display name
        private string GetPlayerDisplayName(object? player)
        {
            if (player is DiscordMember member)
                return member.DisplayName;
            return player?.ToString() ?? "Unknown Player";
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
            [Description("Game type (1v1 or 2v2)")][SlashChoiceProvider<GameTypeChoiceProvider>] string gameType = "OneVsOne",
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

                // Parse game type
                GameType parsedGameType;
                try
                {
                    parsedGameType = Enum.Parse<GameType>(gameType);
                }
                catch (Exception)
                {
                    await context.EditResponseAsync($"Invalid game type: {gameType}. Valid options are 'OneVsOne' or 'TwoVsTwo'.");
                    return;
                }

                // Create the signup using TournamentManager
                var signup = _tournamentManager.CreateSignup(
                    name,
                    Enum.Parse<TournamentFormat>(format),
                    context.User,
                    signupChannelId.Value,
                    parsedGameType,
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
                    await SafeResponse(context, $"Tournament signup '{name}' created successfully. Check {signupChannel.Mention} for the signup form.", null, true, 10);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending signup message: {ex.Message}\n{ex.StackTrace}");
                    await SafeResponse(context, $"Tournament signup '{name}' was created but there was an error creating the signup message: {ex.Message}", null, true, 10);
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

                // Respond to the user and schedule the message to be deleted after 10 seconds
                var message = await context.EditResponseAsync($"You have been added to the tournament '{tournamentName}'.");

                // Delete the message after 10 seconds
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    try
                    {
                        await message.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to auto-delete signup message: {ex.Message}");
                    }
                });
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
                    await SafeResponse(context, $"Tournament '{name}' has been deleted.", null, true, 10);
                    found = true;
                }

                // If not found as a tournament, try as a signup
                if (!found)
                {
                    var signup = _tournamentManager.GetSignup(name);
                    if (signup != null)
                    {
                        // Delete the signup and its related messages
                        await _tournamentManager.DeleteSignup(name, context.Client);
                        await SafeResponse(context, $"Tournament signup '{name}' has been deleted.", null, true, 10);
                        found = true;
                    }
                }

                // If neither tournament nor signup was found
                if (!found)
                {
                    // Get available tournaments and signups for a helpful message
                    var tournaments = _tournamentManager.GetAllTournaments();
                    var signups = _tournamentManager.GetAllSignups();

                    string availableItems = "";
                    if (tournaments.Any())
                        availableItems += $"Available tournaments: {string.Join(", ", tournaments.Select(t => t.Name))}\n";

                    if (signups.Any())
                        availableItems += $"Available signups: {string.Join(", ", signups.Select(s => s.Name))}";

                    if (string.IsNullOrEmpty(availableItems))
                        availableItems = "No tournaments or signups found.";

                    await SafeResponse(context, $"No tournament or signup named '{name}' was found to delete.\n\n{availableItems}", null, true, 10);
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

        [Command("repair_data")]
        [Description("Repair tournament data files (admin only)")]
        public async Task RepairData(CommandContext context)
        {
            await SafeExecute(context, async () =>
            {
                // Check permissions
                if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.Administrator))
                {
                    await context.EditResponseAsync("You don't have permission to repair tournament data.");
                    return;
                }

                // Send initial response
                await context.EditResponseAsync("Starting tournament data repair process...");

                // Create backup directory if it doesn't exist
                string backupDir = Path.Combine("Data", "Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupDir);

                // Get the paths of data files
                string dataDir = Path.Combine("Data");
                string tournamentsFile = Path.Combine(dataDir, "tournaments.json");
                string signupsFile = Path.Combine(dataDir, "signups.json");
                string stateFile = Path.Combine(dataDir, "tournament_state.json");

                // Create backups
                bool backupSuccess = true;
                try
                {
                    if (File.Exists(tournamentsFile))
                        File.Copy(tournamentsFile, Path.Combine(backupDir, "tournaments.json"), true);

                    if (File.Exists(signupsFile))
                        File.Copy(signupsFile, Path.Combine(backupDir, "signups.json"), true);

                    if (File.Exists(stateFile))
                        File.Copy(stateFile, Path.Combine(backupDir, "tournament_state.json"), true);
                }
                catch (Exception ex)
                {
                    backupSuccess = false;
                    _logger.LogError(ex, "Failed to create backups before data repair");
                    await context.Channel.SendMessageAsync($"⚠️ Warning: Failed to create backups: {ex.Message}");
                }

                // Call the repair method
                await _tournamentManager.RepairDataFiles(context.Client);

                // Send success message
                string backupMsg = backupSuccess
                    ? $"Backups created in {backupDir}"
                    : "Warning: Backups could not be created";

                await context.EditResponseAsync($"✅ Tournament data repair completed successfully! {backupMsg}");
            }, "Failed to repair tournament data");
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
                var signup = _tournamentManager.GetSignup(tournamentName);
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

                // Remove any existing seed for this player
                signup.Seeds.RemoveAll(s => s.Player?.Id == player.Id || s.PlayerId == player.Id);

                // If seed is not 0, add the new seed
                if (seed > 0)
                {
                    var participantSeed = new ParticipantSeed
                    {
                        Player = player,
                        PlayerId = player.Id,
                        Seed = seed
                    };
                    signup.Seeds.Add(participantSeed);
                }

                // Update the signup
                _tournamentManager.UpdateSignup(signup);

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
                var tournament = _tournamentManager.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"No tournament found with name '{tournamentName}'", null, true);
                    return;
                }

                // Check if the player is in the tournament
                bool isInTournament = false;
                Tournament.GroupParticipant? participantToSeed = null;

                foreach (var group in tournament.Groups)
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
                await _tournamentManager.SaveTournamentState(context.Client);
            }, "Failed to set tournament seed");
        }

        // Add the DetermineGroupCount method
        private int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // Based on the player count and format, determine how many groups to create
            if (format == TournamentFormat.SingleElimination || format == TournamentFormat.DoubleElimination)
            {
                return 1; // No groups for elimination formats
            }
            else if (format == TournamentFormat.RoundRobin)
            {
                return 1; // Single group for round robin
            }
            else // GroupStageWithPlayoffs
            {
                if (playerCount < 9)
                    return 2;
                else if (playerCount < 17)
                    return 4;
                else
                    return 8;
            }
        }

        private ulong? GetSignupChannelId(CommandContext context)
        {
            if (ConfigManager.Config?.Servers == null) return null;

            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild?.Id);
            return server?.SignupChannelId;
        }

        private DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
        {
            string formatName = signup.Format.ToString();
            string gameTypeName = signup.Type.ToString();

            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle($"🏆 Tournament Signup: {signup.Name}")
                .WithDescription($"Format: {formatName}\nGame Type: {gameTypeName}")
                .WithFooter("Sign up by clicking the button below")
                .WithTimestamp(DateTime.Now);

            if (signup.IsOpen)
            {
                embedBuilder.WithColor(DiscordColor.Green);
            }
            else
            {
                embedBuilder.WithColor(DiscordColor.Red);
                embedBuilder.WithFooter("Sign up by clicking the button below (CLOSED)");
            }

            // Add scheduled start time if available
            if (signup.ScheduledStartTime.HasValue)
            {
                string formattedTime = $"<t:{((DateTimeOffset)signup.ScheduledStartTime).ToUnixTimeSeconds()}:F>";
                embedBuilder.AddField("Scheduled Start Time", formattedTime);
            }

            // Sort participants: seeded players first (by seed value), then non-seeded alphabetically
            var sortedParticipants = signup.Participants
                .Select(p => new
                {
                    Player = p,
                    Seed = signup.Seeds?.FirstOrDefault(s => s.Player?.Id == p.Id || s.PlayerId == p.Id)?.Seed ?? 0,
                    DisplayName = p.DisplayName
                })
                .OrderBy(p => p.Seed == 0) // False (0) comes before True (1), so seeded come first
                .ThenBy(p => p.Seed) // Sort seeded players by seed value
                .ThenBy(p => p.DisplayName) // Sort non-seeded players alphabetically
                .ToList();

            // Add participants field
            if (sortedParticipants.Any())
            {
                var participantsList = new List<string>();
                foreach (var p in sortedParticipants)
                {
                    string seedDisplay = p.Seed > 0 ? $" [Seed #{p.Seed}]" : "";
                    participantsList.Add($"• {p.Player.Mention}{seedDisplay}");
                }

                string participantsText = string.Join("\n", participantsList);

                // If the text is too long, truncate it
                if (participantsText.Length > 1024)
                {
                    participantsText = participantsText.Substring(0, 1020) + "...";
                }

                embedBuilder.AddField($"Participants ({sortedParticipants.Count})", participantsText);
            }
            else
            {
                embedBuilder.AddField("Participants (0)", "No participants yet");
            }

            // Add created by field
            if (signup.CreatedBy != null)
            {
                embedBuilder.AddField("Created by", signup.CreatedBy.Mention);
            }
            else if (!string.IsNullOrEmpty(signup.CreatorUsername))
            {
                embedBuilder.AddField("Created by", signup.CreatorUsername);
            }

            return embedBuilder.Build();
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
                var builder = new DiscordMessageBuilder()
                    .AddEmbed(embed);

                if (signup.IsOpen)
                {
                    // Add signup/withdraw buttons
                    builder.AddComponents(
                        new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_{signup.Name.Replace(" ", "_")}",
                            "Sign Up"
                        ),
                        new DiscordButtonComponent(
                            DiscordButtonStyle.Danger,
                            $"withdraw_{signup.Name.Replace(" ", "_")}",
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
                            $"closed_{signup.Name.Replace(" ", "_")}",
                            "Signups Closed",
                            true // disabled
                        )
                    );
                }

                // Update the message
                await message.ModifyAsync(builder);

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
                    // Use SafeResponse for error messages to make them ephemeral and auto-delete
                    await SafeResponse(context, $"{errorPrefix}: {ex.Message}", null, true, 10);
                }
                catch (Exception responseEx)
                {
                    Console.WriteLine($"Failed to send error response via interaction: {responseEx.Message}");

                    // Fallback to channel message if interaction response fails
                    try
                    {
                        var msg = await context.Channel.SendMessageAsync($"{errorPrefix}: {ex.Message}");

                        // Set up auto-deletion for channel messages too
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(10000); // 10 seconds
                            try
                            {
                                await msg.DeleteAsync();
                            }
                            catch (Exception delEx)
                            {
                                Console.WriteLine($"Failed to auto-delete fallback error message: {delEx.Message}");
                            }
                        });
                    }
                    catch (Exception channelEx)
                    {
                        Console.WriteLine($"Failed to send any error messages: {channelEx.Message}");
                    }
                }
            }
        }

        private async Task StartGroupStageMatches(Tournament tournament, DiscordClient client, GameType gameType)
        {
            try
            {
                Console.WriteLine($"Starting group stage matches for tournament '{tournament.Name}'");

                // Define possible match length settings
                int[] possibleMatchLengths = [1, 3, 5]; // Bo1, Bo3, Bo5
                // Default to Bo3 for group stage, but can be changed to other options
                int defaultMatchLength = 3;

                // Check if we need to retrieve match length preference from a config
                // For now, hardcode to Bo3 for group stage
                int matchLength = defaultMatchLength;

                // Process each group
                foreach (var group in tournament.Groups)
                {
                    Console.WriteLine($"Processing group '{group.Name}' with {group.Participants.Count} participants");

                    // Create all possible match pairs within this group
                    List<(Tournament.GroupParticipant, Tournament.GroupParticipant)> matchPairs = new();

                    // Loop through all participants to create unique pairs
                    for (int i = 0; i < group.Participants.Count; i++)
                    {
                        for (int j = i + 1; j < group.Participants.Count; j++)
                        {
                            matchPairs.Add((group.Participants[i], group.Participants[j]));
                        }
                    }

                    Console.WriteLine($"Created {matchPairs.Count} match pairs for group '{group.Name}'");

                    // Schedule each match
                    foreach (var matchPair in matchPairs)
                    {
                        try
                        {
                            // Check if players are valid DiscordMembers
                            if (matchPair.Item1.Player is not DiscordMember player1 ||
                                matchPair.Item2.Player is not DiscordMember player2)
                            {
                                Console.WriteLine($"One or both players are not valid DiscordMembers. Skipping match.");
                                continue;
                            }

                            // Create match based on game type
                            if (gameType == GameType.OneVsOne)
                            {
                                // Create a 1v1 match with the appropriate match length
                                await CreateAndStart1v1Match(tournament, group, player1, player2, client, matchLength);
                            }
                            else
                            {
                                // 2v2 matches require more logic to find teams
                                // This would need to be implemented based on how teams are formed
                                Console.WriteLine("2v2 matches are not yet implemented for automatic tournament running");
                            }

                            // Add a small delay between match creations to avoid rate limits
                            await Task.Delay(2000);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating match: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"Group stage matches started for tournament '{tournament.Name}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting group stage: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private async Task CreateAndStart1v1Match(Tournament tournament, Tournament.Group group, DiscordMember player1, DiscordMember player2, DiscordClient client, int matchLength)
        {
            try
            {
                Console.WriteLine($"Creating 1v1 match: {player1.DisplayName} vs {player2.DisplayName}");

                // Find the bot channel from configuration
                DiscordChannel? matchChannel = null;
                var guild = player1.Guild;

                // Get server configuration from config
                var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == guild.Id);
                if (server != null && server.BotChannelId.HasValue)
                {
                    try
                    {
                        // Get the bot channel from the config
                        matchChannel = await client.GetChannelAsync(server.BotChannelId.Value);
                        Console.WriteLine($"Using configured bot channel: {matchChannel.Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting bot channel: {ex.Message}");
                    }
                }

                // Fall back to tournament announcement channel if bot channel is not found
                if (matchChannel is null && tournament.AnnouncementChannel is not null)
                {
                    matchChannel = tournament.AnnouncementChannel;
                    Console.WriteLine($"Falling back to tournament announcement channel: {matchChannel.Name}");
                }

                // Final fallback to general channel
                if (matchChannel is null)
                {
                    var channels = await guild.GetChannelsAsync();
                    matchChannel = channels.FirstOrDefault(c => c.Name.Contains("general", StringComparison.OrdinalIgnoreCase)) ??
                                  channels.FirstOrDefault(c => c.Name.Contains("chat", StringComparison.OrdinalIgnoreCase)) ??
                                  channels.FirstOrDefault();

                    if (matchChannel is null)
                    {
                        Console.WriteLine("Could not find a suitable channel for the match");
                        return;
                    }
                    Console.WriteLine($"Falling back to general channel: {matchChannel.Name}");
                }

                // Create a match in the tournament structure
                var match = new Tournament.Match
                {
                    Name = $"{player1.DisplayName} vs {player2.DisplayName}",
                    Type = MatchType.GroupStage,
                    BestOf = matchLength // Use the matchLength parameter
                };

                // Create a round with the same settings as the 1v1 command would
                string pings = $"{player1.Mention} {player2.Mention}";
                var round = new Round
                {
                    Name = match.Name,
                    Length = match.BestOf,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    Pings = pings,
                    TournamentId = tournament.Name // Use tournament name as identifier
                };

                // Add participants
                match.Participants.Add(new Tournament.MatchParticipant { Player = player1 });
                match.Participants.Add(new Tournament.MatchParticipant { Player = player2 });

                // Add the match to the group
                group.Matches.Add(match);

                // Setup map ban options just like in the 1v1 command would
                string?[] maps1v1 = Maps.MapCollection?.Where(m => m.Size == "1v1").Select(m => m.Name).ToArray() ?? Array.Empty<string?>();
                var options = new List<DiscordSelectComponentOption>();
                foreach (var map in maps1v1)
                {
                    if (map is not null)
                    {
                        var option = new DiscordSelectComponentOption(map, map);
                        options.Add(option);
                    }
                }

                // Create dropdown and message based on match length (Bo3 by default)
                DiscordSelectComponent dropdown;
                string message;

                switch (round.Length)
                {
                    case 3:
                        dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Choose 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                            "Only 2 maps from each team will be banned, leaving 4 remaining maps. One of the 3rd priority maps " +
                            "selected will be randomly banned in case both teams ban the same map. " +
                            "You will not know which maps were banned by your opponent, and the remaining maps will be revealed " +
                            "randomly before each game after deck codes have been locked in.\n\n" +
                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    case 5:
                        dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                            "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                            "One of the 2nd priority maps selected by each team will be randomly banned. " +
                            "You will not know which maps were banned by your opponent, " +
                            "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.\n\n" +
                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    default:
                        dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                }

                var dropdownBuilder = new DiscordMessageBuilder()
                    .WithContent(message)
                    .AddComponents(dropdown);

                // Create teams and threads
                List<DiscordMember> players = [player1, player2];
                foreach (var player in players)
                {
                    string displayName = player?.DisplayName ?? "Player";
                    Round.Team team = new() { Name = displayName };
                    Round.Participant participant = new() { Player = player };
                    team.Participants.Add(participant);

                    round.Teams.Add(team);

                    // Create a private thread for this player
                    try
                    {
                        var thread = await matchChannel.CreateThreadAsync(displayName, DiscordAutoArchiveDuration.Day, DiscordChannelType.PrivateThread);
                        team.Thread = thread;

                        // Send map ban options to the thread
                        await thread.SendMessageAsync(dropdownBuilder);
                        if (player is not null)
                            await thread.AddThreadMemberAsync(player);

                        // Add admins and moderators to the thread
                        if (player?.Guild is not null)
                            await AddStaffToThread(player.Guild, thread);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating thread for {displayName}: {ex.Message}");
                        // Continue with the match even if thread creation fails
                    }
                }

                // Link the round to the match
                match.LinkedRound = round;

                // Add the round to ongoing rounds
                _ongoingRounds.TourneyRounds.Add(round);

                // Send a message to the channel
                string matchTypeEmoji = match.BestOf switch
                {
                    1 => "🎯", // Single game (Bo1)
                    3 => "🔄", // Best of 3
                    5 => "🏆", // Best of 5
                    _ => "🎮", // Default
                };

                // Create an embed for the match announcement
                var matchEmbed = new DiscordEmbedBuilder()
                    .WithTitle($"{matchTypeEmoji} Group Stage Match: {player1.DisplayName} vs {player2.DisplayName}")
                    .WithDescription($"A Best of {match.BestOf} match has been created in the tournament: **{tournament.Name}**\n\n" +
                                    $"Please check your private threads in <#{matchChannel.Id}> for map bans and match details.")
                    .WithColor(DiscordColor.Blue)
                    .WithFooter($"Tournament Group: {group.Name}");

                var announceMsg = await matchChannel.SendMessageAsync(matchEmbed);

                // Try to notify players via DM
                try
                {
                    var playerNotifyEmbed = new DiscordEmbedBuilder()
                        .WithTitle($"🎮 Your Tournament Match is Ready")
                        .WithDescription($"A Best of {match.BestOf} match has been created for you in the tournament: **{tournament.Name}**\n\n" +
                                        $"Please check the <#{matchChannel.Id}> channel for your private thread where you'll make map bans.")
                        .WithColor(DiscordColor.Green)
                        .WithFooter("Good luck!");

                    await player1.SendMessageAsync(playerNotifyEmbed);
                    await player2.SendMessageAsync(playerNotifyEmbed);
                }
                catch (Exception ex)
                {
                    // Players may have DMs disabled, don't let this stop the process
                    Console.WriteLine($"Could not send DM to one or more players: {ex.Message}");
                }

                // Add to messages to delete when round is complete
                if (round.MsgToDel == null)
                    round.MsgToDel = new List<DiscordMessage>();
                round.MsgToDel.Add(announceMsg);

                // Save tournament state
                await _tournamentManager.SaveTournamentState(client);

                Console.WriteLine($"Created match: {player1.DisplayName} vs {player2.DisplayName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating 1v1 match: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Adds all admins and moderators from the server to the thread
        /// </summary>
        private async Task AddStaffToThread(DiscordGuild guild, DiscordThreadChannel thread)
        {
            if (guild is null || thread is null)
                return;

            try
            {
                // Get all members with their roles
                var membersAsync = guild.GetAllMembersAsync();
                var members = new List<DiscordMember>();

                // Manually collect members from the async enumerable
                await foreach (var member in membersAsync)
                {
                    members.Add(member);
                }

                // Filter for members with admin or moderator roles
                foreach (var member in members)
                {
                    // Check for admin/mod role names
                    bool hasStaffRole = member.Roles.Any(r =>
                        r.Name.Contains("Admin", StringComparison.OrdinalIgnoreCase) ||
                        r.Name.Contains("Mod", StringComparison.OrdinalIgnoreCase) ||
                        r.Name.Contains("Staff", StringComparison.OrdinalIgnoreCase) ||
                        r.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase));

                    if (hasStaffRole)
                    {
                        try
                        {
                            await thread.AddThreadMemberAsync(member);
                            Console.WriteLine($"Added staff member {member.Username} to thread {thread.Name}");
                        }
                        catch (Exception ex)
                        {
                            // Log but continue if we can't add a specific member
                            Console.WriteLine($"Failed to add staff member {member.Username} to thread: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding staff to thread: {ex.Message}");
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

    public class GameTypeChoiceProvider : IChoiceProvider
    {
        private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> gameTypes = new DiscordApplicationCommandOptionChoice[]
        {
            new("1v1", "OneVsOne"),
            new("2v2", "TwoVsTwo"),
        };

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(gameTypes);
        }
    }
}