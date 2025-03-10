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
            await SafeExecute(context, async () =>
            {
                // Log the tournaments in the collection for debugging
                Console.WriteLine($"DEBUG: Found {_ongoingRounds.Tournaments.Count} tournaments in _ongoingRounds.Tournaments");
                foreach (var t in _ongoingRounds.Tournaments)
                {
                    Console.WriteLine($"DEBUG: Tournament in list: Name={t.Name}, ID={(t.GetHashCode())}, Type={t.GetType().Name}");
                }

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
            }, "Failed to list tournaments");
        }

        [Command("signup_create")]
        [Description("Create a new tournament signup")]
        public async Task CreateSignup(
            CommandContext context,
            [Description("Tournament name")] string name,
            [Description("Tournament format")] string format,
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

                // Create the signup
                TournamentSignup signup = new()
                {
                    Name = name,
                    Format = Enum.Parse<TournamentFormat>(format),
                    CreatedAt = DateTime.Now,
                    CreatedBy = context.User,
                    SignupChannelId = signupChannelId.Value,
                    ScheduledStartTime = startTimeUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(startTimeUnix).DateTime : null,
                    IsOpen = true
                };

                _ongoingRounds.TournamentSignups.Add(signup);

                try
                {
                    // Create and send the signup message
                    var signupChannel = await context.Client.GetChannelAsync(signupChannelId.Value);
                    DiscordEmbed embed = CreateSignupEmbed(signup);

                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(embed)
                        .AddComponents(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_{name.Replace(" ", "_")}",
                            "Sign Up"
                        ));

                    var message = await signupChannel.SendMessageAsync(builder);
                    signup.MessageId = message.Id;

                    await context.EditResponseAsync($"Tournament signup '{name}' created successfully. Players can now sign up.");
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

            // Find the signup
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

            if (signup == null)
            {
                await context.EditResponseAsync($"Signup '{tournamentName}' not found.");
                return;
            }

            // Check if the player is in the signup
            var participant = signup.Participants.FirstOrDefault(p => p.Id == player.Id);
            if (participant is null)
            {
                await context.EditResponseAsync($"{player.DisplayName} is not signed up for tournament '{tournamentName}'.");
                return;
            }

            // Remove the player from the signup
            signup.Participants.Remove(participant);

            // Update the signup message
            await UpdateSignupMessage(signup, context.Client);

            await context.EditResponseAsync($"{player.DisplayName} has been removed from the tournament '{tournamentName}'.");
        }

        [Command("delete")]
        [Description("Delete a tournament or signup")]
        public async Task DeleteTournament(
            CommandContext context,
            [Description("Tournament/signup name")] string name)
        {
            await SafeExecute(context, async () =>
            {
                bool deleted = false;

                // Debug info
                Console.WriteLine($"DEBUG: Attempting to delete tournament/signup: '{name}'");
                Console.WriteLine($"DEBUG: _ongoingRounds.Tournaments.Count = {_ongoingRounds.Tournaments.Count}");
                foreach (var t in _ongoingRounds.Tournaments)
                {
                    Console.WriteLine($"DEBUG: Tournament available: Name={t.Name}, ID={t.GetHashCode()}, Type={t.GetType().Name}");
                }

                List<string> availableTournaments = _ongoingRounds.Tournaments.Select(t => t.Name).ToList();
                List<string> availableSignups = _ongoingRounds.TournamentSignups.Select(s => s.Name).ToList();

                // Try exact match first
                Console.WriteLine($"DEBUG: Trying to find tournament with _tournamentManager.GetTournament('{name}')");
                var tournament = _tournamentManager.GetTournament(name);
                if (tournament != null)
                {
                    Console.WriteLine($"DEBUG: Found tournament via GetTournament: {tournament.Name}");
                }

                // Try direct collection search if manager method fails
                if (tournament == null)
                {
                    Console.WriteLine($"DEBUG: Trying to find tournament with direct collection search");
                    tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                        t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (tournament != null)
                    {
                        Console.WriteLine($"DEBUG: Found tournament via direct collection: {tournament.Name}");
                    }
                }

                // Try partial match if direct search fails
                if (tournament == null)
                {
                    Console.WriteLine($"DEBUG: Trying to find tournament with partial name match");
                    tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                        t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

                    if (tournament != null)
                    {
                        Console.WriteLine($"DEBUG: Found tournament via partial match: {tournament.Name}");
                    }
                }

                if (tournament != null)
                {
                    // Remove from list - important: remove the exact instance, not by name
                    int countBefore = _ongoingRounds.Tournaments.Count;
                    Console.WriteLine($"DEBUG: Removing tournament '{tournament.Name}' (ID={tournament.GetHashCode()})");

                    // Use RemoveAll with reference equality
                    _ongoingRounds.Tournaments.RemoveAll(t => ReferenceEquals(t, tournament));

                    int countAfter = _ongoingRounds.Tournaments.Count;
                    Console.WriteLine($"DEBUG: Tournament count before: {countBefore}, after: {countAfter}");

                    if (countBefore == countAfter)
                    {
                        // If reference equality didn't work, try removing by name
                        Console.WriteLine($"DEBUG: Failed to remove by reference, trying by name");
                        int removeByName = _ongoingRounds.Tournaments.RemoveAll(t =>
                            t.Name.Equals(tournament.Name, StringComparison.OrdinalIgnoreCase));
                        Console.WriteLine($"DEBUG: Removed {removeByName} tournaments by name");
                    }

                    await context.EditResponseAsync($"Tournament '{tournament.Name}' has been deleted.");
                    deleted = true;
                }
                else
                {
                    Console.WriteLine($"DEBUG: No tournament found with name '{name}'");
                }

                // Check for signups if no tournament was found or deleted
                if (!deleted)
                {
                    // Try to find a signup with the given name
                    var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                        s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    // Try partial match if exact match fails
                    if (signup == null)
                    {
                        signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                            s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                    }

                    if (signup != null)
                    {
                        // Remove from list
                        _ongoingRounds.TournamentSignups.Remove(signup);
                        await context.EditResponseAsync($"Tournament signup '{signup.Name}' has been deleted.");
                        deleted = true;
                    }
                }

                if (!deleted)
                {
                    // If everything failed, try a brute force approach for tournaments
                    Console.WriteLine($"DEBUG: Trying brute force approach to clear test tournaments");
                    int removedCount = _ongoingRounds.Tournaments.RemoveAll(t =>
                        t.Name.StartsWith("TestFlow_", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.StartsWith("Test", StringComparison.OrdinalIgnoreCase));

                    if (removedCount > 0)
                    {
                        await context.EditResponseAsync($"Removed {removedCount} test tournaments by pattern matching.");
                        return;
                    }

                    // If still not found, show debug info
                    string debug = $"No tournament or signup found with name '{name}'\n\nAvailable tournaments ({availableTournaments.Count}): {string.Join(", ", availableTournaments)}\n\nAvailable signups ({availableSignups.Count}): {string.Join(", ", availableSignups)}";
                    await context.EditResponseAsync(debug);
                }
            }, "Failed to delete tournament/signup");
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
                // Find the signup (search case-insensitive)
                var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                    s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

                if (signup == null)
                {
                    // Try to find by partial name match if exact match fails
                    signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (signup == null)
                    {
                        await context.EditResponseAsync($"Tournament signup '{tournamentName}' not found. Available signups: {string.Join(", ", _ongoingRounds.TournamentSignups.Select(s => s.Name))}");
                        return;
                    }
                }

                // Reopen the signup
                signup.IsOpen = true;

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                string durationMessage = durationMinutes > 0
                    ? $" It will remain open for {durationMinutes} minutes."
                    : " It will remain open indefinitely.";

                await context.EditResponseAsync($"Tournament signup '{signup.Name}' has been reopened.{durationMessage}");

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
                            await UpdateSignupMessage(signup, context.Client);
                            await context.Channel.SendMessageAsync($"Tournament signup '{signup.Name}' has been automatically closed after {durationMinutes} minutes.");
                        }
                    });
                }
            }, "Failed to reopen signup");
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
                .WithTitle($"Tournament Signup: {signup.Name}")
                .WithDescription("Sign up for this tournament by clicking the button below.")
                .WithColor(new DiscordColor(75, 181, 67))
                .AddField("Format", signup.Format.ToString(), true)
                .AddField("Status", signup.IsOpen ? "Open" : "Closed", true)
                .AddField("Created By", signup.CreatedBy?.Username ?? "Unknown", true);

            if (signup.ScheduledStartTime.HasValue)
            {
                // Convert DateTime to Unix timestamp
                long unixTimestamp = ((DateTimeOffset)signup.ScheduledStartTime.Value).ToUnixTimeSeconds();
                string formattedTime = $"<t:{unixTimestamp}:F>";
                builder.AddField("Scheduled Start", formattedTime, false);
            }

            if (signup.Participants.Count > 0)
            {
                string participants = string.Join("\n", signup.Participants.Select(p => p.Username));
                builder.AddField($"Participants ({signup.Participants.Count})", participants, false);
            }
            else
            {
                builder.AddField("Participants (0)", "No participants yet", false);
            }

            builder.WithFooter($"Created at {signup.CreatedAt:yyyy-MM-dd HH:mm:ss}");

            return builder.Build();
        }

        private async Task UpdateSignupMessage(TournamentSignup signup, DSharpPlus.DiscordClient client)
        {
            if (signup.SignupChannelId == 0 || signup.MessageId == 0) return;

            try
            {
                var channel = await client.GetChannelAsync(signup.SignupChannelId);
                var message = await channel.GetMessageAsync(signup.MessageId);

                DiscordEmbed embed = CreateSignupEmbed(signup);

                // If the signup is still open, include the button
                if (signup.IsOpen)
                {
                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(embed)
                        .AddComponents(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_{signup.Name.Replace(" ", "_")}",
                            "Sign Up"
                        ));

                    await message.ModifyAsync(builder);
                }
                else
                {
                    var builder = new DiscordMessageBuilder().AddEmbed(embed);
                    await message.ModifyAsync(builder);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating signup message: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task SafeResponse(CommandContext context, string message, Action? action = null)
        {
            try
            {
                await context.EditResponseAsync(message);
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SafeResponse: {ex.Message}");
                try
                {
                    await context.Channel.SendMessageAsync(message);
                    action?.Invoke();
                }
                catch
                {
                    Console.WriteLine($"Failed to send message to channel as fallback: {message}");
                }
            }
        }

        private async Task SafeExecute(CommandContext context, Func<Task> action, string errorPrefix = "Command failed")
        {
            try
            {
                // Call DeferResponseAsync early to avoid timeouts
                await context.DeferResponseAsync();
                await action();
            }
            catch (DSharpPlus.Exceptions.NotFoundException ex)
            {
                Console.WriteLine($"Discord API timeout: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    // Try to send a message to the channel directly
                    await context.Channel.SendMessageAsync($"{errorPrefix}: The interaction timed out, but the command may have succeeded. Please check if the requested changes were applied.");
                }
                catch (Exception secondEx)
                {
                    Console.WriteLine($"Failed to send fallback message: {secondEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command error: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await SafeResponse(context, $"{errorPrefix}: {ex.Message}");
                }
                catch
                {
                    try
                    {
                        await context.Channel.SendMessageAsync($"{errorPrefix}: {ex.Message}");
                    }
                    catch
                    {
                        Console.WriteLine("Failed to send any error messages to the user");
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