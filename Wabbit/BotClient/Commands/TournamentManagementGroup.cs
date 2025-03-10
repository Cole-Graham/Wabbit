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
            await SafeExecute(context, async () =>
            {
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
                // Find the signup
                var signup = _tournamentManager.GetSignup(signupName);

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _tournamentManager.GetAllSignups();
                    signup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(signupName, StringComparison.OrdinalIgnoreCase));

                    if (signup == null)
                    {
                        await context.EditResponseAsync($"Signup '{signupName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}");
                        return;
                    }
                }

                // Check if a tournament with this name already exists
                if (_tournamentManager.GetTournament(signup.Name) != null)
                {
                    await context.EditResponseAsync($"A tournament with the name '{signup.Name}' already exists. Please delete it first or use a different name for the signup.");
                    return;
                }

                // Get the players who have signed up
                var players = signup.Participants;

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
            try
            {
                // Don't defer the response, that's what's causing issues
                // Instead, we'll send a direct message to the channel

                // Log the tournaments in the collection for debugging
                Console.WriteLine($"LIST DEBUG: Attempting to list tournaments");
                Console.WriteLine($"LIST DEBUG: _ongoingRounds.Tournaments is {(_ongoingRounds.Tournaments == null ? "NULL" : "NOT NULL")}");
                Console.WriteLine($"LIST DEBUG: Found {_ongoingRounds.Tournaments?.Count ?? 0} tournaments in _ongoingRounds.Tournaments");
                Console.WriteLine($"LIST DEBUG: _tournamentManager GetAllTournaments() returns {_tournamentManager.GetAllTournaments()?.Count ?? 0} tournaments");

                if (_ongoingRounds.Tournaments != null)
                {
                    foreach (var t in _ongoingRounds.Tournaments)
                    {
                        Console.WriteLine($"LIST DEBUG: Tournament in _ongoingRounds.Tournaments: Name={t.Name}, ID={(t.GetHashCode())}, Type={t.GetType().Name}");
                    }
                }

                // Also check what the tournament manager knows
                var managerTournaments = _tournamentManager.GetAllTournaments();
                if (managerTournaments != null)
                {
                    foreach (var t in managerTournaments)
                    {
                        Console.WriteLine($"LIST DEBUG: Tournament in _tournamentManager.GetAllTournaments(): Name={t.Name}, ID={(t.GetHashCode())}, Type={t.GetType().Name}");
                    }
                }

                // Try to use the manager's list instead of direct access
                var tournamentsToShow = _tournamentManager.GetAllTournaments();

                if (tournamentsToShow == null || !tournamentsToShow.Any())
                {
                    // Send a direct message instead of using interaction response
                    await context.Channel.SendMessageAsync("No active tournaments.");
                    return;
                }

                var embed = new DiscordEmbedBuilder
                {
                    Title = "Active Tournaments",
                    Description = "List of all active tournaments",
                    Color = DiscordColor.Blurple
                };

                foreach (var tournament in tournamentsToShow)
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

                // Send as a direct message to avoid interaction timing issues
                var messageBuilder = new DiscordMessageBuilder().AddEmbed(embed);
                await context.Channel.SendMessageAsync(messageBuilder);

                // Try to acknowledge the interaction to avoid the "This interaction failed" message
                try
                {
                    // Just send an empty deferred response to satisfy Discord
                    await context.DeferResponseAsync();
                }
                catch (Exception ex)
                {
                    // Ignore any errors from the defer - we already sent our content
                    Console.WriteLine($"Non-critical error acknowledging list command: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ListTournaments: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await context.Channel.SendMessageAsync($"Error listing tournaments: {ex.Message}");
                }
                catch
                {
                    Console.WriteLine("Failed to send error message");
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
                DateTime? scheduledStartTime = startTimeUnix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(startTimeUnix).DateTime
                    : null;

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
                                $"signup_{name.Replace(" ", "_")}",
                                "Sign Up"
                            ),
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Danger,
                                $"withdraw_{name.Replace(" ", "_")}",
                                "Withdraw"
                            )
                        );

                    var message = await signupChannel.SendMessageAsync(builder);
                    signup.MessageId = message.Id;

                    // Save updated MessageId
                    _tournamentManager.UpdateSignup(signup);

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
            await SafeExecute(context, async () =>
            {
                // Find the signup
                var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                    s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

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
                signup.Participants.Add((DiscordMember)context.User);

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
                // Find the signup
                var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                    s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

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
                signup.Participants.Remove(participant);

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
                // Find the signup
                var signup = _tournamentManager.GetSignup(tournamentName);

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _tournamentManager.GetAllSignups();
                    signup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (signup == null)
                    {
                        await context.EditResponseAsync($"Signup '{tournamentName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}");
                        return;
                    }
                }

                if (!signup.IsOpen)
                {
                    await context.EditResponseAsync($"Signup '{signup.Name}' is already closed.");
                    return;
                }

                // Close the signup
                signup.IsOpen = false;
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                await context.EditResponseAsync($"Signup '{signup.Name}' has been closed. Use '/tournament_manager create_from_signup' to create the tournament.");
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
                // Find the signup
                var signup = _tournamentManager.GetSignup(tournamentName);

                if (signup == null)
                {
                    // Try partial match
                    var allSignups = _tournamentManager.GetAllSignups();
                    signup = allSignups.FirstOrDefault(s =>
                        s.Name.Contains(tournamentName, StringComparison.OrdinalIgnoreCase));

                    if (signup == null)
                    {
                        await context.EditResponseAsync($"Signup '{tournamentName}' not found. Available signups: {string.Join(", ", allSignups.Select(s => s.Name))}");
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
                // Check admin permissions
                if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageMessages))
                {
                    await context.EditResponseAsync("You don't have permission to add players to signups.");
                    return;
                }

                // Find the signup
                var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                    s.Name.Equals(tournamentName, StringComparison.OrdinalIgnoreCase));

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
                    await context.EditResponseAsync($"{player.DisplayName} is already signed up for tournament '{tournamentName}'.");
                    return;
                }

                // Add the player
                signup.Participants.Add(player);

                // Update the signup message
                await UpdateSignupMessage(signup, context.Client);

                await context.EditResponseAsync($"{player.DisplayName} has been added to the tournament '{tournamentName}'.");
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
                bool deleted = false;

                // Try to delete as tournament first
                var tournament = _tournamentManager.GetTournament(name);
                if (tournament != null)
                {
                    _tournamentManager.DeleteTournament(name);
                    await context.EditResponseAsync($"Tournament '{name}' has been deleted.");
                    deleted = true;
                }

                // If not found as tournament, try as signup
                if (!deleted)
                {
                    var signup = _tournamentManager.GetSignup(name);
                    if (signup != null)
                    {
                        _tournamentManager.DeleteSignup(name);
                        await context.EditResponseAsync($"Tournament signup '{name}' has been deleted.");
                        deleted = true;
                    }
                }

                if (!deleted)
                {
                    // If neither found, try partial name matches
                    var allSignups = _tournamentManager.GetAllSignups().Select(s => s.Name).ToList();
                    var allTournaments = _tournamentManager.GetAllTournaments().Select(t => t.Name).ToList();

                    string debug = $"No tournament or signup found with name '{name}'\n\n" +
                        $"Available tournaments ({allTournaments.Count}): {string.Join(", ", allTournaments)}\n\n" +
                        $"Available signups ({allSignups.Count}): {string.Join(", ", allSignups)}\n\n" +
                        $"Please try using the exact name from the list above.";
                    await context.EditResponseAsync(debug);
                }
            }, "Failed to delete tournament/signup");
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

        private async Task UpdateSignupMessage(TournamentSignup signup, DiscordClient client)
        {
            try
            {
                if (signup.MessageId == 0 || signup.SignupChannelId == 0)
                {
                    Console.WriteLine($"Cannot update signup message for '{signup.Name}' - missing message ID or channel ID");
                    return;
                }

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

                // Create embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Tournament Signup: {signup.Name}")
                    .WithDescription($"Format: {signup.Format}")
                    .WithColor(signup.IsOpen ? DiscordColor.Green : DiscordColor.Red)
                    .AddField("Status", signup.IsOpen ? "OPEN" : "CLOSED", true)
                    .AddField("Creator", signup.CreatedBy?.Username ?? "Unknown", true);

                if (signup.ScheduledStartTime.HasValue)
                {
                    embed.AddField("Scheduled Start", $"<t:{((DateTimeOffset)signup.ScheduledStartTime.Value).ToUnixTimeSeconds()}:F>", true);
                }

                // Add participants field
                string participantsText = signup.Participants.Count > 0
                    ? string.Join("\n", signup.Participants.Select((user, index) => $"{index + 1}. {user.Username}"))
                    : "No players signed up yet";
                embed.AddField($"Participants ({signup.Participants.Count})", participantsText);

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

                // Wait a longer time to give Discord more time to process the defer
                await Task.Delay(1000);  // Increased from 500ms

                // Now execute the action
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
            catch (DSharpPlus.Exceptions.BadRequestException ex)
            {
                Console.WriteLine($"Discord API bad request: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    // Try to send a message to the channel directly
                    await context.Channel.SendMessageAsync($"{errorPrefix}: Discord rejected the request. This often happens when a command times out. Your command might still have worked.");
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
                    // Try to send a message directly to the channel first - most reliable
                    await context.Channel.SendMessageAsync($"{errorPrefix}: {ex.Message}");
                }
                catch (Exception directEx)
                {
                    Console.WriteLine($"Failed to send direct channel message: {directEx.Message}");
                    try
                    {
                        await SafeResponse(context, $"{errorPrefix}: {ex.Message}");
                    }
                    catch (Exception responseEx)
                    {
                        Console.WriteLine($"Failed to send safe response: {responseEx.Message}");
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