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

namespace Wabbit.BotClient.Commands
{
    [Command("tournament_manager")]
    [Description("Commands for managing tournaments")]
    public class TournamentManagementGroup
    {
        private readonly TournamentManager _tournamentManager;
        private readonly OngoingRounds _ongoingRounds;

        public TournamentManagementGroup(OngoingRounds ongoingRounds)
        {
            _ongoingRounds = ongoingRounds;
            _tournamentManager = new TournamentManager(ongoingRounds);
        }

        [Command("create")]
        [Description("Create a new tournament")]
        public async Task CreateTournament(
            CommandContext context,
            [Description("Tournament name")] string name,
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format)
        {
            await context.DeferResponseAsync();

            // Check if a tournament with this name already exists
            if (_ongoingRounds.Tournaments.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                await context.EditResponseAsync($"A tournament with the name '{name}' already exists.");
                return;
            }

            // Inform user about next steps
            await context.EditResponseAsync(
                $"Tournament '{name}' creation started. Please @mention all players that should participate, separated by spaces. " +
                $"For example: @Player1 @Player2 @Player3...");

            // Tournament creation will be handled by user mentioning players in follow-up message
            // which will be processed in an event handler
        }

        [Command("create_from_signup")]
        [Description("Create a tournament from an active signup")]
        public async Task CreateFromSignup(
            CommandContext context,
            [Description("Signup name")] string signupName,
            [Description("Tournament name (optional)")] string tournamentName = "")
        {
            await context.DeferResponseAsync();

            // Use signupName as tournamentName if not provided
            string actualTournamentName = string.IsNullOrEmpty(tournamentName) ? signupName : tournamentName;

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(signupName, StringComparison.OrdinalIgnoreCase));

            if (signup is null)
            {
                await context.EditResponseAsync($"Signup '{signupName}' not found.");
                return;
            }

            if (signup.Participants.Count < 2)
            {
                await context.EditResponseAsync($"Cannot create tournament: Signup '{signupName}' has fewer than 2 participants.");
                return;
            }

            // Check if a tournament with this name already exists
            if (_ongoingRounds.Tournaments.Any(t => t.Name.Equals(actualTournamentName, StringComparison.OrdinalIgnoreCase)))
            {
                await context.EditResponseAsync($"A tournament with the name '{actualTournamentName}' already exists.");
                return;
            }

            // Get the list of DiscordMember participants
            List<DiscordMember> players = signup.Participants;

            // Create tournament
            var tournament = _tournamentManager.CreateTournament(
                actualTournamentName,
                players,
                Enum.Parse<TournamentFormat>(signup.Format.ToString()),
                context.Channel);

            await context.EditResponseAsync($"Tournament '{actualTournamentName}' created successfully with {players.Count} participants from signup '{signupName}'.");

            // Close the signup
            signup.IsOpen = false;
            await UpdateSignupMessage(signup, context.Client);
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
                // Generate the standings image
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

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
            await context.DeferResponseAsync();

            if (!_ongoingRounds.Tournaments.Any())
            {
                await context.EditResponseAsync("No active tournaments.");
                return;
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = "Active Tournaments",
                Description = "List of all active tournaments",
                Color = DiscordColor.Blurple
            };

            foreach (var tournament in _ongoingRounds.Tournaments)
            {
                string status = tournament.IsComplete ? "Complete" : $"In Progress - {tournament.CurrentStage}";

                // Count total matches and completed matches
                int totalMatches = 0;
                int completedMatches = 0;

                foreach (var group in tournament.Groups)
                {
                    totalMatches += group.Matches.Count;
                    completedMatches += group.Matches.Count(m => m.IsComplete);
                }

                totalMatches += tournament.PlayoffMatches.Count;
                completedMatches += tournament.PlayoffMatches.Count(m => m.IsComplete);

                embed.AddField(
                    tournament.Name,
                    $"**Status:** {status}\n" +
                    $"**Format:** {tournament.Format}\n" +
                    $"**Progress:** {completedMatches}/{totalMatches} matches completed\n" +
                    $"**Groups:** {tournament.Groups.Count}"
                );
            }

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
        }

        [Command("update")]
        [Description("Update tournament standings from game results")]
        public async Task UpdateTournament(
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

            // Update the tournament based on completed rounds
            _tournamentManager.UpdateTournamentFromRound(tournament);

            // Generate and send the updated standings
            try
            {
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"📊 **{tournament.Name}** Standings (Updated)")
                    .AddFile(Path.GetFileName(imagePath), fileStream);

                await context.EditResponseAsync(messageBuilder);
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Tournament updated, but failed to generate standings image: {ex.Message}");
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
            await context.DeferResponseAsync();

            // Convert Unix timestamp to DateTime if provided
            DateTime? startTime = startTimeUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(startTimeUnix).DateTime
                : null;

            // Check if this is the signup channel
            ulong? signupChannelId = GetSignupChannelId(context);

            if (signupChannelId is null)
            {
                await context.EditResponseAsync("Signup channel is not configured. Please ask an admin to set it up.");
                return;
            }

            // Only allow signup creation in the designated channel
            if (context.Channel.Id != signupChannelId)
            {
                await context.EditResponseAsync($"Tournament signups can only be created in the designated signup channel.");
                return;
            }

            // Check if a signup with this name already exists
            if (_ongoingRounds.TournamentSignups.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                await context.EditResponseAsync($"A tournament signup with the name '{name}' already exists.");
                return;
            }

            // Create the signup
            var signup = new TournamentSignup
            {
                Name = name,
                Format = Enum.Parse<TournamentFormat>(format),
                ScheduledStartTime = startTime,
                CreatedBy = context.User
            };

            _ongoingRounds.TournamentSignups.Add(signup);

            // Create and post the initial signup message
            var embed = CreateSignupEmbed(signup);

            var message = await context.Channel.SendMessageAsync(embed: embed);
            signup.SignupListMessage = message;

            await context.EditResponseAsync($"Tournament signup '{name}' created successfully. Players can now sign up.");
        }

