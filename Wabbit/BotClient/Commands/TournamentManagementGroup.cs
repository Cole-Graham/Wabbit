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
using MatchType = Wabbit.Models.MatchType;
using System.Text;

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

                // Create tournament with random groups or using seeding if available
                Tournament tournament;
                if (signup.Seeds != null && signup.Seeds.Any())
                {
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

                // Post the tournament standings visualization to the standings channel if configured
                await _tournamentManager.PostTournamentVisualization(tournament, context.Client);

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

                // Preserve signup data instead of deleting it
                await _tournamentManager.DeleteSignup(signupName, context.Client, true);

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

        // Replace the existing DetermineGroupCount method with a new implementation
        private int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // For non-group formats, return 1
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
                // Return group count according to the GroupStageFormat specifications
                return playerCount switch
                {
                    < 7 => 1,    // Small tournaments: 1 group
                    7 => 1,      // 1 group of 7
                    8 => 2,      // 2 groups of 4
                    9 => 3,      // 3 groups of 3
                    10 => 2,     // 2 groups of 5
                    11 => 3,     // 3 groups (4, 4, 3)
                    12 => 3,     // 3 groups of 4
                    13 => 3,     // 3 groups (4, 4, 5)
                    14 => 2,     // 2 groups of 7
                    15 => 3,     // 3 groups of 5
                    16 => 4,     // 4 groups of 4
                    17 => 3,     // 3 groups (6, 6, 5)
                    18 => 3,     // 3 groups of 6
                    _ => 4       // Large tournaments: use 4 groups
                };
            }
        }

        // Add new method to get the optimal group sizes for a tournament
        private List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
        {
            // Given the player count and group count, determine the optimal size for each group
            List<int> groupSizes = new List<int>();

            switch (playerCount)
            {
                case 7:
                    groupSizes.Add(7); // 1 group of 7
                    break;
                case 8:
                    groupSizes.Add(4); groupSizes.Add(4); // 2 groups of 4
                    break;
                case 9:
                    groupSizes.Add(3); groupSizes.Add(3); groupSizes.Add(3); // 3 groups of 3
                    break;
                case 10:
                    groupSizes.Add(5); groupSizes.Add(5); // 2 groups of 5
                    break;
                case 11:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(3); // 3 groups (4, 4, 3)
                    break;
                case 12:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); // 3 groups of 4
                    break;
                case 13:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(5); // 3 groups (4, 4, 5)
                    break;
                case 14:
                    groupSizes.Add(7); groupSizes.Add(7); // 2 groups of 7
                    break;
                case 15:
                    groupSizes.Add(5); groupSizes.Add(5); groupSizes.Add(5); // 3 groups of 5
                    break;
                case 16:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); // 4 groups of 4
                    break;
                case 17:
                    groupSizes.Add(6); groupSizes.Add(6); groupSizes.Add(5); // 3 groups (6, 6, 5)
                    break;
                case 18:
                    groupSizes.Add(6); groupSizes.Add(6); groupSizes.Add(6); // 3 groups of 6
                    break;
                default:
                    // For player counts not explicitly defined, distribute players evenly
                    int baseSize = playerCount / groupCount;
                    int remainder = playerCount % groupCount;

                    for (int i = 0; i < groupCount; i++)
                    {
                        // Give the first 'remainder' groups one extra player
                        groupSizes.Add(baseSize + (i < remainder ? 1 : 0));
                    }
                    break;
            }

            return groupSizes;
        }

        // Add new method to determine advancement criteria based on player count and group count
        private (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
        {
            // Return a tuple (groupWinners, bestThirdPlace) that specifies how many top players
            // from each group advance, and how many best third-place players advance

            return playerCount switch
            {
                7 => (4, 0),      // Top 4 advance to playoffs
                8 => (2, 0),      // Top 2 from each group
                9 => (2, 2),      // Top 2 from each group + 2 best third-place
                10 => (2, 0),     // Top 2 from each group
                11 => (2, 2),     // Top 2 from each group + 2 best third-place
                12 => (2, 2),     // Top 2 from each group + 2 best third-place
                13 => (2, 2),     // Top 2 from each group + 2 best third-place
                14 => (4, 0),     // Top 4 from each group
                15 => (2, 2),     // Top 2 from each group + 2 best third-place
                16 => (2, 0),     // Top 2 from each group
                17 => (2, 2),     // Top 2 from each group + 2 best third-place
                18 => (2, 2),     // Top 2 from each group + 2 best third-place
                _ => groupCount < 3 ? (2, 0) : (2, 2) // Default based on group count
            };
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
            int groupCount = DetermineGroupCount(playerCount, signup.Format);

            // Get optimal group sizes
            List<int> groupSizes = GetOptimalGroupSizes(playerCount, groupCount);

            // Get advancement criteria
            var advancementCriteria = GetAdvancementCriteria(playerCount, groupCount);

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

                Console.WriteLine($"Successfully removed {player.DisplayName} (ID: {player.Id}) from signup '{tournamentName}'");
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
                    await context.EditResponseAsync("You need administrator permission to repair tournament data.");
                    return;
                }

                await context.EditResponseAsync("Repairing tournament data files...");

                await _tournamentManager.RepairDataFiles(context.Client);

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
                    var participantSeed = new ParticipantSeed();
                    participantSeed.SetPlayer(player);
                    participantSeed.Seed = seed;
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
            Console.WriteLine($"Starting group stage matches for tournament '{tournament.Name}' with {tournament.Groups.Count} groups");

            // Track which players already have a match scheduled
            var playersWithScheduledMatches = new HashSet<ulong>();

            // Process each group
            foreach (var group in tournament.Groups)
            {
                Console.WriteLine($"Processing group {group.Name} with {group.Participants.Count} participants and {group.Matches.Count} matches");

                // First, convert all players to proper DiscordMember objects
                Console.WriteLine("Converting all participants to DiscordMember objects");
                foreach (var participant in group.Participants)
                {
                    if (participant.Player != null)
                    {
                        // Get the player ID
                        ulong? playerId = null;
                        if (participant.Player is DiscordMember member)
                        {
                            playerId = member.Id;
                            Console.WriteLine($"Player {member.Username} (ID: {member.Id}) is already a DiscordMember");
                        }
                        else
                        {
                            Console.WriteLine($"Player object is not a DiscordMember: {participant.Player?.GetType().Name ?? "null"}, attempting conversion");
                            // Try to get ID using reflection or ToString parsing
                            var playerObj = participant.Player;
                            if (playerObj != null)
                            {
                                var idProperty = playerObj.GetType().GetProperty("Id");
                                if (idProperty != null)
                                {
                                    var idValue = idProperty.GetValue(playerObj);
                                    if (idValue != null && ulong.TryParse(idValue.ToString(), out ulong playerId1))
                                    {
                                        playerId = playerId1;
                                        Console.WriteLine($"Retrieved player ID {playerId1} using reflection");
                                    }
                                }
                                else
                                {
                                    // Parse from string representation as last resort
                                    string playerString = playerObj.ToString() ?? "";
                                    Console.WriteLine($"Attempting to parse player ID from string: {playerString}");
                                    var idMatch = System.Text.RegularExpressions.Regex.Match(playerString, @"Id\s*=\s*(\d+)");
                                    if (idMatch.Success && ulong.TryParse(idMatch.Groups[1].Value, out ulong playerId2))
                                    {
                                        playerId = playerId2;
                                        Console.WriteLine($"Retrieved player ID {playerId2} from string representation");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("Player object is null, cannot determine ID");
                            }
                        }

                        // If we have a valid ID, fetch the DiscordMember
                        if (playerId.HasValue)
                        {
                            try
                            {
                                Console.WriteLine($"Fetching DiscordMember for player with ID {playerId}");
                                // Find all guilds this client is in
                                foreach (var guild in client.Guilds.Values)
                                {
                                    try
                                    {
                                        var memberObj = await guild.GetMemberAsync(playerId.Value);
                                        if (memberObj is not null)
                                        {
                                            // Successfully found the member, update the object
                                            participant.Player = memberObj;
                                            Console.WriteLine($"Successfully converted player to DiscordMember: {memberObj.Username} (ID: {memberObj.Id})");
                                            break;
                                        }
                                    }
                                    catch (Exception guildEx)
                                    {
                                        // Member not in this guild, continue to next
                                        Console.WriteLine($"Player with ID {playerId} not found in guild {guild.Name}: {guildEx.Message}");
                                        continue;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to convert player ID {playerId} to DiscordMember: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Could not determine player ID for conversion: {participant.Player}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Found a participant with null Player property");
                    }
                }

                // Now create only the first round of matches (one match per player)
                Console.WriteLine("Creating first round of matches - one match per player");

                // Get valid participants (those that are DiscordMember objects)
                var validParticipants = group.Participants
                    .Where(p => p.Player is DiscordMember)
                    .ToList();

                // Randomize the order to create fair initial pairings
                var shuffledParticipants = validParticipants
                    .OrderBy(_ => Guid.NewGuid())
                    .ToList();

                // Create matches, ensuring each player only gets one match
                for (int i = 0; i < shuffledParticipants.Count; i += 2)
                {
                    // If we have an odd number of players and this is the last one, skip
                    if (i + 1 >= shuffledParticipants.Count)
                        break;

                    var player1 = shuffledParticipants[i].Player as DiscordMember;
                    var player2 = shuffledParticipants[i + 1].Player as DiscordMember;

                    if (player1 is null || player2 is null)
                        continue;

                    // Check if either player already has a match scheduled
                    if (playersWithScheduledMatches.Contains(player1.Id) ||
                        playersWithScheduledMatches.Contains(player2.Id))
                        continue;

                    // Find if a match already exists between these players
                    var existingMatch = group.Matches.FirstOrDefault(m =>
                        m.Participants.Count == 2 &&
                        m.Participants.Any(p => p.Player is DiscordMember member && member.Id == player1.Id) &&
                        m.Participants.Any(p => p.Player is DiscordMember member && member.Id == player2.Id));

                    // Create thread for this match
                    Console.WriteLine($"Creating match thread for {player1.Username} vs {player2.Username}");

                    try
                    {
                        // Use the existing match or create a new one if needed
                        if (existingMatch == null)
                        {
                            existingMatch = new Tournament.Match
                            {
                                Name = $"{player1.DisplayName} vs {player2.DisplayName}",
                                Type = MatchType.GroupStage,
                                BestOf = 3, // Use Bo3 for group stages
                                Participants = new List<Tournament.MatchParticipant>
                                {
                                    new Tournament.MatchParticipant { Player = player1 },
                                    new Tournament.MatchParticipant { Player = player2 }
                                }
                            };

                            // Add the match to the group
                            group.Matches.Add(existingMatch);
                        }

                        // Create the thread and set up the match
                        await CreateAndStart1v1Match(tournament, group, player1, player2, client, 3, existingMatch);

                        // Mark these players as having a scheduled match
                        playersWithScheduledMatches.Add(player1.Id);
                        playersWithScheduledMatches.Add(player2.Id);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating match for {player1.Username} vs {player2.Username}: {ex.Message}");
                    }

                    // Add a slight delay to avoid rate limiting
                    await Task.Delay(1000);
                }
            }

            Console.WriteLine("Saving tournament state after creating initial matches");
            // Update tournament state
            await _tournamentManager.SaveTournamentState(client);

            Console.WriteLine("Posting tournament visualization");
            // Post the tournament visualization after matches are created
            await _tournamentManager.PostTournamentVisualization(tournament, client);
        }

        // Helper method to extract player ID from an object
        private ulong? GetPlayerIdFromObject(object playerObj)
        {
            if (playerObj == null)
                return null;

            if (playerObj is DiscordMember member)
                return member.Id;

            // Try to get ID using reflection
            var idProperty = playerObj.GetType().GetProperty("Id");
            if (idProperty != null)
            {
                var idValue = idProperty.GetValue(playerObj);
                if (idValue != null && ulong.TryParse(idValue.ToString(), out ulong reflectionId))
                    return reflectionId;
            }

            // Parse from string representation as last resort
            string playerString = playerObj.ToString() ?? "";
            var idMatch = System.Text.RegularExpressions.Regex.Match(playerString, @"Id\s*=\s*(\d+)");
            if (idMatch.Success && ulong.TryParse(idMatch.Groups[1].Value, out ulong stringId))
                return stringId;

            return null;
        }

        // Helper method to get DiscordMember from ID
        private async Task<DiscordMember?> GetDiscordMemberFromId(ulong id, DiscordClient client)
        {
            foreach (var guild in client.Guilds.Values)
            {
                try
                {
                    var member = await guild.GetMemberAsync(id);
                    if (member is not null)
                        return member;
                }
                catch
                {
                    // Member not in this guild, continue to next
                    continue;
                }
            }
            return null;
        }

        private async Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember player1,
            DiscordMember player2,
            DiscordClient client,
            int matchLength,
            Tournament.Match? existingMatch = null)
        {
            Console.WriteLine($"Starting CreateAndStart1v1Match for {player1.Username} vs {player2.Username} in tournament '{tournament.Name}'");
            try
            {
                // Find or create a channel to use for the match
                // BotChannelId is used as AnnouncementChannel, but maybe add an option to configure an AnnouncementChannelId in the server config file.
                DiscordChannel? channel = tournament.AnnouncementChannel;
                Console.WriteLine($"Initial channel: {(channel is not null ? $"{channel.Name} (ID: {channel.Id})" : "null")}");

                if (channel is null)
                {
                    var guild = player1.Guild;
                    Console.WriteLine($"Looking for a channel in guild {guild.Name} (ID: {guild.Id})");

                    // Get the server config to find the bot channel ID
                    var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == guild.Id);
                    if (server != null && server.BotChannelId.HasValue)
                    {
                        try
                        {
                            Console.WriteLine($"Attempting to use configured bot channel ID: {server.BotChannelId.Value}");
                            channel = await guild.GetChannelAsync(server.BotChannelId.Value);
                            Console.WriteLine($"Found configured bot channel: {channel.Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to get bot channel with ID {server.BotChannelId.Value}: {ex.Message}");
                        }
                    }

                    // Fallback if bot channel ID is not configured or channel not found
                    if (channel is null)
                    {
                        Console.WriteLine("Fallback: Searching for a general or chat channel");
                        var channels = await guild.GetChannelsAsync();
                        channel = channels.FirstOrDefault(c =>
                            c.Type == DiscordChannelType.Text &&
                            (c.Name.Contains("general", StringComparison.OrdinalIgnoreCase) ||
                             c.Name.Contains("chat", StringComparison.OrdinalIgnoreCase))
                        );

                        // If no suitable channel found, use the first text channel
                        if (channel is null)
                        {
                            Console.WriteLine("No general/chat channel found, using first text channel");
                            channel = channels.FirstOrDefault(c => c.Type == DiscordChannelType.Text);
                        }
                    }

                    if (channel is not null)
                    {
                        Console.WriteLine($"Using channel: {channel.Name} (ID: {channel.Id})");
                    }
                    else
                    {
                        Console.WriteLine($"Could not find a suitable channel for tournament match in guild {guild.Name}");
                        return;
                    }
                }

                // Use existing match or create a new one
                Tournament.Match? match = existingMatch;
                if (match == null)
                {
                    // Create a new match
                    Console.WriteLine("Creating new tournament match object");
                    match = new Tournament.Match
                    {
                        Name = $"{player1.DisplayName} vs {player2.DisplayName}",
                        Type = MatchType.GroupStage,
                        BestOf = matchLength,
                        Participants = new List<Tournament.MatchParticipant>
                        {
                            new Tournament.MatchParticipant { Player = player1 },
                            new Tournament.MatchParticipant { Player = player2 }
                        }
                    };

                    // Add match to the group
                    group?.Matches.Add(match);
                    Console.WriteLine($"Added match to group {group?.Name}");
                }
                else
                {
                    Console.WriteLine($"Using existing match object: {match.Name}");
                    // Ensure players in the match are set correctly to DiscordMember objects
                    if (match.Participants.Count == 2)
                    {
                        match.Participants[0].Player = player1;
                        match.Participants[1].Player = player2;
                    }
                }

                // Create and start a round for this match
                Console.WriteLine("Creating Round object for the match");
                var round = new Round
                {
                    Name = match.Name,
                    Length = matchLength,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    Pings = $"{player1.Mention} {player2.Mention}"
                };

                // Create teams (one player per team)
                var team1 = new Round.Team
                {
                    Name = player1.DisplayName,
                    Participants = new List<Round.Participant> { new Round.Participant { Player = player1 } }
                };

                var team2 = new Round.Team
                {
                    Name = player2.DisplayName,
                    Participants = new List<Round.Participant> { new Round.Participant { Player = player2 } }
                };

                round.Teams.Add(team1);
                round.Teams.Add(team2);

                // Create a private thread for each player
                Console.WriteLine($"Attempting to create private threads in channel {channel.Name} (ID: {channel.Id})");
                try
                {
                    // Create thread for player 1
                    var thread1 = await channel.CreateThreadAsync(
                        $"Match: {player1.DisplayName} vs {player2.DisplayName} (thread for {player1.DisplayName})",
                        DiscordAutoArchiveDuration.Day,
                        DiscordChannelType.PrivateThread,
                        $"Tournament match thread for {player1.DisplayName}"
                    );

                    Console.WriteLine($"Successfully created thread for player 1: {thread1.Name} (ID: {thread1.Id})");
                    team1.Thread = thread1;

                    // Add player 1 to their thread
                    Console.WriteLine($"Adding player 1 to thread: {player1.Username}");
                    await thread1.AddThreadMemberAsync(player1);

                    // Create thread for player 2
                    var thread2 = await channel.CreateThreadAsync(
                        $"Match: {player1.DisplayName} vs {player2.DisplayName} (thread for {player2.DisplayName})",
                        DiscordAutoArchiveDuration.Day,
                        DiscordChannelType.PrivateThread,
                        $"Tournament match thread for {player2.DisplayName}"
                    );

                    Console.WriteLine($"Successfully created thread for player 2: {thread2.Name} (ID: {thread2.Id})");
                    team2.Thread = thread2;

                    // Add player 2 to their thread
                    Console.WriteLine($"Adding player 2 to thread: {player2.Username}");
                    await thread2.AddThreadMemberAsync(player2);

                    Console.WriteLine("Successfully added players to their respective threads");
                }
                catch (Exception threadEx)
                {
                    Console.WriteLine($"Failed to create or setup private threads: {threadEx.Message}");
                    Console.WriteLine($"Error details: {threadEx}");
                    // We still want to continue to create the match even if thread creation fails
                    // so we'll just log the error and continue
                }

                // Create map ban dropdown options
                Console.WriteLine("Getting map pool for match");
                string?[] maps1v1 = _tournamentManager.GetTournamentMapPool(true).ToArray();

                var options = new List<DiscordSelectComponentOption>();
                foreach (var map in maps1v1)
                {
                    if (map is not null)
                    {
                        var option = new DiscordSelectComponentOption(map, map);
                        options.Add(option);
                    }
                }
                Console.WriteLine($"Created {options.Count} map options");

                // Create map ban dropdown
                DiscordSelectComponent mapBanDropdown;
                string message;

                switch (matchLength)
                {
                    case 3:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                             "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    case 5:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    default:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 1, 1);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 1 map to ban. This map will not be played in this match.\n\n" +
                            "**Note:** After making your selection, you'll have a chance to review your choice and confirm or revise it.";
                        break;
                }

                // Create winner selection dropdown
                Console.WriteLine("Creating game winner selection dropdown for the first game");
                var winnerOptions = new List<DiscordSelectComponentOption>
                {
                    new DiscordSelectComponentOption(
                        $"{player1.DisplayName} wins",
                        $"game_winner:{player1.Id}",
                        $"{player1.DisplayName} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        $"{player2.DisplayName} wins",
                        $"game_winner:{player2.Id}",
                        $"{player2.DisplayName} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        "Draw",
                        "game_winner:draw",
                        "This game ended in a draw"
                    )
                };

                var winnerDropdown = new DiscordSelectComponent(
                    "tournament_game_winner_dropdown",
                    "Select the winner of this game",
                    winnerOptions,
                    false, 1, 1
                );

                // Create a custom property in the round to track match series state
                round.CustomProperties = new Dictionary<string, object>
                {
                    ["CurrentGame"] = 1,
                    ["Player1Wins"] = 0,
                    ["Player2Wins"] = 0,
                    ["Draws"] = 0,
                    ["MatchLength"] = matchLength,
                    ["Player1Id"] = player1.Id,
                    ["Player2Id"] = player2.Id
                };

                // Send messages to both threads if they were created successfully
                if (team1.Thread is not null)
                {
                    Console.WriteLine("Sending map bans and instructions to player 1's thread");
                    try
                    {
                        // Send map bans and instructions
                        var dropdownBuilder = new DiscordMessageBuilder()
                            .WithContent($"{player1.Mention}\n\n{message}")
                            .AddComponents(mapBanDropdown);

                        await team1.Thread.SendMessageAsync(dropdownBuilder);

                        // Send match overview
                        var matchOverview = new DiscordEmbedBuilder()
                            .WithTitle($"Match: {player1.DisplayName} vs {player2.DisplayName}")
                            .WithDescription($"Best of {matchLength} series")
                            .AddField("Current Score", "0 - 0", true)
                            .AddField("Game", "1/" + (matchLength > 1 ? matchLength.ToString() : "1"), true)
                            .WithColor(DiscordColor.Blurple);

                        await team1.Thread.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(matchOverview));

                        // Send game winner dropdown
                        var winnerBuilder = new DiscordMessageBuilder()
                            .WithContent("**When this game is complete, select the winner:**")
                            .AddComponents(winnerDropdown);

                        await team1.Thread.SendMessageAsync(winnerBuilder);
                        Console.WriteLine("Successfully sent components to player 1's thread");
                    }
                    catch (Exception msgEx)
                    {
                        Console.WriteLine($"Failed to send messages to player 1's thread: {msgEx.Message}");
                    }
                }

                // Send similar messages to player 2's thread
                if (team2.Thread is not null)
                {
                    Console.WriteLine("Sending map bans and instructions to player 2's thread");
                    try
                    {
                        // Send map bans and instructions
                        var dropdownBuilder = new DiscordMessageBuilder()
                            .WithContent($"{player2.Mention}\n\n{message}")
                            .AddComponents(mapBanDropdown);

                        await team2.Thread.SendMessageAsync(dropdownBuilder);

                        // Send match overview
                        var matchOverview = new DiscordEmbedBuilder()
                            .WithTitle($"Match: {player1.DisplayName} vs {player2.DisplayName}")
                            .WithDescription($"Best of {matchLength} series")
                            .AddField("Current Score", "0 - 0", true)
                            .AddField("Game", "1/" + (matchLength > 1 ? matchLength.ToString() : "1"), true)
                            .WithColor(DiscordColor.Blurple);

                        await team2.Thread.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(matchOverview));

                        // Send game winner dropdown
                        var winnerBuilder = new DiscordMessageBuilder()
                            .WithContent("**When this game is complete, select the winner:**")
                            .AddComponents(winnerDropdown);

                        await team2.Thread.SendMessageAsync(winnerBuilder);
                        Console.WriteLine("Successfully sent components to player 2's thread");
                    }
                    catch (Exception msgEx)
                    {
                        Console.WriteLine($"Failed to send messages to player 2's thread: {msgEx.Message}");
                    }
                }

                // Send announcement message to the channel
                Console.WriteLine("Sending match announcement to channel");
                try
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"🎮 Match Started: {player1.DisplayName} vs {player2.DisplayName}")
                        .WithDescription($"A new Bo{matchLength} match has started in Group {group?.Name}.")
                        .AddField("Players", $"{player1.Mention} vs {player2.Mention}", false)
                        .WithColor(DiscordColor.Blue)
                        .WithTimestamp(DateTime.Now);

                    await channel.SendMessageAsync(embed);
                    Console.WriteLine("Successfully sent match announcement");
                }
                catch (Exception announceEx)
                {
                    Console.WriteLine($"Failed to send match announcement: {announceEx.Message}");
                }

                // Add to ongoing rounds and link to tournament match
                Console.WriteLine("Adding round to ongoing rounds and linking to tournament match");
                _ongoingRounds.TourneyRounds.Add(round);
                match.LinkedRound = round;

                // Save tournament state
                Console.WriteLine("Saving tournament state");
                await _tournamentManager.SaveTournamentState(client);

                Console.WriteLine($"Successfully started match {match.Name} in Group {group?.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating 1v1 match between {player1?.DisplayName} and {player2?.DisplayName}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // New method to handle match completion and schedule next matches as needed
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            try
            {
                // Check if this is a tiebreaker match
                if (match.Type == MatchType.ThirdPlaceTiebreaker && tournament.CustomProperties != null &&
                    tournament.CustomProperties.ContainsKey("NeedsTiebreakerMatches") &&
                    tournament.CustomProperties.ContainsKey("TiebreakerMatches"))
                {
                    _logger.LogInformation($"Tiebreaker match completed: {match.Name}");

                    // Check if all tiebreaker matches are complete
                    var allTiebreakerMatchesComplete = true;

                    if (tournament.CustomProperties["TiebreakerMatches"] is List<Tournament.Match> tiebreakerMatches)
                    {
                        foreach (var tiebreakerMatch in tiebreakerMatches)
                        {
                            if (!tiebreakerMatch.IsComplete)
                            {
                                allTiebreakerMatchesComplete = false;
                                break;
                            }
                        }

                        if (allTiebreakerMatchesComplete)
                        {
                            _logger.LogInformation("All tiebreaker matches are complete. Resolving third-place qualification.");

                            // Get the tied participants and available spots
                            if (tournament.CustomProperties["TiedParticipants"] is List<Tournament.GroupParticipant> tiedParticipants &&
                                tournament.CustomProperties["BestThirdPlaceSpots"] is int availableSpots)
                            {
                                // Calculate points from tiebreaker matches
                                Dictionary<object, int> tiebreakerPoints = new Dictionary<object, int>();

                                foreach (var tiebreakerMatch in tiebreakerMatches)
                                {
                                    if (tiebreakerMatch.Result?.Winner != null)
                                    {
                                        // Award 3 points for a win in the tiebreaker match
                                        if (!tiebreakerPoints.ContainsKey(tiebreakerMatch.Result.Winner))
                                            tiebreakerPoints[tiebreakerMatch.Result.Winner] = 0;

                                        tiebreakerPoints[tiebreakerMatch.Result.Winner] += 3;
                                    }
                                }

                                // Rank tied participants by tiebreaker points
                                var resolvedParticipants = tiedParticipants
                                    .OrderByDescending(p =>
                                        p.Player is not null && tiebreakerPoints.ContainsKey(p.Player) ? tiebreakerPoints[p.Player] : 0)
                                    .ToList();

                                // Select the top participants for promotion
                                var qualifyingParticipants = resolvedParticipants.Take(availableSpots).ToList();

                                // Mark qualifying participants as advanced
                                foreach (var participant in qualifyingParticipants)
                                {
                                    participant.AdvancedToPlayoffs = true;
                                    participant.QualificationInfo = "Best Third Place (Tiebreaker)";

                                    _logger.LogInformation($"Player {GetPlayerDisplayName(participant.Player)} qualified after tiebreakers");
                                }

                                // Clean up tiebreaker-related properties
                                tournament.CustomProperties.Remove("NeedsTiebreakerMatches");
                                tournament.CustomProperties.Remove("TiebreakerMatches");
                                tournament.CustomProperties.Remove("TiedParticipants");
                                tournament.CustomProperties.Remove("BestThirdPlaceSpots");
                                tournament.CustomProperties.Remove("TiebreakerPlayersCount");

                                // Now we can proceed with playoff setup
                                _logger.LogInformation("Tiebreaker resolution complete. Setting up playoffs.");
                                tournament.CurrentStage = TournamentStage.Playoffs;

                                await _tournamentManager.SaveTournamentState(client);
                                await StartPlayoffMatches(tournament, client);
                                return;
                            }
                        }
                    }
                }

                // Original code for handling regular match completion
                if (match.LinkedRound is not null && match.Result is not null)
                {
                    // Find the group this match belongs to
                    var group = tournament.Groups.FirstOrDefault(g => g.Matches.Contains(match));
                    if (group == null)
                        return; // Not a group stage match

                    // Check if the group is complete after this match
                    _tournamentManager.UpdateTournamentFromRound(tournament);

                    // If the group isn't complete, schedule new matches for these players if needed
                    if (!group.IsComplete)
                    {
                        foreach (var participant in match.Participants)
                        {
                            if (participant?.Player is not DiscordMember player)
                                continue;

                            // Find all participants this player hasn't played against yet
                            var opponentsToPlay = group.Participants
                                .Where(p => p.Player is DiscordMember && p.Player != participant.Player)
                                .Select(p => p.Player as DiscordMember)
                                .Where(opponent => opponent is not null && !group.Matches.Any(m =>
                                    m.Participants.Count == 2 &&
                                    m.Participants.Any(mp => mp.Player is DiscordMember playerMember && playerMember.Id == player.Id) &&
                                    m.Participants.Any(mp => mp.Player is DiscordMember opponentMember && opponentMember.Id == opponent.Id)))
                                .ToList();

                            // If this player has opponents they haven't played, create one new match
                            if (opponentsToPlay.Any())
                            {
                                // Schedule only one new match for this player
                                var opponent = opponentsToPlay.First();

                                // Guard against null opponent (shouldn't happen due to filtering, but compiler doesn't know that)
                                if (opponent is null)
                                {
                                    _logger.LogError("Opponent was null when attempting to create a match. Skipping.");
                                    continue;
                                }

                                // Check if this match is already being created by the other player's loop
                                var key = new[] { player.Id, opponent.Id }.OrderBy(id => id).ToArray();
                                var matchKey = $"{key[0]}_{key[1]}";

                                // Use a simple locking mechanism to avoid duplicate match creation
                                if (!_processingMatches.Contains(matchKey))
                                {
                                    try
                                    {
                                        _processingMatches.Add(matchKey);

                                        // First close the current match thread if it exists
                                        if (match.LinkedRound?.Teams != null)
                                        {
                                            foreach (var team in match.LinkedRound.Teams)
                                            {
                                                if (team.Thread is not null)
                                                {
                                                    try
                                                    {
                                                        await team.Thread.ModifyAsync(t =>
                                                        {
                                                            t.IsArchived = true;
                                                            t.Locked = true;
                                                        });
                                                    }
                                                    catch
                                                    {
                                                        // Thread might already be deleted or archived
                                                    }
                                                }
                                            }
                                        }

                                        // Create a new match
                                        _logger.LogInformation($"Creating new match for {player.DisplayName} vs {opponent.DisplayName} in group {group.Name}");
                                        await CreateAndStart1v1Match(tournament, group, player, opponent, client, 3);
                                    }
                                    finally
                                    {
                                        _processingMatches.Remove(matchKey);
                                    }
                                }

                                // Only schedule one new match per player to avoid flooding
                                break;
                            }
                        }
                    }
                    // If group is complete, check if all groups are complete to set up playoffs
                    else if (tournament.Groups.All(g => g.IsComplete) && tournament.CurrentStage == TournamentStage.Groups)
                    {
                        // Set up playoffs
                        _tournamentManager.SetupPlayoffs(tournament);

                        // Post updated standings
                        await _tournamentManager.PostTournamentVisualization(tournament, client);

                        // Start playoff matches if we're ready
                        if (tournament.CurrentStage == TournamentStage.Playoffs)
                        {
                            await StartPlayoffMatches(tournament, client);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling match completion");
            }
        }

        // Add a new field to track matches being processed
        private readonly HashSet<string> _processingMatches = new HashSet<string>();

        // Method to handle game result selection and advance the match series
        public async Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client)
        {
            if (thread is DiscordThreadChannel threadChannel)
            {
                await HandleGameResult(round, threadChannel, winnerId, client);
            }
            else
            {
                Console.WriteLine("Cannot handle game result: Channel is not a thread channel");
            }
        }

        // Method to handle game result selection and advance the match series
        private async Task HandleGameResult(Round round, DiscordThreadChannel reportingThread, string winnerId, DiscordClient client)
        {
            try
            {
                // Extract match information from round's custom properties
                var currentGame = (int)round.CustomProperties["CurrentGame"];
                var player1Wins = (int)round.CustomProperties["Player1Wins"];
                var player2Wins = (int)round.CustomProperties["Player2Wins"];
                var draws = (int)round.CustomProperties["Draws"];
                var matchLength = (int)round.CustomProperties["MatchLength"];
                var player1Id = (ulong)round.CustomProperties["Player1Id"];
                var player2Id = (ulong)round.CustomProperties["Player2Id"];

                // Get player objects
                DiscordMember? player1 = null;
                DiscordMember? player2 = null;

                // Get both team threads so we can update both players
                DiscordThreadChannel? player1Thread = null;
                DiscordThreadChannel? player2Thread = null;

                if (round.Teams != null)
                {
                    foreach (var team in round.Teams)
                    {
                        if (team?.Participants != null)
                        {
                            foreach (var participant in team.Participants)
                            {
                                if (participant?.Player is DiscordMember member)
                                {
                                    if (member.Id == player1Id)
                                    {
                                        player1 = member;
                                        player1Thread = team.Thread;
                                    }
                                    else if (member.Id == player2Id)
                                    {
                                        player2 = member;
                                        player2Thread = team.Thread;
                                    }
                                }
                            }
                        }
                    }
                }

                if (player1 is null || player2 is null)
                {
                    await reportingThread.SendMessageAsync("⚠️ Error: Could not find player information.");
                    return;
                }

                // Get list of all threads to update
                List<DiscordThreadChannel> threadsToUpdate = new List<DiscordThreadChannel>();
                if (player1Thread is not null) threadsToUpdate.Add(player1Thread);
                if (player2Thread is not null) threadsToUpdate.Add(player2Thread);

                // If no threads found, use the reporting thread
                if (threadsToUpdate.Count == 0)
                {
                    threadsToUpdate.Add(reportingThread);
                }

                // Update scores based on winner
                bool isDraw = winnerId == "draw";
                string gameResultMessage;

                if (isDraw)
                {
                    draws++;
                    round.CustomProperties["Draws"] = draws;
                    gameResultMessage = $"Game {currentGame} ended in a draw!";
                }
                else if (winnerId == player1Id.ToString())
                {
                    player1Wins++;
                    round.CustomProperties["Player1Wins"] = player1Wins;
                    gameResultMessage = $"Game {currentGame}: {player1.DisplayName} wins!";
                }
                else if (winnerId == player2Id.ToString())
                {
                    player2Wins++;
                    round.CustomProperties["Player2Wins"] = player2Wins;
                    gameResultMessage = $"Game {currentGame}: {player2.DisplayName} wins!";
                }
                else
                {
                    gameResultMessage = "Invalid winner selection.";
                }

                // Send game result to all threads
                foreach (var currentThread in threadsToUpdate)
                {
                    await currentThread.SendMessageAsync(gameResultMessage);
                }

                // Check if match is complete
                bool isMatchComplete = false;
                string matchResult = "";

                // For Bo1, match is complete after 1 game
                if (matchLength == 1)
                {
                    isMatchComplete = true;
                    if (isDraw)
                        matchResult = "Match ended in a draw!";
                    else if (player1Wins > 0)
                        matchResult = $"{player1.DisplayName} wins the match!";
                    else
                        matchResult = $"{player2.DisplayName} wins the match!";
                }
                // For Bo3, match is complete if:
                else if (matchLength == 3)
                {
                    // Player has 2 wins
                    if (player1Wins >= 2)
                    {
                        isMatchComplete = true;
                        matchResult = $"{player1.DisplayName} wins the match {player1Wins}-{player2Wins}!";
                    }
                    else if (player2Wins >= 2)
                    {
                        isMatchComplete = true;
                        matchResult = $"{player2.DisplayName} wins the match {player2Wins}-{player1Wins}!";
                    }
                    // Mathematical draw (1-1-1)
                    else if (player1Wins == 1 && player2Wins == 1 && draws == 1)
                    {
                        isMatchComplete = true;
                        matchResult = "Match ended in a draw!";
                    }
                    // Max games played without a winner
                    else if (currentGame >= matchLength)
                    {
                        isMatchComplete = true;
                        if (player1Wins > player2Wins)
                            matchResult = $"{player1.DisplayName} wins the match {player1Wins}-{player2Wins}!";
                        else if (player2Wins > player1Wins)
                            matchResult = $"{player2.DisplayName} wins the match {player2Wins}-{player1Wins}!";
                        else
                            matchResult = "Match ended in a draw!";
                    }
                }
                // For Bo5, match is complete if:
                else if (matchLength == 5)
                {
                    // Player has 3 wins
                    if (player1Wins >= 3)
                    {
                        isMatchComplete = true;
                        matchResult = $"{player1.DisplayName} wins the match {player1Wins}-{player2Wins}!";
                    }
                    else if (player2Wins >= 3)
                    {
                        isMatchComplete = true;
                        matchResult = $"{player2.DisplayName} wins the match {player2Wins}-{player1Wins}!";
                    }
                    // Mathematical draw (various scenarios where no one can reach 3 wins)
                    else if (player1Wins == 2 && player2Wins == 2 && draws == 1)
                    {
                        isMatchComplete = true;
                        matchResult = "Match ended in a draw!";
                    }
                    // Max games played without a winner
                    else if (currentGame >= matchLength)
                    {
                        isMatchComplete = true;
                        if (player1Wins > player2Wins)
                            matchResult = $"{player1.DisplayName} wins the match {player1Wins}-{player2Wins}!";
                        else if (player2Wins > player1Wins)
                            matchResult = $"{player2.DisplayName} wins the match {player2Wins}-{player1Wins}!";
                        else
                            matchResult = "Match ended in a draw!";
                    }
                }

                // Update match overview in all threads
                var matchOverview = new DiscordEmbedBuilder()
                    .WithTitle($"Match: {player1.DisplayName} vs {player2.DisplayName}")
                    .WithDescription($"Best of {matchLength} series")
                    .AddField("Current Score", $"{player1Wins} - {player2Wins}" + (draws > 0 ? $" (Draws: {draws})" : ""), true)
                    .AddField("Game", $"{currentGame}/{(matchLength > 1 ? matchLength.ToString() : "1")}", true)
                    .WithColor(isMatchComplete ? DiscordColor.Green : DiscordColor.Blurple);

                foreach (var currentThread in threadsToUpdate)
                {
                    await currentThread.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(matchOverview));
                }

                if (isMatchComplete)
                {
                    // Match is complete - announce winner in all threads
                    foreach (var currentThread in threadsToUpdate)
                    {
                        await currentThread.SendMessageAsync($"🏆 **{matchResult}**");
                    }

                    // Find the tournament this match belongs to
                    var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                        t.Groups.Any(g => g.Matches.Any(m => m.LinkedRound == round)));

                    if (tournament != null)
                    {
                        var match = tournament.Groups
                            .SelectMany(g => g.Matches)
                            .FirstOrDefault(m => m.LinkedRound == round);

                        if (match != null)
                        {
                            // Update match with the result
                            // IsComplete is a read-only property based on Result != null
                            var resultRecord = new Tournament.MatchResult();

                            // Set winner
                            if (!isDraw && player1Wins != player2Wins)
                            {
                                var winningPlayerId = player1Wins > player2Wins ? player1Id : player2Id;
                                resultRecord.Winner = match.Participants.FirstOrDefault(p =>
                                    p.Player is DiscordMember member && member.Id == winningPlayerId)?.Player;
                            }

                            // Set the result which implicitly sets IsComplete to true
                            match.Result = resultRecord;

                            // Save state
                            await _tournamentManager.SaveTournamentState(client);

                            // Update tournament visualization
                            await _tournamentManager.PostTournamentVisualization(tournament, client);

                            // Handle match completion (schedule next matches, etc.)
                            await HandleMatchCompletion(tournament, match, client);
                        }
                    }

                    // Archive all threads
                    foreach (var currentThread in threadsToUpdate)
                    {
                        try
                        {
                            await currentThread.ModifyAsync(t =>
                            {
                                t.IsArchived = true;
                                t.AutoArchiveDuration = DiscordAutoArchiveDuration.Hour;
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error archiving thread {currentThread.Id}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Match continues - move to next game
                    currentGame++;
                    round.CustomProperties["CurrentGame"] = currentGame;

                    // Create winner dropdown for next game
                    var winnerOptions = new List<DiscordSelectComponentOption>
                    {
                        new DiscordSelectComponentOption(
                            $"{player1.DisplayName} wins",
                            $"game_winner:{player1.Id}",
                            $"{player1.DisplayName} wins this game"
                        ),
                        new DiscordSelectComponentOption(
                            $"{player2.DisplayName} wins",
                            $"game_winner:{player2.Id}",
                            $"{player2.DisplayName} wins this game"
                        ),
                        new DiscordSelectComponentOption(
                            "Draw",
                            "game_winner:draw",
                            "This game ended in a draw"
                        )
                    };

                    var winnerDropdown = new DiscordSelectComponent(
                        "tournament_game_winner_dropdown",
                        "Select the winner of this game",
                        winnerOptions,
                        false, 1, 1
                    );

                    // Send winner dropdown to all threads
                    foreach (var currentThread in threadsToUpdate)
                    {
                        await currentThread.SendMessageAsync(
                            new DiscordMessageBuilder()
                                .WithContent($"**Game {currentGame} starting - When this game is complete, select the winner:**")
                                .AddComponents(winnerDropdown)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling game result: {ex.Message}");
                await reportingThread.SendMessageAsync($"⚠️ Error handling game result: {ex.Message}");
            }
        }

        // Update the StartPlayoffMatches method to handle dynamic playoff structures
        private async Task StartPlayoffMatches(Tournament tournament, DiscordClient client)
        {
            // Log that playoffs are starting
            _logger.LogInformation($"Starting playoff matches for tournament {tournament.Name}");

            try
            {
                // Get the advancement criteria from tournament's custom properties
                int groupWinnersAdvance = 2; // Default: top 2 from each group
                int bestThirdPlaceAdvance = 0; // Default: no third-place players

                if (tournament.CustomProperties != null)
                {
                    if (tournament.CustomProperties.TryGetValue("GroupWinnersAdvance", out var winnersObj) && winnersObj is int winners)
                        groupWinnersAdvance = winners;

                    if (tournament.CustomProperties.TryGetValue("BestThirdPlaceAdvance", out var thirdPlaceObj) && thirdPlaceObj is int thirdPlace)
                        bestThirdPlaceAdvance = thirdPlace;
                }

                _logger.LogInformation($"Playoff advancement criteria: Top {groupWinnersAdvance} from each group + {bestThirdPlaceAdvance} best third-place players");

                // Collect qualified players
                List<Tournament.GroupParticipant> qualifiedParticipants = new List<Tournament.GroupParticipant>();

                // Get group winners according to criteria
                foreach (var group in tournament.Groups)
                {
                    // Sort participants by points (wins - losses)
                    var sortedParticipants = group.Participants
                        .OrderByDescending(p => p.Wins - p.Losses)
                        .ThenByDescending(p => p.Wins)
                        .ToList();

                    // Add the top X players from each group
                    for (int i = 0; i < Math.Min(groupWinnersAdvance, sortedParticipants.Count); i++)
                    {
                        sortedParticipants[i].QualificationInfo = $"Group {group.Name} - Position {i + 1}";
                        qualifiedParticipants.Add(sortedParticipants[i]);
                    }
                }

                // If we need to add best third-place finishers
                if (bestThirdPlaceAdvance > 0)
                {
                    // Collect all third-place finishers from all groups
                    var thirdPlaceParticipants = new List<Tournament.GroupParticipant>();

                    foreach (var group in tournament.Groups)
                    {
                        // Sort participants by points
                        var sortedParticipants = group.Participants
                            .OrderByDescending(p => p.Wins - p.Losses)
                            .ThenByDescending(p => p.Wins)
                            .ToList();

                        // Add the third-place player if the group has enough participants
                        if (sortedParticipants.Count >= 3)
                        {
                            sortedParticipants[2].QualificationInfo = $"Group {group.Name} - 3rd Place";
                            thirdPlaceParticipants.Add(sortedParticipants[2]);
                        }
                    }

                    // Sort third-place finishers by points and add the best ones, detecting ties
                    var thirdPlaceSorted = thirdPlaceParticipants
                        .OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.Wins)
                        .ThenBy(p => p.Losses)
                        .ThenByDescending(p => p.GamesWon - p.GamesLost) // Game differential
                        .ThenByDescending(p => p.GamesWon) // Total games won as final tiebreaker
                        .ToList();

                    // If we have more third place candidates than spots available, check for ties
                    if (thirdPlaceSorted.Count > bestThirdPlaceAdvance)
                    {
                        // Get the participant at the cutoff position
                        var cutoffParticipant = thirdPlaceSorted[bestThirdPlaceAdvance - 1];

                        // Find any participants tied with the cutoff participant
                        var tiedParticipants = thirdPlaceSorted
                            .Skip(bestThirdPlaceAdvance - 1) // Start from the cutoff position
                            .TakeWhile(p =>
                                p.Points == cutoffParticipant.Points &&
                                p.Wins == cutoffParticipant.Wins &&
                                p.Losses == cutoffParticipant.Losses &&
                                (p.GamesWon - p.GamesLost) == (cutoffParticipant.GamesWon - cutoffParticipant.GamesLost) &&
                                p.GamesWon == cutoffParticipant.GamesWon)
                            .ToList();

                        // If we have ties at the cutoff position
                        if (tiedParticipants.Count > 1)
                        {
                            _logger.LogInformation($"Detected {tiedParticipants.Count} players tied for the final best third-place spot(s). Creating tiebreaker matches.");

                            // Store information that tiebreaker matches are needed
                            if (tournament.CustomProperties == null)
                                tournament.CustomProperties = new Dictionary<string, object>();

                            tournament.CustomProperties["NeedsTiebreakerMatches"] = true;
                            tournament.CustomProperties["TiebreakerPlayersCount"] = tiedParticipants.Count;

                            // Create a list to store tiebreaker match data
                            var tiebreakerMatches = new List<Tournament.Match>();

                            // Create tiebreaker matches between tied players
                            for (int i = 0; i < tiedParticipants.Count; i++)
                            {
                                for (int j = i + 1; j < tiedParticipants.Count; j++)
                                {
                                    // Get the players
                                    var player1 = tiedParticipants[i].Player;
                                    var player2 = tiedParticipants[j].Player;

                                    _logger.LogInformation($"Creating tiebreaker match between {GetPlayerDisplayName(player1)} and {GetPlayerDisplayName(player2)}");

                                    // Create a special tiebreaker match
                                    var tiebreakerMatch = new Tournament.Match
                                    {
                                        Name = $"Tiebreaker: {GetPlayerDisplayName(player1)} vs {GetPlayerDisplayName(player2)}",
                                        Type = MatchType.ThirdPlaceTiebreaker, // Using ThirdPlace for now, could add a dedicated type
                                        BestOf = 1, // Single game tiebreaker
                                        Participants = new List<Tournament.MatchParticipant>
                                        {
                                            new Tournament.MatchParticipant { Player = player1 },
                                            new Tournament.MatchParticipant { Player = player2 }
                                        }
                                    };

                                    // Add to the list of tiebreaker matches
                                    tiebreakerMatches.Add(tiebreakerMatch);
                                    tournament.PlayoffMatches.Add(tiebreakerMatch);

                                    // Start the match if players are DiscordMembers
                                    if (player1 is DiscordMember member1 && player2 is DiscordMember member2)
                                    {
                                        // Start the match with a special indicator that it's a tiebreaker
                                        await CreateAndStart1v1Match(tournament, null, member1, member2, client, 1, tiebreakerMatch);

                                        // Add a slight delay to avoid rate limiting
                                        await Task.Delay(1000);
                                    }
                                }
                            }

                            // Store tiebreaker matches and participants for later reference
                            tournament.CustomProperties["TiebreakerMatches"] = tiebreakerMatches;
                            tournament.CustomProperties["TiedParticipants"] = tiedParticipants;
                            tournament.CustomProperties["BestThirdPlaceSpots"] = bestThirdPlaceAdvance;

                            // Store current state and display message
                            await _tournamentManager.SaveTournamentState(client);

                            _logger.LogInformation("Tiebreaker matches have been created. Playoffs will be set up once all tiebreaker matches are complete.");

                            // Exit early - playoffs will be set up once tiebreakers are complete
                            return;
                        }
                    }

                    // No ties detected, or ties don't affect qualification, proceed normally
                    var bestThirdPlace = thirdPlaceSorted.Take(bestThirdPlaceAdvance).ToList();

                    // Mark the selected players as advanced and update their qualification info
                    foreach (var participant in bestThirdPlace)
                    {
                        participant.AdvancedToPlayoffs = true;
                        participant.QualificationInfo = "Best Third Place";
                    }

                    qualifiedParticipants.AddRange(bestThirdPlace);
                }

                // Log qualified participants
                _logger.LogInformation($"Total qualified participants for playoffs: {qualifiedParticipants.Count}");

                // Create playoff bracket
                _logger.LogInformation("Creating playoff matches...");

                // Shuffle or seed the participants
                qualifiedParticipants = qualifiedParticipants
                    .OrderBy(_ => Guid.NewGuid()) // Random seeding for now
                    .ToList();

                // Create playoff matches based on the total number of qualified participants
                // For a proper bracket, we need a power of 2 (8, 16, etc.)
                int bracketSize = 1;
                while (bracketSize < qualifiedParticipants.Count)
                    bracketSize *= 2;

                _logger.LogInformation($"Creating a {bracketSize}-player bracket for {qualifiedParticipants.Count} participants");

                // Create matches with byes as needed
                for (int i = 0; i < bracketSize / 2; i++)
                {
                    // In a standard bracket, match i plays against (bracketSize-1-i)
                    int player1Index = i;
                    int player2Index = bracketSize - 1 - i;

                    // Check if both indices are valid
                    bool hasPlayer1 = player1Index < qualifiedParticipants.Count;
                    bool hasPlayer2 = player2Index < qualifiedParticipants.Count;

                    if (!hasPlayer1 && !hasPlayer2)
                        continue; // Skip this match if both players would be byes

                    var player1 = hasPlayer1 ? qualifiedParticipants[player1Index].Player : null;
                    var player2 = hasPlayer2 ? qualifiedParticipants[player2Index].Player : null;

                    // If one player is a bye, the other automatically advances
                    if (!hasPlayer1 || !hasPlayer2)
                    {
                        var advancingPlayer = hasPlayer1 ? player1 : player2;

                        // Create a placeholder match that's already complete
                        var byeMatch = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(advancingPlayer)} advances (bye)",
                            Type = MatchType.PlayoffStage,
                            BestOf = 3,
                            Result = new Tournament.MatchResult
                            {
                                Winner = advancingPlayer
                            },
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new Tournament.MatchParticipant { Player = advancingPlayer }
                            }
                        };

                        tournament.PlayoffMatches.Add(byeMatch);
                        _logger.LogInformation($"Created bye match: {byeMatch.Name}");
                    }
                    else
                    {
                        // Create a normal match with both players
                        var match = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(player1)} vs {GetPlayerDisplayName(player2)}",
                            Type = MatchType.PlayoffStage,
                            BestOf = 3,
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new Tournament.MatchParticipant { Player = player1 },
                                new Tournament.MatchParticipant { Player = player2 }
                            }
                        };

                        tournament.PlayoffMatches.Add(match);
                        _logger.LogInformation($"Created playoff match: {match.Name}");

                        // Start the match
                        if (player1 is DiscordMember member1 && player2 is DiscordMember member2)
                        {
                            await CreateAndStart1v1Match(tournament, null, member1, member2, client, 3, match);
                        }
                        else
                        {
                            _logger.LogWarning($"Could not start match because one or both players are not valid DiscordMembers");
                        }
                    }
                }

                // Update tournament state
                await _tournamentManager.SaveTournamentState(client);

                // Update tournament visualization
                await _tournamentManager.PostTournamentVisualization(tournament, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up playoff matches");
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