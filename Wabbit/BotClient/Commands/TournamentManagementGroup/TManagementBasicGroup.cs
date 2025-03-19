using DSharpPlus.Commands;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.ComponentModel;
using Wabbit.BotClient.Config;

namespace Wabbit.BotClient.Commands
{
    public partial class TournamentManagementGroup
    {
        [Command("create_from_signup")]
        [Description("Create a tournament from an existing signup")]
        public async Task CreateTournamentFromSignup(
            CommandContext context,
            [Description("Signup name")] string signupName)
        {
            await SafeExecute(context, async () =>
            {
                // Get the signup
                TournamentSignup? signup = await _tournamentService.GetSignupWithParticipantsAsync(signupName, context.Client);
                if (signup == null)
                {
                    await SafeResponse(context, $"No signup found with name '{signupName}'");
                    return;
                }

                // Check if the signup has enough participants
                if (signup.Participants == null || signup.Participants.Count < 2)
                {
                    await SafeResponse(context, $"Signup '{signupName}' has fewer than 2 participants.");
                    return;
                }

                // Convert the signup to a tournament
                var tournament = await CreateTournamentWithSeeding(signup, context.Channel);

                // Display tournament standings
                await _tournamentService.PostTournamentVisualizationAsync(tournament, context.Client);

                // Start the group stage matches using the batch scheduler
                await _tournamentService.StartGroupStage(tournament, context.Client);

                // Respond to the user
                await SafeResponse(context, $"Tournament '{tournament.Name}' created and started from signup '{signupName}'.");
            }, "Failed to create tournament from signup");
        }

        private async Task<Tournament> CreateTournamentWithSeeding(TournamentSignup signup, DiscordChannel channel)
        {
            // Create initial tournament structure
            var tournament = new Tournament
            {
                Name = signup.Name,
                Format = signup.Format,
                GameType = signup.GameType.ToTournamentGameType(),
                CurrentStage = TournamentStage.Groups,
                AnnouncementChannel = channel,
                Groups = new List<Tournament.Group>()  // Initialize Groups
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
                var group = new Tournament.Group
                {
                    Name = $"Group {(char)('A' + i)}",
                    Participants = new List<Tournament.GroupParticipant>(),  // Initialize Participants
                    Matches = new List<Tournament.Match>()  // Initialize Matches
                };
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
                            var winner = FindTournamentWinner(tournament);
                            string winnerName = winner != null ? GetPlayerDisplayName(winner) : "Unknown";
                            status = $"✅ Complete - 🏆 Winner: {winnerName}";
                        }
                        else if (tournament.CurrentStage == TournamentStage.Groups)
                        {
                            bool allGroupsComplete = (tournament.Groups ?? Enumerable.Empty<Tournament.Group>()).All(g => g.IsComplete);
                            status = allGroupsComplete ? "⏳ Group Stage Complete" : "🏁 Group Stage";
                        }
                        else if (tournament.CurrentStage == TournamentStage.Playoffs)
                        {
                            status = "🥇 Playoffs";
                        }
                        else if (tournament.CurrentStage == TournamentStage.Complete)
                        {
                            var winner = FindTournamentWinner(tournament);
                            string winnerName = winner != null ? GetPlayerDisplayName(winner) : "Unknown";
                            status = $"✅ Complete - 🏆 Winner: {winnerName}";
                        }

                        int playerCount = (tournament.Groups ?? Enumerable.Empty<Tournament.Group>()).Sum(g => g.Participants?.Count ?? 0);
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
                    .AddField("Groups", (tournament.Groups?.Count ?? 0).ToString())
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

        // Handler for match completion
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            try
            {
                if (tournament == null || match == null || client == null)
                {
                    _logger.LogError("Cannot handle match completion: tournament, match, or client is null");
                    return;
                }

                // Call the tournament manager to handle match completion and schedule next matches
                await _tournamentService.HandleMatchCompletionEvent(tournament, match, client);

                // Update tournament visualization
                await _tournamentService.PostTournamentVisualizationAsync(tournament, client);

                _logger.LogInformation($"Handled completion of match {match.Name} in tournament {tournament.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling match completion: {ex.Message}");
            }
        }

        // Handler for game result selection
        public async Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client)
        {
            await _tournamentGameService.HandleGameResultAsync(round, thread, winnerId, client);
        }

        private async Task StartGroupStageMatches(Tournament tournament, DiscordClient client, TournamentGameType gameType)
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
                if (group.Participants == null)
                {
                    group.Participants = new List<Tournament.GroupParticipant>();
                }
                if (group.Matches == null)
                {
                    group.Matches = new List<Tournament.Match>();
                }

                _logger.LogInformation($"Processing group {group.Name}");
                // Convert GroupParticipants to DiscordMembers
                var participants = new List<DiscordMember>();
                foreach (var participant in group.Participants)
                {
                    if (participant?.Player is DiscordMember member)
                    {
                        participants.Add(member);
                    }
                    else if (participant?.Player is not null)
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
                        bool matchExists = group.Matches?.Any(m =>
                            m.Participants?.Count == 2 &&
                            ((m.Participants[0].Player is DiscordMember p1 && p1.Id == player1.Id &&
                              m.Participants[1].Player is DiscordMember p2 && p2.Id == player2.Id) ||
                             (m.Participants[0].Player is DiscordMember p3 && p3.Id == player2.Id &&
                              m.Participants[1].Player is DiscordMember p4 && p4.Id == player1.Id))) ?? false;

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
    }
}
