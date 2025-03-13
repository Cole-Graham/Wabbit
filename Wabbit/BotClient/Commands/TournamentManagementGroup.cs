using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus;
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
using DSharpPlus.Net;
using System.Reflection;
using System.Dynamic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Commands
{
    [Command("tournament_manager")]
    [Description("Commands for managing tournaments")]
    public class TournamentManagementGroup
    {
        private const int autoDeleteSeconds = 30;

        // New services
        private readonly ITournamentManagerService _tournamentService;
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentManagementGroup> _logger;
        private readonly ITournamentRepositoryService _repositoryService;
        private readonly ITournamentSignupService _signupService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ITournamentMatchService _tournamentMatchService;

        public TournamentManagementGroup(
            OngoingRounds ongoingRounds,
            ILogger<TournamentManagementGroup> logger,
            ITournamentManagerService tournamentService,
            ITournamentRepositoryService repositoryService,
            ITournamentSignupService signupService,
            ITournamentStateService stateService,
            ITournamentGroupService groupService,
            ITournamentPlayoffService playoffService,
            ITournamentGameService tournamentGameService,
            ITournamentMatchService tournamentMatchService)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;

            // Initialize new services
            _tournamentService = tournamentService;
            _repositoryService = repositoryService;
            _signupService = signupService;
            _stateService = stateService;
            _groupService = groupService;
            _playoffService = playoffService;
            _tournamentGameService = tournamentGameService;
            _tournamentMatchService = tournamentMatchService;
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
                var signup = await _signupService.GetSignupWithParticipantsAsync(signupName, context.Client);
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

                // Create tournament with random groups or using seeding if available
                Tournament tournament;
                if (signup.Seeds != null && signup.Seeds.Any())
                {
                    tournament = await CreateTournamentWithSeeding(signup, context.Channel);
                }
                else
                {
                    // Create tournament with default settings
                    tournament = await _tournamentService.CreateTournamentAsync(
                        signup.Name,
                        signup.Participants,
                        signup.Format,
                        context.Channel,
                        signup.Type);
                }

                // Post the tournament standings visualization to the standings channel if configured
                await _tournamentService.PostTournamentVisualizationAsync(tournament, context.Client);

                // Create confirmation embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 Tournament Created: {tournament.Name}")
                    .WithDescription($"A new tournament has been created from signup '{signup.Name}'.")
                    .AddField("Players", tournament.Groups.Sum(g => g.Participants.Count).ToString(), true)
                    .AddField("Format", tournament.Format.ToString(), true)
                    .AddField("Game Type", tournament.GameType == GameType.OneVsOne ? "1v1" : "2v2", true)
                    .WithColor(DiscordColor.Green)
                    .WithFooter("Use /tournament_manager show_standings to view the current standings.");

                // Send confirmation
                await context.EditResponseAsync(embed);

                // Send a public message
                var publicMessage = await context.Channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"**Tournament Created**: {tournament.Name} has been created and will start shortly!")
                        .WithAllowedMentions(new IMention[] { new RoleMention() }));

                // Clean up signup data
                await _signupService.DeleteSignupAsync(signupName, context.Client, true);

                // Save tournament state
                await SaveTournamentStateAsync(context.Client);

                // Auto-delete both messages after a short time
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try
                    {
                        await publicMessage.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete messages: {ex.Message}");
                    }
                });
            }, "Failed to create tournament from signup");
        }

        // Modify the CreateTournamentWithSeeding method to use these new functions
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
            int groupCount = _groupService.DetermineGroupCount(playerCount, signup.Format);

            // Get optimal group sizes
            List<int> groupSizes = _groupService.GetOptimalGroupSizes(playerCount, groupCount);

            // Get advancement criteria
            var advancementCriteria = _playoffService.GetAdvancementCriteria(playerCount, groupCount);

            // Store advancement criteria in tournament's custom properties
            if (tournament.CustomProperties == null)
                tournament.CustomProperties = new Dictionary<string, object>();

            tournament.CustomProperties["GroupWinnersAdvance"] = advancementCriteria.groupWinners;
            tournament.CustomProperties["BestThirdPlaceAdvance"] = advancementCriteria.bestThirdPlace;

            // Create the groups with the determined sizes
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
            bool reverseDirection = false;
            int currentGroup = 0;

            foreach (var player in seededPlayers)
            {
                // Find the seed value for this player
                int seedValue = signup.Seeds
                    .FirstOrDefault(s => s.Player == player || (s.Player?.Id == player.Id))?.Seed ?? 0;

                tournament.Groups[currentGroup].Participants.Add(new Tournament.GroupParticipant
                {
                    Player = player,
                    Seed = seedValue  // Transfer the seed value from signup to tournament
                });

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

            // Distribute remaining unseeded players according to the optimal group sizes
            for (int i = 0; i < unseededPlayers.Count; i++)
            {
                // Find the group that needs more players (has fewer than its target size)
                var groupsToFill = tournament.Groups
                    .Select((g, index) => new { Group = g, TargetSize = groupSizes[index] })
                    .Where(g => g.Group.Participants.Count < g.TargetSize)
                    .OrderBy(g => g.Group.Participants.Count) // Fill smallest groups first for balance
                    .ToList();

                if (!groupsToFill.Any())
                    break; // All groups are filled to their target sizes

                var groupToFill = groupsToFill.First().Group;

                // Get any potential seed value (should be 0 in most cases, but checking just in case)
                int seedValue = signup.Seeds
                    .FirstOrDefault(s => s.Player == unseededPlayers[i] || (s.Player?.Id == unseededPlayers[i].Id))?.Seed ?? 0;

                groupToFill.Participants.Add(new Tournament.GroupParticipant
                {
                    Player = unseededPlayers[i],
                    Seed = seedValue  // Transfer any seed value that might exist
                });
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
                            Name = $"{_groupService.GetPlayerDisplayName(group.Participants[i].Player)} vs {_groupService.GetPlayerDisplayName(group.Participants[j].Player)}",
                            Type = TournamentMatchType.GroupStage,
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

            // Save tournament state
            await _repositoryService.SaveTournamentsAsync();

            // Log creation details for debugging
            var participantCounts = string.Join(", ", tournament.Groups.Select(g => g.Participants.Count));
            _logger.LogInformation($"Created tournament with {groupCount} groups. Group sizes: {participantCounts}");
            _logger.LogInformation($"Advancement criteria: Top {advancementCriteria.groupWinners} from each group + {advancementCriteria.bestThirdPlace} best third-place players");

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
                string imagePath = await TournamentVisualization.GenerateStandingsImage(tournament, context.Client, _stateService);

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

            await SafeExecute(context, async () =>
            {
                // Get all tournaments and signups
                var activeTournaments = _tournamentService.GetAllTournaments() ?? new List<Tournament>();
                var signups = _signupService.GetAllSignups() ?? new List<TournamentSignup>();

                // Create embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("🏆 Tournaments & Signups")
                    .WithColor(DiscordColor.Gold);

                // Active Tournaments
                StringBuilder activeTournamentsText = new StringBuilder();
                if (activeTournaments.Count > 0)
                {
                    foreach (var tournament in activeTournaments.OrderBy(t => t.IsComplete).ThenBy(t => t.Name))
                    {
                        string status = "";
                        if (tournament.IsComplete)
                        {
                            status = "✅ Complete";
                        }
                        else if (tournament.CurrentStage == TournamentStage.Groups)
                        {
                            bool allGroupsComplete = tournament.Groups.All(g => g.IsComplete);
                            status = allGroupsComplete ? "⏳ Group Stage Complete" : "🏁 Group Stage";
                        }
                        else if (tournament.CurrentStage == TournamentStage.Playoffs)
                        {
                            status = "🥇 Playoffs";
                        }

                        int playerCount = tournament.Groups.Sum(g => g.Participants.Count);
                        activeTournamentsText.AppendLine($"**{tournament.Name}** - {status} - {playerCount} players");
                    }
                }
                else
                {
                    activeTournamentsText.AppendLine("*No active tournaments*");
                }

                // Open Signups
                StringBuilder openSignupsText = new StringBuilder();
                var openSignups = signups.Where(s => s.IsOpen).ToList();
                if (openSignups.Count > 0)
                {
                    foreach (var signup in openSignups.OrderBy(s => s.Name))
                    {
                        int participantCount = _signupService.GetParticipantCount(signup);
                        openSignupsText.AppendLine($"**{signup.Name}** - {participantCount} participants");
                    }
                }
                else
                {
                    openSignupsText.AppendLine("*No open signups*");
                }

                // Closed Signups
                StringBuilder closedSignupsText = new StringBuilder();
                var closedSignups = signups.Where(s => !s.IsOpen).ToList();
                if (closedSignups.Count > 0)
                {
                    foreach (var signup in closedSignups.OrderBy(s => s.Name))
                    {
                        int participantCount = _signupService.GetParticipantCount(signup);
                        closedSignupsText.AppendLine($"**{signup.Name}** - {participantCount} participants - ⏸️ Closed");
                    }
                }
                else
                {
                    closedSignupsText.AppendLine("*No closed signups*");
                }

                // Add fields to embed
                embed.AddField("Active Tournaments", activeTournamentsText.ToString(), false);
                embed.AddField("Open Signups", openSignupsText.ToString(), false);
                embed.AddField("Closed Signups", closedSignupsText.ToString(), false);

                // Send response
                await context.EditResponseAsync(embed);
            }, "Failed to list tournaments and signups");
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
                if (_signupService.GetAllSignups().Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
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

                // Create the signup using the new SignupService
                var signup = _signupService.CreateSignup(
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
                    _signupService.UpdateSignup(signup);

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
                    Console.WriteLine($"Error sending signup message: {ex.Message}\n{ex.StackTrace}");
                    await SafeResponse(context, $"Tournament signup '{name}' was created but there was an error creating the signup message: {ex.Message}", null, true, 10);
                }
            }, "Failed to create tournament signup");
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
                Console.WriteLine($"Adding player to signup '{tournamentName}': {player.Username} (ID: {player.Id})");

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

                Console.WriteLine($"Successfully removed {player.DisplayName} (ID: {player.Id}) from signup '{tournamentName}'");
                Console.WriteLine($"Signup now has {signup.Participants.Count} participants");

                // Save the updated signup
                _signupService.UpdateSignup(signup);

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
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Try to find it as a tournament
                var tournament = _tournamentService.GetTournament(name);
                if (tournament != null)
                {
                    // Delete the tournament
                    await _tournamentService.DeleteTournamentAsync(name, context.Client);
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Tournament '{name}' has been deleted."));
                    return;
                }

                // Try to find it as a signup
                var signup = _signupService.GetSignup(name);
                if (signup != null)
                {
                    // Delete the signup
                    await _signupService.DeleteSignupAsync(name, context.Client);
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Signup '{name}' has been deleted."));
                    return;
                }

                // If we get here, nothing was found
                var tournaments = _tournamentService.GetAllTournaments();
                var signups = _signupService.GetAllSignups();

                string availableOptions = "";
                if (tournaments.Count > 0)
                {
                    availableOptions += "**Available Tournaments:**\n" + string.Join("\n", tournaments.Select(t => t.Name)) + "\n\n";
                }
                if (signups.Count > 0)
                {
                    availableOptions += "**Available Signups:**\n" + string.Join("\n", signups.Select(s => s.Name));
                }

                if (string.IsNullOrEmpty(availableOptions))
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("No tournaments or signups found."));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"No tournament or signup found with name '{name}'.\n\n{availableOptions}"));
                }
            }, "Failed to delete tournament or signup");
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
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await context.EditResponseAsync($"Tournament '{tournamentName}' not found.");
                    return;
                }

                // Find active rounds for this tournament
                var activeRounds = _stateService.GetActiveRoundsForTournament(tournament.Name);

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
                        string status = round.IsCompleted ? "Completed" : "In Progress";
                        roundsInfo += $"• Round {round.Id}: {status}, Map {round.MapNum + 1}/{round.BestOf}\n";
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
                    await context.EditResponseAsync("You need administrator permission to repair tournament data.");
                    return;
                }

                await context.EditResponseAsync("Repairing tournament data files...");

                await _repositoryService.RepairDataFilesAsync(context.Client);

                await context.EditResponseAsync("Tournament data files have been repaired.");
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

                // Remove any existing seed for this player
                signup.Seeds.RemoveAll(s => s.Player?.Id == player.Id || s.PlayerId == player.Id);

                // If seed is not 0, add the new seed
                if (seed > 0)
                {
                    var participantSeed = new ParticipantSeed();
                    participantSeed.SetPlayer(player);
                    participantSeed.Seed = seed;
                    signup.Seeds.Add(participantSeed);
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
                await _stateService.SaveTournamentStateAsync(context.Client);
            }, "Failed to set tournament seed");
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
                StringBuilder participantsText = new StringBuilder();
                int totalParticipants = sortedParticipants.Count;

                // Calculate the number of rows needed
                int rowsNeeded = (int)Math.Ceiling(totalParticipants / 2.0);

                for (int i = 0; i < rowsNeeded; i++)
                {
                    // Left column
                    int leftIndex = i * 2;
                    if (leftIndex < totalParticipants)
                    {
                        var leftPlayer = sortedParticipants[leftIndex];
                        string leftSeedDisplay = leftPlayer.Seed > 0 ? $"[Seed #{leftPlayer.Seed}]" : "";
                        participantsText.Append($"{leftIndex + 1}. <@{leftPlayer.Player.Id}> {leftSeedDisplay}");

                        // Add padding between columns
                        participantsText.Append("    ");

                        // Right column
                        int rightIndex = leftIndex + 1;
                        if (rightIndex < totalParticipants)
                        {
                            var rightPlayer = sortedParticipants[rightIndex];
                            string rightSeedDisplay = rightPlayer.Seed > 0 ? $"[Seed #{rightPlayer.Seed}]" : "";
                            participantsText.Append($"{rightIndex + 1}. <@{rightPlayer.Player.Id}> {rightSeedDisplay}");
                        }

                        participantsText.AppendLine();
                    }
                }

                string finalText = participantsText.ToString();

                // If the text is too long, truncate it
                if (finalText.Length > 1024)
                {
                    finalText = finalText.Substring(0, 1020) + "...";
                }

                embedBuilder.AddField($"Participants ({sortedParticipants.Count})", finalText);
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
                await _signupService.LoadParticipantsAsync(signup, (DSharpPlus.DiscordClient)client);

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
            _logger.LogInformation($"Starting group stage for tournament {tournament.Name}");
            // First check that we have at least one group
            if (tournament.Groups == null || !tournament.Groups.Any())
            {
                _logger.LogError("Tournament has no groups, cannot start matches");
                return;
            }

            _logger.LogInformation($"Tournament has {tournament.Groups.Count} groups");

            // Track players who have already been scheduled for a match
            HashSet<ulong> playersWithMatches = new HashSet<ulong>();

            // Process each group
            foreach (var group in tournament.Groups)
            {
                _logger.LogInformation($"Processing group {group.Name}");
                // Convert GroupParticipants to DiscordMembers
                var participants = new List<DiscordMember>();
                foreach (var participant in group.Participants)
                {
                    if (participant.Player is DiscordMember member)
                    {
                        participants.Add(member);
                    }
                    else if (participant.Player is not null)
                    {
                        // Try to use reflection to get the ID
                        _logger.LogWarning($"Using reflection to get the player ID from {participant.Player.GetType().Name}");
                        try
                        {
                            var property = participant.Player.GetType().GetProperty("Id");
                            if (property != null)
                            {
                                var value = property.GetValue(participant.Player);
                                if (value is ulong playerId)
                                {
                                    // Try to get the member by ID from any of the guilds the bot is in
                                    var guilds = client.Guilds.Values;
                                    foreach (var guild in guilds)
                                    {
                                        try
                                        {
                                            var discordMember = await guild.GetMemberAsync(playerId);
                                            if (discordMember is not null)
                                            {
                                                participants.Add(discordMember);
                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, $"Error getting member with ID {playerId} from guild {guild.Id}");
                                        }
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning($"Player.Id is not ulong, it's {value?.GetType().Name ?? "null"}");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Player type {participant.Player.GetType().Name} doesn't have Id property");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error using reflection to get player ID");
                        }

                        // If reflection fails, try string parsing (last resort)
                        if (participant.Player is not DiscordMember && participant.Player is not null)
                        {
                            _logger.LogWarning($"Trying to parse string for player ID");
                            try
                            {
                                // Try to get the ID from the string representation
                                var playerString = participant.Player.ToString();
                                if (!string.IsNullOrEmpty(playerString))
                                {
                                    var idMatch = System.Text.RegularExpressions.Regex.Match(playerString, @"Id\s*=\s*(\d+)");
                                    if (idMatch.Success && ulong.TryParse(idMatch.Groups[1].Value, out ulong playerId))
                                    {
                                        // Try to get the member by ID from any of the guilds the bot is in
                                        var guilds = client.Guilds.Values;
                                        foreach (var guild in guilds)
                                        {
                                            try
                                            {
                                                var discordMember = await guild.GetMemberAsync(playerId);
                                                if (discordMember is not null)
                                                {
                                                    participants.Add(discordMember);
                                                    break;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, $"Error getting member with ID {playerId} from guild {guild.Id}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Could not extract ID from player string: {playerString}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error parsing player string for ID");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError($"Participant has no player assigned");
                    }
                }

                if (participants.Count < 2)
                {
                    _logger.LogWarning($"Group {group.Name} has fewer than 2 participants, skipping match creation");
                    continue;
                }

                // Create matches between all participants in the group
                for (int i = 0; i < participants.Count; i++)
                {
                    for (int j = i + 1; j < participants.Count; j++)
                    {
                        var player1 = participants[i];
                        var player2 = participants[j];

                        // Check if both players already have matches
                        if (playersWithMatches.Contains(player1.Id) && playersWithMatches.Contains(player2.Id))
                        {
                            _logger.LogInformation($"Both players {player1.Username} and {player2.Username} already have matches, skipping");
                            continue;
                        }

                        // Check if a match already exists between these players
                        bool matchExists = group.Matches.Any(m =>
                            m.Participants.Count == 2 &&
                            ((m.Participants[0].Player is DiscordMember p1 && p1.Id == player1.Id &&
                              m.Participants[1].Player is DiscordMember p2 && p2.Id == player2.Id) ||
                             (m.Participants[0].Player is DiscordMember p3 && p3.Id == player2.Id &&
                              m.Participants[1].Player is DiscordMember p4 && p4.Id == player1.Id)));

                        if (matchExists)
                        {
                            _logger.LogInformation($"Match already exists between {player1.Username} and {player2.Username}, skipping");
                            continue;
                        }

                        // Create a new match for these players
                        _logger.LogInformation($"Creating new match between {player1.Username} and {player2.Username}");
                        Tournament.Match? existingMatch = null;

                        // Use the tournament match service to create the match
                        await _tournamentMatchService.CreateAndStart1v1Match(tournament, group, player1, player2, client, 3, existingMatch);

                        // Mark players as having matches
                        playersWithMatches.Add(player1.Id);
                        playersWithMatches.Add(player2.Id);

                        // We've created one match for these players, so we'll break out of this loop
                        // and move on to the next player
                        break;
                    }
                }
            }

            // Save the tournament state
            await _tournamentService.SaveAllDataAsync();
        }

        // Method to handle match completion (delegate to service)
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            await _tournamentMatchService.HandleMatchCompletion(tournament, match, client);
        }

        // Method to handle game result selection and advance the match series
        public async Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client)
        {
            await _tournamentGameService.HandleGameResultAsync(round, thread, winnerId, client);
        }

        [Command("save")]
        [Description("Save tournament data")]
        public async Task SaveTournamentData(CommandContext context)
        {
            await context.DeferResponseAsync();

            await SafeExecute(context, async () =>
            {
                // Save all tournament data
                await _tournamentService.SaveAllDataAsync();

                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Tournament Data Saved")
                    .WithDescription("All tournament data has been saved successfully.")
                    .WithColor(DiscordColor.Green);

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            });
        }

        /// <summary>
        /// Internal helper method to save the tournament state
        /// </summary>
        private async Task SaveTournamentStateAsync(BaseDiscordClient client)
        {
            // Save the tournament state using the appropriate client type
            if (client is DiscordClient discordClient)
            {
                await _stateService.SaveTournamentStateAsync(discordClient);
            }
            else
            {
                // If the client is not a DiscordClient, pass null
                await _stateService.SaveTournamentStateAsync(null);
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