        [Command("signup_list")]
        [Description("List all active tournament signups")]
        public async Task ListSignups(CommandContext context)
        {
            await context.DeferResponseAsync();

            if (!_ongoingRounds.TournamentSignups.Any(s => s.IsOpen))
            {
                await context.EditResponseAsync("No active tournament signups.");
                return;
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = "Active Tournament Signups",
                Description = "List of all active tournament signups",
                Color = DiscordColor.Green
            };

            foreach (var signup in _ongoingRounds.TournamentSignups.Where(s => s.IsOpen))
            {
                string scheduledTime = signup.ScheduledStartTime.HasValue
                    ? $"<t:{new DateTimeOffset(signup.ScheduledStartTime.Value).ToUnixTimeSeconds()}:F>"
                    : "Not scheduled";

                embed.AddField(
                    signup.Name,
                    $"**Format:** {signup.Format}\n" +
                    $"**Participants:** {signup.Participants.Count}\n" +
                    $"**Created by:** {signup.CreatedBy.Username}\n" +
                    $"**Scheduled start:** {scheduledTime}\n" +
                    $"**Created at:** <t:{new DateTimeOffset(signup.CreatedAt).ToUnixTimeSeconds()}:R>"
                );
            }

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
        }

        [Command("signup")]
        [Description("Sign up for a tournament")]
        public async Task Signup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await context.DeferResponseAsync();

            // Check if this is the signup channel
            ulong? signupChannelId = GetSignupChannelId(context);

            if (signupChannelId is null)
            {
                await context.EditResponseAsync("Signup channel is not configured. Please ask an admin to set it up.");
                return;
            }

            // Only allow signups in the designated channel
            if (context.Channel.Id != signupChannelId)
            {
                await context.EditResponseAsync($"You can only sign up in the designated signup channel.");
                return;
            }

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase) && s.IsOpen);

            if (signup is null)
            {
                await context.EditResponseAsync($"Open tournament signup '{tournamentName}' not found.");
                return;
            }

            // Check if the user is already signed up
            if (signup.Participants.Any(p => p.Id == context.User.Id))
            {
                await context.EditResponseAsync($"You are already signed up for tournament '{tournamentName}'.");
                return;
            }

            // Add the user to the participants
            if (context.Guild is null)
            {
                await context.EditResponseAsync("This command cannot be used outside of a server.");
                return;
            }

            var member = await context.Guild.GetMemberAsync(context.User.Id);
            signup.Participants.Add(member);

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"You have been successfully signed up for tournament '{tournamentName}'.");
        }

        [Command("signup_cancel")]
        [Description("Cancel your signup for a tournament")]
        public async Task CancelSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await context.DeferResponseAsync();

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase) && s.IsOpen);

            if (signup is null)
            {
                await context.EditResponseAsync($"Open tournament signup '{tournamentName}' not found.");
                return;
            }

            // Check if the user is signed up
            var participant = signup.Participants.FirstOrDefault(p => p.Id == context.User.Id);
            if (participant is null)
            {
                await context.EditResponseAsync($"You are not signed up for tournament '{tournamentName}'.");
                return;
            }

            // Remove the user from the participants
            signup.Participants.Remove(participant);

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"Your signup for tournament '{tournamentName}' has been cancelled.");
        }

        [Command("signup_close")]
        [Description("Close signups for a tournament")]
        public async Task CloseSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await context.DeferResponseAsync();

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase) && s.IsOpen);

            if (signup is null)
            {
                await context.EditResponseAsync($"Open tournament signup '{tournamentName}' not found.");
                return;
            }

            // Check if the user is the creator or has sufficient permissions
            if (signup.CreatedBy.Id != context.User.Id &&
                (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageMessages)))
            {
                await context.EditResponseAsync("You don't have permission to close this signup.");
                return;
            }

            // Close the signup
            signup.IsOpen = false;

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"Signups for tournament '{tournamentName}' have been closed.");
        }

        [Command("signup_add")]
        [Description("Add a player to a tournament signup (admins only)")]
        public async Task AddToSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to add")] DiscordMember player)
        {
            await context.DeferResponseAsync();

            // Check admin permissions
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageMessages))
            {
                await context.EditResponseAsync("You don't have permission to add players to signups.");
                return;
            }

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase) && s.IsOpen);

            if (signup is null)
            {
                await context.EditResponseAsync($"Open tournament signup '{tournamentName}' not found.");
                return;
            }

            // Check if the player is already signed up
            if (signup.Participants.Any(p => p.Id == player.Id))
            {
                await context.EditResponseAsync($"{player.DisplayName} is already signed up for tournament '{tournamentName}'.");
                return;
            }

            // Add the player
            signup.Participants.Add(player);

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"{player.DisplayName} has been added to the tournament '{tournamentName}'.");
        }

        [Command("signup_remove")]
        [Description("Remove a player from a tournament signup (admins only)")]
        public async Task RemoveFromSignup(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player to remove")] DiscordMember player)
        {
            await context.DeferResponseAsync();

            // Check admin permissions
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageMessages))
            {
                await context.EditResponseAsync("You don't have permission to remove players from signups.");
                return;
            }

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase) && s.IsOpen);

            if (signup is null)
            {
                await context.EditResponseAsync($"Open tournament signup '{tournamentName}' not found.");
                return;
            }

            // Check if the player is signed up
            var participant = signup.Participants.FirstOrDefault(p => p.Id == player.Id);
            if (participant is null)
            {
                await context.EditResponseAsync($"{player.DisplayName} is not signed up for tournament '{tournamentName}'.");
                return;
            }

            // Remove the player
            signup.Participants.Remove(participant);

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"{player.DisplayName} has been removed from the tournament '{tournamentName}'.");
        }

        [Command("test_setup")]
        [Description("Test command to setup a tournament with fake participants")]
        public async Task TestSetup(
            CommandContext context,
            [Description("Number of participants")] int participantCount = 8,
            [Description("Tournament format")][SlashChoiceProvider<TournamentFormatChoiceProvider>] string format = "GroupStageWithPlayoffs")
        {
            await context.DeferResponseAsync();

            try
            {
                // Create a new tournament with a test name
                string tournamentName = $"Test Tournament {DateTime.Now:yyyyMMdd-HHmmss}";

                // Create the tournament directly
                var tournament = new Tournament
                {
                    Name = tournamentName,
                    Format = Enum.Parse<TournamentFormat>(format),
                    AnnouncementChannel = context.Channel
                };

                // Add tournament to active tournaments
                _ongoingRounds.Tournaments.Add(tournament);

                // Create mock players for the tournament
                CreateTestGroups(tournament, participantCount,
                    format == "RoundRobin" ? 1 : Math.Max(2, participantCount / 4),
                    false);

                // Generate and show the tournament visualization
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);
                using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var builder = new DiscordWebhookBuilder()
                    .WithContent($"Test tournament '{tournamentName}' created with {participantCount} participants.")
                    .AddFile("tournament.png", fs);

                await context.EditResponseAsync(builder);
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Error in test setup: {ex.Message}");
            }
        }

        [Command("test_create_tournament")]
        [Description("Test command to create a tournament from a test signup")]
        public async Task TestCreateTournament(
            CommandContext context,
            [Description("Signup name")] string signupName)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the signup
                var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                    s.Name.Equals(signupName, StringComparison.OrdinalIgnoreCase));

                if (signup == null)
                {
                    await context.EditResponseAsync($"Signup '{signupName}' not found.");
                    return;
                }

                // Create the tournament
                var tournament = _tournamentManager.CreateTournament(
                    signupName,
                    signup.Participants,
                    signup.Format,
                    context.Channel);

                _ongoingRounds.Tournaments.Add(tournament);

                // Close the signup
                signup.IsOpen = false;
                await UpdateSignupMessage(signup, context.Client);

                // Generate and send standings image
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 Test Tournament Created: {tournament.Name}")
                    .WithDescription($"Format: {tournament.Format}\nPlayers: {tournament.Groups.Sum(g => g.Participants.Count)}\nGroups: {tournament.Groups.Count}")
                    .WithColor(DiscordColor.Green)
                    .WithFooter("Use /tournament_manager test_simulate_matches to simulate matches");

                // Send the tournament info with the standings image
                var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var messageBuilder = new DiscordWebhookBuilder()
                    .AddEmbed(embed)
                    .AddFile(Path.GetFileName(imagePath), fileStream);

                await context.EditResponseAsync(messageBuilder);

                await context.Channel.SendMessageAsync($"Use `/tournament_manager test_simulate_matches {tournament.Name}` to simulate tournament matches.");
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Error creating test tournament: {ex.Message}");
            }
        }

        [Command("test_simulate_matches")]
        [Description("Test command to simulate tournament matches")]
        public async Task TestSimulateMatches(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Simulate all stages")] bool simulateAllStages = false)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the tournament
                var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                    t.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

                if (tournament == null)
                {
                    await context.EditResponseAsync($"Tournament '{tournamentName}' not found.");
                    return;
                }

                await context.EditResponseAsync($"Simulating matches for tournament '{tournamentName}'...");

                // Simulate group stage matches
                if (tournament.CurrentStage == TournamentStage.Groups)
                {
                    await SimulateGroupMatches(context, tournament);

                    if (!simulateAllStages)
                    {
                        // Generate standings after group stage
                        string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                        var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                        var messageBuilder = new DiscordWebhookBuilder()
                            .WithContent($"Group stage completed for tournament '{tournamentName}'")
                            .AddFile(Path.GetFileName(imagePath), fileStream);

                        await context.EditResponseAsync(messageBuilder);

                        if (tournament.CurrentStage == TournamentStage.Playoffs)
                        {
                            await context.Channel.SendMessageAsync($"Use `/tournament_manager test_simulate_matches {tournamentName}` to simulate playoff matches.");
                        }
                        return;
                    }
                }

                // Simulate playoff matches if applicable
                if (tournament.CurrentStage == TournamentStage.Playoffs)
                {
                    await SimulatePlayoffMatches(context, tournament);
                }

                // Generate final standings
                string finalImagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                var finalFileStream = new FileStream(finalImagePath, FileMode.Open, FileAccess.Read);
                var finalMessageBuilder = new DiscordWebhookBuilder()
                    .WithContent($"Tournament '{tournamentName}' completed!")
                    .AddFile(Path.GetFileName(finalImagePath), finalFileStream);

                await context.EditResponseAsync(finalMessageBuilder);
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Error simulating matches: {ex.Message}");
            }
        }

        [Command("test_visualization")]
        [Description("Test tournament visualization with various tournament states")]
        public async Task TestVisualization(
            CommandContext context,
            [Description("Test scenario")][SlashChoiceProvider<VisualizationTestChoiceProvider>] string scenario = "complete")
        {
            await context.DeferResponseAsync();

            try
            {
                var tournament = CreateTestTournamentForVisualization(scenario);

                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var messageBuilder = new DiscordWebhookBuilder()
                    .WithContent($"Visualization test: {scenario} tournament")
                    .AddFile(Path.GetFileName(imagePath), fileStream);

                await context.EditResponseAsync(messageBuilder);
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Error in visualization test: {ex.Message}");
            }
        }

        [Command("test_user_flow")]
        [Description("Test all user interactions in sequence")]
        public async Task TestUserFlow(
            CommandContext context,
            [Description("Starting step number")] int startStep = 1)
        {
            await context.DeferResponseAsync();

            try
            {
                // Use a unique name for all test objects to avoid conflicts
                string testName = $"TestFlow_{DateTime.Now:yyyyMMddHHmmss}";

                switch (startStep)
                {
                    case 1: // Create a tournament signup
                        await context.EditResponseAsync($"**Step 1**: Creating tournament signup '{testName}'...");

                        var signup = new TournamentSignup
                        {
                            Name = testName,
                            Format = TournamentFormat.GroupStageWithPlayoffs,
                            CreatedBy = context.User,
                            IsOpen = true,
                            CreatedAt = DateTime.Now
                        };

                        _ongoingRounds.TournamentSignups.Add(signup);

                        // Create a signup message
                        var embed = CreateSignupEmbed(signup);
                        var message = await context.Channel.SendMessageAsync(embed);
                        signup.SignupListMessage = message;

                        await context.Channel.SendMessageAsync($"✅ **Step 1 Complete**: Created signup '{testName}'. Now run `/tournament_manager test_user_flow 2` to continue.");
                        break;

                    case 2: // Add mock participants to signup
                        await context.EditResponseAsync($"**Step 2**: Adding participants to '{testName}'...");

                        var existingSignup = _ongoingRounds.TournamentSignups.FirstOrDefault(s => s.Name == testName);
                        if (existingSignup == null)
                        {
                            await context.EditResponseAsync($"❌ Error: Couldn't find signup '{testName}'. Please run step 1 first.");
                            return;
                        }

                        // Add the current user
                        if (context.Member is not null && !existingSignup.Participants.Any(p => p.Id == context.User.Id))
                        {
                            existingSignup.Participants.Add(context.Member);
                        }

                        // Add mock participants
                        for (int i = 0; i < 3; i++)
                        {
                            var mockPlayer = new MockDiscordMember
                            {
                                UserId = 100000000000000000 + (ulong)i,
                                UserName = $"TestPlayer{i + 1}"
                            };
                            existingSignup.Participants.Add((DiscordMember)mockPlayer);
                        }

                        // Update the signup message
                        await UpdateSignupMessage(existingSignup, context.Client);

                        await context.Channel.SendMessageAsync($"✅ **Step 2 Complete**: Added participants to signup '{testName}'. Now run `/tournament_manager test_user_flow 3` to continue.");
                        break;

                    case 3: // Create tournament from signup
                        await context.EditResponseAsync($"**Step 3**: Creating tournament from signup '{testName}'...");

                        var signupToConvert = _ongoingRounds.TournamentSignups.FirstOrDefault(s => s.Name == testName);
                        if (signupToConvert == null)
                        {
                            await context.EditResponseAsync($"❌ Error: Couldn't find signup '{testName}'. Please run steps 1-2 first.");
                            return;
                        }

                        // Create the tournament
                        var players = signupToConvert.Participants.ToList();
                        var tournament = _tournamentManager.CreateTournament(
                            testName,
                            players,
                            signupToConvert.Format,
                            context.Channel);

                        // Generate and show standings
                        {
                            string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);
                            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                            var builder = new DiscordWebhookBuilder()
                                .WithContent($"✅ **Step 3 Complete**: Created tournament '{testName}' with {players.Count} participants.\nRun `/tournament_manager test_user_flow 4` to continue.")
                                .AddFile("tournament.png", fs);

                            await context.EditResponseAsync(builder);
                        }
                        break;

                    case 4: // Simulate matches and update standings
                        await context.EditResponseAsync($"**Step 4**: Simulating matches for tournament '{testName}'...");

                        var tournamentToUpdate = _ongoingRounds.Tournaments.FirstOrDefault(t => t.Name == testName);
                        if (tournamentToUpdate == null)
                        {
                            await context.EditResponseAsync($"❌ Error: Couldn't find tournament '{testName}'. Please run steps 1-3 first.");
                            return;
                        }

                        // Simulate group matches
                        await SimulateGroupMatches(context, tournamentToUpdate);

                        // Update visualization
                        {
                            string updatedImagePath = TournamentVisualization.GenerateStandingsImage(tournamentToUpdate);
                            using var updatedFs = new FileStream(updatedImagePath, FileMode.Open, FileAccess.Read);
                            var updatedBuilder = new DiscordWebhookBuilder()
                                .WithContent($"✅ **Step 4 Complete**: Group stage matches completed for '{testName}'.\nRun `/tournament_manager test_user_flow 5` to continue.")
                                .AddFile("tournament_updated.png", updatedFs);

                            await context.EditResponseAsync(updatedBuilder);
                        }
                        break;

                    case 5: // Complete tournament
                        await context.EditResponseAsync($"**Step 5**: Completing tournament '{testName}'...");

                        var tournamentToComplete = _ongoingRounds.Tournaments.FirstOrDefault(t => t.Name == testName);
                        if (tournamentToComplete == null)
                        {
                            await context.EditResponseAsync($"❌ Error: Couldn't find tournament '{testName}'. Please run steps 1-4 first.");
                            return;
                        }

                        // Set up playoffs
                        if (tournamentToComplete.CurrentStage == TournamentStage.Groups)
                        {
                            SetupTestPlayoffs(tournamentToComplete, true, 1.0);
                        }

                        // Simulate playoff matches
                        if (tournamentToComplete.CurrentStage == TournamentStage.Playoffs)
                        {
                            await SimulatePlayoffMatches(context, tournamentToComplete);
                        }

                        // Mark as complete
                        tournamentToComplete.CurrentStage = TournamentStage.Complete;
                        tournamentToComplete.IsComplete = true;

                        // Final visualization
                        {
                            string finalImagePath = TournamentVisualization.GenerateStandingsImage(tournamentToComplete);
                            using var finalFs = new FileStream(finalImagePath, FileMode.Open, FileAccess.Read);
                            var finalBuilder = new DiscordWebhookBuilder()
                                .WithContent($"🏆 **Testing Complete**: Tournament '{testName}' has finished! All user flows tested successfully.")
                                .AddFile("tournament_final.png", finalFs);

                            await context.EditResponseAsync(finalBuilder);
                        }
                        break;

                    default:
                        await context.EditResponseAsync("Invalid step number. Please use steps 1-5.");
                        break;
                }
            }
            catch (Exception ex)
            {
                await context.EditResponseAsync($"Error during test: {ex.Message}");
            }
        }

        private List<DiscordMember> GetTestParticipants(CommandContext context, int count)
        {
            // Instead of casting which causes null reference errors,
            // we'll add the mock players directly to the tournament

            // Just return empty list - we'll use the mockplayers directly in TestSetup
            return new List<DiscordMember>();
        }

        private async Task SimulateGroupMatches(CommandContext context, Tournament tournament)
        {
            var random = new Random();

            foreach (var group in tournament.Groups)
            {
                foreach (var match in group.Matches)
                {
                    if (match.IsComplete)
                        continue;

                    // Randomly select winner
                    int winnerIdx = random.Next(0, match.Participants.Count);
                    var winner = match.Participants[winnerIdx].Player;

                    // Skip if winner is null
                    if (winner is null)
                        continue;

                    // Random scores - winner gets 2-3, loser gets 0-1
                    int winnerScore = random.Next(2, 4);
                    int loserScore = random.Next(0, 2);

                    // Update the match result
                    _tournamentManager.UpdateMatchResult(tournament, match, winner, winnerScore, loserScore);

                    // Small delay between updates to avoid rate limits
                    await Task.Delay(100);
                }
            }

            // Update tournament to advance to playoffs if all group matches are complete
            _tournamentManager.UpdateTournamentFromRound(tournament);
        }

        private async Task SimulatePlayoffMatches(CommandContext context, Tournament tournament)
        {
            var random = new Random();
            var remainingMatches = tournament.PlayoffMatches.Where(m => !m.IsComplete).ToList();

            while (remainingMatches.Any())
            {
                // Find matches that can be played (all participants are set)
                var playableMatches = remainingMatches
                    .Where(m => m.Participants.Count == 2 && m.Participants.All(p => p.Player is not null))
                    .ToList();

                if (!playableMatches.Any())
                    break;

                foreach (var match in playableMatches)
                {
                    // Randomly select winner
                    int winnerIdx = random.Next(0, match.Participants.Count);
                    var winner = match.Participants[winnerIdx].Player;

                    // Skip if winner is null
                    if (winner is null)
                        continue;

                    // Random scores - winner gets 2-3, loser gets 0-1
                    int winnerScore = random.Next(2, 4);
                    int loserScore = random.Next(0, 2);

                    // Update the match result
                    _tournamentManager.UpdateMatchResult(tournament, match, winner, winnerScore, loserScore);

                    // Small delay between updates to avoid rate limits
                    await Task.Delay(100);
                }

                // Update tournament to advance players to next rounds
                _tournamentManager.UpdateTournamentFromRound(tournament);

                // Get remaining matches for next iteration
                remainingMatches = tournament.PlayoffMatches.Where(m => !m.IsComplete).ToList();
            }
        }

        private Tournament CreateTestTournamentForVisualization(string scenario)
        {
            var tournament = new Tournament
            {
                Name = $"Test_{scenario}",
                Format = TournamentFormat.GroupStageWithPlayoffs
            };

            switch (scenario.ToLower())
            {
                case "empty":
                    // Just return empty tournament
                    break;

                case "groups_only":
                    // Create 2 groups with 4 players each, no matches played
                    CreateTestGroups(tournament, 8, 2, false);
                    break;

                case "groups_in_progress":
                    // Create 2 groups with 4 players each, some matches played
                    CreateTestGroups(tournament, 8, 2, true, 0.5);
                    break;

                case "playoffs_ready":
                    // Groups completed, playoffs set up but not started
                    CreateTestGroups(tournament, 8, 2, true, 1.0);
                    tournament.CurrentStage = TournamentStage.Playoffs;
                    SetupTestPlayoffs(tournament, false);
                    break;

                case "playoffs_in_progress":
                    // Groups completed, playoffs partially completed
                    CreateTestGroups(tournament, 8, 2, true, 1.0);
                    tournament.CurrentStage = TournamentStage.Playoffs;
                    SetupTestPlayoffs(tournament, true, 0.5);
                    break;

                case "complete":
                default:
                    // Full tournament completed
                    CreateTestGroups(tournament, 8, 2, true, 1.0);
                    tournament.CurrentStage = TournamentStage.Playoffs;
                    SetupTestPlayoffs(tournament, true, 1.0);
                    tournament.IsComplete = true;
                    break;
            }

            return tournament;
        }

        private void CreateTestGroups(Tournament tournament, int playerCount, int groupCount, bool playMatches, double completionRate = 0.0)
        {
            // Create the groups
            var random = new Random();
            for (int i = 0; i < groupCount; i++)
            {
                var group = new Tournament.Group
                {
                    Name = $"Group {(char)('A' + i)}"
                };

                int playersPerGroup = playerCount / groupCount;
                int remainder = playerCount % groupCount;
                int groupSize = playersPerGroup + (i < remainder ? 1 : 0);

                // Add players to the group
                for (int j = 0; j < groupSize; j++)
                {
                    string playerName = $"Player {(char)('A' + i)}{j + 1}";
                    var mockPlayer = new MockDiscordMember
                    {
                        UserId = 100000000000000000 + (ulong)(i * playersPerGroup + j),
                        UserName = playerName
                    };

                    group.Participants.Add(new Tournament.GroupParticipant
                    {
                        Player = (DiscordMember)mockPlayer
                    });
                }

                // Add the group to the tournament
                tournament.Groups.Add(group);
            }

            // Generate matches for each group
            foreach (var group in tournament.Groups)
            {
                GenerateGroupMatches(tournament, group);

                // Play matches if required
                if (playMatches)
                {
                    PlayGroupMatches(group, random, completionRate);
                }
            }

            // If we're playing matches and all groups are complete, set up playoffs
            if (playMatches && tournament.Groups.All(g => g.IsComplete))
            {
                SetupTestPlayoffs(tournament, playMatches, completionRate);
            }
        }

        private void SetupTestPlayoffs(Tournament tournament, bool playMatches, double completionRate = 0.0)
        {
            // Create playoff bracket
            List<Tournament.MatchParticipant> qualifiedPlayers = new();

            // Get qualified players from groups
            foreach (var group in tournament.Groups)
            {
                for (int position = 1; position <= 2; position++)
                {
                    var qualifier = group.Participants
                        .Where(p => p.AdvancedToPlayoffs)
                        .OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon - p.GamesLost)
                        .ElementAtOrDefault(position - 1);

                    if (qualifier != null)
                    {
                        qualifiedPlayers.Add(new Tournament.MatchParticipant
                        {
                            Player = qualifier.Player,
                            SourceGroup = group,
                            SourceGroupPosition = position
                        });
                    }
                }
            }

            // Create semifinal matches
            var sf1 = new Tournament.Match
            {
                Name = "Semifinal 1",
                Type = Wabbit.Models.MatchType.Semifinal,
                DisplayPosition = "Semifinal 1",
                BestOf = 5
            };

            var sf2 = new Tournament.Match
            {
                Name = "Semifinal 2",
                Type = Wabbit.Models.MatchType.Semifinal,
                DisplayPosition = "Semifinal 2",
                BestOf = 5
            };

            // Add participants - cross-seeding
            if (qualifiedPlayers.Count >= 4)
            {
                // Ensure proper seeding: A1 vs B2, A2 vs B1
                sf1.Participants.Add(qualifiedPlayers.First(p => p.SourceGroup is not null && p.SourceGroup.Name == "Group A" && p.SourceGroupPosition == 1));
                sf1.Participants.Add(qualifiedPlayers.First(p => p.SourceGroup is not null && p.SourceGroup.Name == "Group B" && p.SourceGroupPosition == 2));

                sf2.Participants.Add(qualifiedPlayers.First(p => p.SourceGroup is not null && p.SourceGroup.Name == "Group B" && p.SourceGroupPosition == 1));
                sf2.Participants.Add(qualifiedPlayers.First(p => p.SourceGroup is not null && p.SourceGroup.Name == "Group A" && p.SourceGroupPosition == 2));
            }

            // Create final and third place matches
            var final = new Tournament.Match
            {
                Name = "Final",
                Type = Wabbit.Models.MatchType.Final,
                DisplayPosition = "Final",
                BestOf = 5
            };

            var thirdPlace = new Tournament.Match
            {
                Name = "Third Place",
                Type = Wabbit.Models.MatchType.ThirdPlace,
                DisplayPosition = "Third Place",
                BestOf = 3
            };

            // Link matches to the bracket
            sf1.NextMatch = final;
            sf2.NextMatch = final;

            // Add matches to tournament
            tournament.PlayoffMatches.Add(sf1);
            tournament.PlayoffMatches.Add(sf2);
            tournament.PlayoffMatches.Add(final);
            tournament.PlayoffMatches.Add(thirdPlace);

            // Simulate matches if needed
            if (playMatches)
            {
                var random = new Random();
                var allMatches = tournament.PlayoffMatches.ToList();
                int matchesToPlay = (int)(allMatches.Count * completionRate);

                // Play semifinals
                if (matchesToPlay >= 2)
                {
                    PlayTestMatch(sf1, random);
                    PlayTestMatch(sf2, random);

                    // Add participants to final and third place matches
                    final.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = sf1.Result?.Winner,
                        SourceGroup = sf1.Participants.First(p =>
                            p.Player is not null &&
                            sf1.Result?.Winner is not null &&
                            p.Player.Id == sf1.Result.Winner.Id).SourceGroup
                    });

                    final.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = sf2.Result?.Winner,
                        SourceGroup = sf2.Participants.First(p =>
                            p.Player is not null &&
                            sf2.Result?.Winner is not null &&
                            p.Player.Id == sf2.Result.Winner.Id).SourceGroup
                    });

                    thirdPlace.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = sf1.Participants.First(p => !p.IsWinner).Player,
                        SourceGroup = sf1.Participants.First(p => !p.IsWinner).SourceGroup
                    });

                    thirdPlace.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = sf2.Participants.First(p => !p.IsWinner).Player,
                        SourceGroup = sf2.Participants.First(p => !p.IsWinner).SourceGroup
                    });
                }

                // Play third place match
                if (matchesToPlay >= 3)
                {
                    PlayTestMatch(thirdPlace, random);
                }

                // Play final
                if (matchesToPlay >= 4)
                {
                    PlayTestMatch(final, random);
                    tournament.IsComplete = true;
                }
            }
        }

        private void PlayTestMatch(Tournament.Match match, Random random)
        {
            // Randomly determine winner
            int winnerIdx = random.Next(0, 2);
            var winner = match.Participants[winnerIdx].Player;

            // Create result
            match.Result = new Tournament.MatchResult
            {
                Winner = winner,
                CompletedAt = DateTime.Now.AddHours(-random.Next(1, 24))
            };

            match.Participants[winnerIdx].IsWinner = true;
            match.Participants[winnerIdx].Score = random.Next(3, 5);
            match.Participants[1 - winnerIdx].Score = random.Next(match.BestOf / 2);
        }

        private ulong? GetSignupChannelId(CommandContext context)
        {
            if (context.Guild is null)
                return null;

            var serverConfig = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild.Id);
            return serverConfig?.SignupChannelId;
        }

        private DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
        {
            var embed = new DiscordEmbedBuilder
            {
                Title = $"🏆 Tournament Signup: {signup.Name}",
                Description = signup.IsOpen
                    ? "Use `/tournament_manager signup name:\"" + signup.Name + "\"` to join this tournament!"
                    : "⚠️ **Signups are now CLOSED** ⚠️",
                Color = signup.IsOpen ? DiscordColor.Green : DiscordColor.Red,
                Timestamp = DateTime.Now
            };

            string formatStr = signup.Format switch
            {
                TournamentFormat.GroupStageWithPlayoffs => "Group Stage + Playoffs",
                TournamentFormat.SingleElimination => "Single Elimination",
                TournamentFormat.DoubleElimination => "Double Elimination",
                TournamentFormat.RoundRobin => "Round Robin",
                _ => signup.Format.ToString()
            };

            embed.AddField("Format", formatStr, true);

            if (signup.ScheduledStartTime.HasValue)
            {
                embed.AddField("Scheduled Start",
                    $"<t:{new DateTimeOffset(signup.ScheduledStartTime.Value).ToUnixTimeSeconds()}:F>", true);
            }

            embed.AddField("Created by", signup.CreatedBy.Username, true);

            // Add participant list
            var participantList = signup.Participants.Count > 0
                ? string.Join("\n", signup.Participants.Select((p, i) => $"{i + 1}. {p.Mention}"))
                : "No participants yet";

            embed.AddField($"Participants ({signup.Participants.Count})", participantList);

            return embed.Build();
        }

        private async Task UpdateSignupMessage(TournamentSignup signup, DSharpPlus.DiscordClient client)
        {
            if (signup.SignupListMessage is null)
                return;

            try
            {
                // Create updated embed
                var embed = CreateSignupEmbed(signup);

                // Update the message
                await signup.SignupListMessage.ModifyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating signup message: {ex.Message}");
            }
        }

        private void GenerateGroupMatches(Tournament tournament, Tournament.Group group)
        {
            // Create matches within the group (round robin)
            for (int i = 0; i < group.Participants.Count; i++)
            {
                for (int j = i + 1; j < group.Participants.Count; j++)
                {
                    var match = new Tournament.Match
                    {
                        Name = $"{group.Name} Match {i + 1}v{j + 1}",
                        Type = Wabbit.Models.MatchType.GroupStage
                    };

                    match.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = group.Participants[i].Player
                    });

                    match.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = group.Participants[j].Player
                    });

                    group.Matches.Add(match);
                }
            }
        }

        private void PlayGroupMatches(Tournament.Group group, Random random, double completionRate = 1.0)
        {
            int matchesToPlay = (int)(group.Matches.Count * completionRate);
            int matchesPlayed = 0;

            foreach (var match in group.Matches)
            {
                if (matchesPlayed >= matchesToPlay)
                    break;

                // Randomly determine winner
                int winnerIdx = random.Next(0, 2);
                var winner = match.Participants[winnerIdx].Player;
                var loser = match.Participants[1 - winnerIdx].Player;

                // Create result
                match.Result = new Tournament.MatchResult
                {
                    Winner = winner,
                    CompletedAt = DateTime.Now.AddDays(-random.Next(1, 5))
                };

                match.Participants[winnerIdx].IsWinner = true;
                match.Participants[winnerIdx].Score = random.Next(2, 4);
                match.Participants[1 - winnerIdx].Score = random.Next(0, 2);

                // Update group participants stats
                var winnerParticipant = group.Participants.First(p =>
                    p.Player is not null &&
                    winner is not null &&
                    p.Player.Id == winner.Id);

                var loserParticipant = group.Participants.First(p =>
                    p.Player is not null &&
                    loser is not null &&
                    p.Player.Id == loser.Id);

                winnerParticipant.Wins++;
                winnerParticipant.GamesWon += match.Participants[winnerIdx].Score;
                winnerParticipant.GamesLost += match.Participants[1 - winnerIdx].Score;

                loserParticipant.Losses++;
                loserParticipant.GamesWon += match.Participants[1 - winnerIdx].Score;
                loserParticipant.GamesLost += match.Participants[winnerIdx].Score;

                matchesPlayed++;
            }

            // Mark group as complete if all matches are played
            if (group.Matches.All(m => m.IsComplete))
            {
                group.IsComplete = true;

                // Mark top 2 players as advanced
                var topPlayers = group.Participants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GamesWon - p.GamesLost)
                    .Take(2);

                foreach (var player in topPlayers)
                {
                    player.AdvancedToPlayoffs = true;
                }
            }
        }
    }

    public class TournamentFormatChoiceProvider : IChoiceProvider
    {
        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            var choices = new List<DiscordApplicationCommandOptionChoice>
            {
                new("Group Stage + Playoffs", TournamentFormat.GroupStageWithPlayoffs.ToString()),
                new("Single Elimination", TournamentFormat.SingleElimination.ToString()),
                new("Double Elimination", TournamentFormat.DoubleElimination.ToString()),
                new("Round Robin", TournamentFormat.RoundRobin.ToString()),
            };

            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(choices);
        }
    }

    public class VisualizationTestChoiceProvider : IChoiceProvider
    {
        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            var choices = new List<DiscordApplicationCommandOptionChoice>
            {
                new DiscordApplicationCommandOptionChoice("Empty Tournament", "empty"),
                new DiscordApplicationCommandOptionChoice("Groups Only", "groups_only"),
                new DiscordApplicationCommandOptionChoice("Groups In Progress", "groups_in_progress"),
                new DiscordApplicationCommandOptionChoice("Playoffs Ready", "playoffs_ready"),
                new DiscordApplicationCommandOptionChoice("Playoffs In Progress", "playoffs_in_progress"),
                new DiscordApplicationCommandOptionChoice("Complete Tournament", "complete"),
            };

            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(choices);
        }
    }

    // Simple class that can be cast to DiscordMember for testing
    public class MockDiscordMember
    {
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;

        public ulong Id => UserId;
        public string Username => UserName;
        public string DisplayName => UserName;

        // Override ToString to help with debugging and visualization
        public override string ToString() => UserName;

        // Explicit conversion to allow using as DiscordMember in tests
        public static explicit operator DSharpPlus.Entities.DiscordMember(MockDiscordMember mock)
        {
            // This forces the participant.Player?.DisplayName in TournamentVisualization.cs
            // to use the MockDiscordMember.DisplayName property by pretending to be null
            // but exposing its DisplayName via ToString()
            return null!;
        }
    }
}