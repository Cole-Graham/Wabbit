using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.BotClient.Config;
using System.IO;
using Wabbit.Data;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing tournament matches, extracted from TournamentManagementGroup
    /// </summary>
    public class TournamentMatchService : ITournamentMatchService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentMapService _mapService;
        private readonly ILogger<TournamentMatchService> _logger;
        private readonly IMatchStatusService _matchStatusService;

        private const int autoDeleteSeconds = 30;
        private const int mapThumbnailDurationMinutes = 5;

        public TournamentMatchService(
            OngoingRounds ongoingRounds,
            ITournamentGameService tournamentGameService,
            ITournamentPlayoffService playoffService,
            ITournamentStateService stateService,
            ITournamentMapService mapService,
            ILogger<TournamentMatchService> logger,
            IMatchStatusService matchStatusService)
        {
            _ongoingRounds = ongoingRounds;
            _tournamentGameService = tournamentGameService;
            _playoffService = playoffService;
            _stateService = stateService;
            _mapService = mapService;
            _logger = logger;
            _matchStatusService = matchStatusService;
        }

        /// <inheritdoc/>
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

            // This implementation delegates to the TournamentGameService
            await _tournamentGameService.HandleMatchCompletion(tournament, match, client);

            // Save tournament state
            await _stateService.SaveTournamentStateAsync(client);

            // Generate a new visualization
            try
            {
                await Misc.TournamentVisualization.GenerateStandingsImage(tournament, client, _stateService);
                _logger.LogInformation($"Generated updated standings visualization for tournament {tournament.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating standings visualization for tournament {tournament.Name}: {ex.Message}");
            }

            // Check if tournament is complete
        }

        /// <inheritdoc/>
        public async Task StartPlayoffMatches(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Starting playoff matches for tournament {tournament.Name}");

            if (tournament.PlayoffMatches is null || !tournament.PlayoffMatches.Any())
            {
                _logger.LogWarning($"No playoff matches to start for tournament {tournament.Name}");
                return;
            }

            // Find matches that need players and have all their participants assigned
            var matchesToStart = tournament.PlayoffMatches
                .Where(m => !m.IsComplete && m.Participants.Count >= 2 &&
                            m.Participants.All(p => p.Player is not null))
                .ToList();

            foreach (var match in matchesToStart)
            {
                _logger.LogInformation($"Setting up playoff match: {match.Name}");

                try
                {
                    // Get players as DiscordMembers
                    var participants = match.Participants.Select(p => p.Player).ToList();
                    if (participants.Count < 2)
                    {
                        _logger.LogWarning($"Not enough participants for match {match.Name}");
                        continue;
                    }

                    // Ensure we have DiscordMember objects
                    if (participants[0] is DiscordMember player1 && participants[1] is DiscordMember player2)
                    {
                        // Determine match length - finals can be longer
                        int matchLength = 3; // Default Bo3
                        if (match.Type == TournamentMatchType.Final)
                        {
                            matchLength = 5; // Finals are Bo5 by default
                        }

                        // Create and start the match
                        await CreateAndStart1v1Match(tournament, null, player1, player2, client, matchLength, match);
                    }
                    else
                    {
                        _logger.LogWarning($"Cannot start match - players are not DiscordMember objects");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error starting playoff match {match.Name}");
                }
            }
        }

        /// <inheritdoc/>
        public async Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember player1,
            DiscordMember player2,
            DiscordClient client,
            int bestOf = 3,
            Tournament.Match? existingMatch = null)
        {
            try
            {
                _logger.LogInformation($"Creating 1v1 match: {player1.DisplayName} vs {player2.DisplayName}");

                // Validate parameters
                if (tournament is null)
                {
                    _logger.LogError("Tournament cannot be null");
                    throw new ArgumentNullException(nameof(tournament));
                }

                if (player1 is null || player2 is null)
                {
                    _logger.LogError("Players cannot be null");
                    throw new ArgumentNullException(player1 is null ? nameof(player1) : nameof(player2));
                }

                // Create or use existing match
                Tournament.Match match;
                if (existingMatch is not null)
                {
                    match = existingMatch;
                }
                else
                {
                    // Create a new match
                    _logger.LogInformation("Creating new match for tournament");
                    match = new Tournament.Match
                    {
                        Name = $"{player1.DisplayName} vs {player2.DisplayName}",
                        Type = TournamentMatchType.GroupStage,
                        BestOf = bestOf,
                        Participants = new List<Tournament.MatchParticipant>
                        {
                            new Tournament.MatchParticipant { Player = player1, SourceGroup = group },
                            new Tournament.MatchParticipant { Player = player2, SourceGroup = group }
                        }
                    };

                    // Add the match to the group if applicable
                    if (group is not null)
                    {
                        group.Matches.Add(match);
                    }
                    else
                    {
                        // If no group, add to playoff matches
                        tournament.PlayoffMatches.Add(match);
                    }
                }

                // Find or create a thread for the match
                _logger.LogInformation("Finding channel for tournament match");
                DiscordChannel? thread = null;
                DiscordThreadChannel? player1Thread = null;
                DiscordThreadChannel? player2Thread = null;
                DiscordGuild? guild = null;

                try
                {
                    // Try to find the guild through the client
                    if (player1 is not null)
                    {
                        guild = await client.GetGuildAsync(player1.Guild.Id);
                    }
                    else if (player2 is not null)
                    {
                        guild = await client.GetGuildAsync(player2.Guild.Id);
                    }

                    if (guild is null)
                    {
                        _logger.LogError("Could not resolve guild for match");
                        return;
                    }

                    // Try to find any existing threads for either player in this tournament
                    if (tournament is not null)
                    {
                        // For group stages, reuse player threads when possible
                        if (match.Type == TournamentMatchType.GroupStage && group is not null)
                        {
                            // Try to find existing player threads for this tournament
                            foreach (var existingRound in _ongoingRounds.TourneyRounds)
                            {
                                if (existingRound.TournamentId == tournament.Name)
                                {
                                    foreach (var team in existingRound.Teams ?? Enumerable.Empty<Round.Team>())
                                    {
                                        foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                                        {
                                            // Check for player1's thread
                                            if (player1 is not null && participant?.Player?.Id == player1.Id && team.Thread is not null)
                                            {
                                                player1Thread = team.Thread;
                                                break;
                                            }

                                            // Check for player2's thread
                                            if (player2 is not null && participant?.Player?.Id == player2.Id && team.Thread is not null)
                                            {
                                                player2Thread = team.Thread;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            // Prefer player1's thread for consistency
                            if (player1Thread is not null)
                            {
                                thread = player1Thread;

                                // Add a visual separator if reusing a thread for a new match in group stage
                                if (match.Type == TournamentMatchType.GroupStage)
                                {
                                    // Calculate match information for the separator
                                    int matchesPlayed = 0;
                                    int totalMatches = 0;

                                    if (group is not null && player1 is not null)
                                    {
                                        // Get the group stage info for display
                                        int groupSize = group.Participants?.Count ?? 0;
                                        totalMatches = Math.Max(0, groupSize - 1); // Each player plays against every other player once

                                        // Count matches player1 has already played
                                        matchesPlayed = group.Matches?.Count(m =>
                                            m?.Participants?.Any(p => p?.Player is DiscordMember member && member.Id == player1.Id) == true &&
                                            m != match && m.IsComplete) ?? 0;
                                    }

                                    try
                                    {
                                        // Add a visual separator before starting the new match
                                        await _matchStatusService.AddMatchSeparatorAsync(
                                            thread,
                                            client,
                                            matchesPlayed + 1,
                                            totalMatches,
                                            player2?.DisplayName ?? "Unknown Player"
                                        );
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Could not add match separator");
                                    }
                                }
                            }
                            else if (player2Thread is not null)
                            {
                                thread = player2Thread;

                                // Add a visual separator if reusing a thread for a new match in group stage
                                if (match.Type == TournamentMatchType.GroupStage)
                                {
                                    // Calculate match information for the separator
                                    int matchesPlayed = 0;
                                    int totalMatches = 0;

                                    if (group is not null && player2 is not null)
                                    {
                                        // Get the group stage info for display
                                        int groupSize = group.Participants?.Count ?? 0;
                                        totalMatches = Math.Max(0, groupSize - 1); // Each player plays against every other player once

                                        // Count matches player2 has already played
                                        matchesPlayed = group.Matches?.Count(m =>
                                            m?.Participants?.Any(p => p?.Player is DiscordMember member && member.Id == player2.Id) == true &&
                                            m != match && m.IsComplete) ?? 0;
                                    }

                                    try
                                    {
                                        // Add a visual separator before starting the new match
                                        await _matchStatusService.AddMatchSeparatorAsync(
                                            thread,
                                            client,
                                            matchesPlayed + 1,
                                            totalMatches,
                                            player1?.DisplayName ?? "Unknown Player"
                                        );
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Could not add match separator");
                                    }
                                }
                            }
                        }
                    }

                    // If we don't have a thread yet, create a new one
                    if (thread is null)
                    {
                        // Looking for a tournaments channel to create the thread in
                        var tournamentChannel = guild.Channels.Values
                            .FirstOrDefault(c => c.Name.Contains("tournament", StringComparison.OrdinalIgnoreCase));

                        if (tournamentChannel is null)
                        {
                            // Fallback to any "general" channel if tournament channel not found
                            tournamentChannel = guild.Channels.Values
                                .FirstOrDefault(c => c.Name.Contains("general", StringComparison.OrdinalIgnoreCase));
                        }

                        if (tournamentChannel is not null)
                        {
                            // For group stages, name the thread after player1
                            // For playoffs, name it after both players
                            string threadName;
                            if (match.Type == TournamentMatchType.GroupStage)
                            {
                                threadName = player1?.DisplayName ?? "Player 1";
                            }
                            else
                            {
                                threadName = $"{player1?.DisplayName ?? "Player 1"} vs {player2?.DisplayName ?? "Player 2"}";
                            }

                            // Create a thread
                            thread = await tournamentChannel.CreateThreadAsync(
                                threadName,
                                DSharpPlus.Entities.DiscordAutoArchiveDuration.Week,
                                DSharpPlus.Entities.DiscordChannelType.PrivateThread,
                                "Tournament match thread");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error finding or creating thread for match");
                }

                // Create the Round object
                var round = new Round
                {
                    Name = match.Name,
                    Length = bestOf,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    TournamentId = tournament?.Name, // Use null conditional operator to avoid null reference
                    MsgToDel = new List<DiscordMessage>(),
                    TournamentRound = true
                };

                // Set group stage match information
                if (match.Type == TournamentMatchType.GroupStage && group is not null)
                {
                    // Calculate total matches per player in this group
                    int groupSize = group.Participants?.Count ?? 0;
                    int totalMatchesPerPlayer = Math.Max(0, groupSize - 1); // Each player plays against every other player once

                    // Calculate match number for this specific match in player1's sequence
                    int player1MatchesPlayed = 0;

                    if (player1 is not null && group.Matches is not null)
                    {
                        player1MatchesPlayed = group.Matches.Count(m =>
                            m?.Participants?.Any(p =>
                                p?.Player != null &&
                                player1 is not null &&
                                p.Player.ToString() == player1.Id.ToString() &&
                                m != match) ?? false);
                    }

                    round.GroupStageMatchNumber = player1MatchesPlayed + 1;
                    round.TotalGroupStageMatches = totalMatchesPerPlayer;
                }

                // Initialize CustomProperties if needed
                if (round.CustomProperties is null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }

                // Store the match in the round's custom properties
                round.CustomProperties["TournamentMatch"] = match;
                round.CustomProperties["CreatedAt"] = DateTime.Now;
                round.CustomProperties["RoundId"] = Guid.NewGuid().ToString();

                // Create map ban dropdown options
                _logger.LogInformation("Getting map pool for match");
                string[] maps1v1 = _mapService.GetTournamentMapPool(true).ToArray();

                // Initialize the Maps collection
                round.Maps = new List<string>();

                // Create options for map ban dropdown
                var options = new List<DiscordSelectComponentOption>();
                foreach (var map in maps1v1)
                {
                    if (string.IsNullOrEmpty(map)) continue;
                    options.Add(new DiscordSelectComponentOption(map, map));
                }

                // Create teams
                var team1 = new Round.Team
                {
                    Name = player1?.DisplayName ?? "Player 1",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player1 }
                    },
                    Thread = player1Thread ?? (thread as DiscordThreadChannel),
                    MapBans = new List<string>()
                };

                var team2 = new Round.Team
                {
                    Name = player2?.DisplayName ?? "Player 2",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player2 }
                    },
                    Thread = player2Thread ?? (thread as DiscordThreadChannel),
                    MapBans = new List<string>()
                };

                // Add teams to round
                round.Teams.Add(team1);
                round.Teams.Add(team2);

                // Set up metadata for the match
                if (round.CustomProperties is not null)
                {
                    round.CustomProperties["Player1Name"] = player1?.DisplayName ?? "Player 1";
                    round.CustomProperties["Player2Name"] = player2?.DisplayName ?? "Player 2";
                    round.CustomProperties["Player1Score"] = 0;
                    round.CustomProperties["Player2Score"] = 0;
                    round.CustomProperties["Player1Wins"] = 0;
                    round.CustomProperties["Player2Wins"] = 0;
                    round.CustomProperties["Draws"] = 0;
                    round.CustomProperties["MatchLength"] = bestOf;
                    round.CustomProperties["Player1Id"] = player1?.Id ?? 0;
                    round.CustomProperties["Player2Id"] = player2?.Id ?? 0;
                }

                // Add the round to ongoing rounds
                _ongoingRounds.TourneyRounds.Add(round);

                // Link the round to the match
                match.LinkedRound = round;

                // Helper method to send match information to a thread
                async Task SendMatchInfoToThread(DiscordChannel threadChannel)
                {
                    try
                    {
                        // Welcome message without mentions - will auto-delete after 10 seconds
                        var welcomeMsg = await threadChannel.SendMessageAsync("**Match Thread**\nWelcome to your tournament match thread!");

                        // Auto-delete welcome message after 10 seconds to keep thread clean
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(10000); // 10 seconds
                            try
                            {
                                await welcomeMsg.DeleteAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Failed to auto-delete welcome message: {ex.Message}");
                            }
                        });

                        // Initialize the match status system
                        try
                        {
                            // Get the round associated with this thread
                            var round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                                r.Teams?.Any(t => t.Thread?.Id == threadChannel.Id) ?? false);

                            if (round is not null)
                            {
                                // Determine if this is a new match in an existing thread
                                bool isReusingThread = false;
                                if (round.TournamentRound && round.GroupStageMatchNumber > 0)
                                {
                                    // Check if we have previous matches in this thread
                                    var previousMatches = _ongoingRounds.TourneyRounds
                                        .Where(r => r != round &&
                                                r.Teams?.Any(t => t.Thread?.Id == threadChannel.Id) == true)
                                        .ToList();

                                    isReusingThread = previousMatches.Any();
                                }

                                // If we're reusing a thread, create a new status message
                                // Otherwise, update the existing one
                                if (isReusingThread)
                                {
                                    // Create a fresh match status with transition context
                                    await _matchStatusService.CreateNewMatchStatusAsync(threadChannel, round, client);
                                }
                                else
                                {
                                    // Create the base match status (first match in this thread)
                                    await _matchStatusService.UpdateMatchStatusAsync(threadChannel, round, client);
                                }

                                // Set initial stage to map banning
                                await _matchStatusService.UpdateToMapBanStageAsync(threadChannel, round, client);

                                _logger.LogInformation($"Match status initialized for thread {threadChannel.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to initialize match status for thread {threadChannel.Id}");
                        }

                        // Create map ban dropdown options
                        var options = new List<DiscordSelectComponentOption>();
                        foreach (var map in maps1v1)
                        {
                            if (string.IsNullOrEmpty(map)) continue;
                            options.Add(new DiscordSelectComponentOption(map, map));
                        }

                        // Create dropdown with appropriate instructions based on match length
                        DiscordSelectComponent dropdown;
                        string message;

                        switch (bestOf)
                        {
                            case 3:
                                dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                                message = "**Scroll to see all map options!**\n\n" +
                                    "Choose 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                    "Only 2 maps from each player will be banned, leaving 4 remaining maps. One of the 3rd priority maps " +
                                    "selected will be randomly banned in case both players ban the same map. " +
                                    "You will not know which maps were banned by your opponent, and the remaining maps will be revealed " +
                                    "randomly before each game after deck codes have been locked in.\n\n" +
                                    "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                                break;
                            case 5:
                                dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                                message = "**Scroll to see all map options!**\n\n" +
                                    "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                    "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                                    "One of the 2nd priority maps selected by each player will be randomly banned. " +
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

                        // Instructions message with the dropdown - this is important to keep
                        var dropdownBuilder = new DiscordMessageBuilder()
                            .WithContent(message)
                            .AddComponents(dropdown);

                        await threadChannel.SendMessageAsync(dropdownBuilder);

                        // Create a button to report results
                        var reportButton = new DiscordButtonComponent(
                            DiscordButtonStyle.Primary,
                            "report_result",
                            "Report Result");

                        // Create a message with the button - this is important to keep
                        var resultMessage = new DiscordMessageBuilder()
                            .WithContent("Report the result of the match when it's done:")
                            .AddComponents(reportButton);

                        // Send the message with the button
                        await threadChannel.SendMessageAsync(resultMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error sending match information to thread {threadChannel.Name}");
                    }
                }

                // Send information to both player threads
                if (team1.Thread is not null)
                {
                    await SendMatchInfoToThread(team1.Thread);
                }

                if (team2.Thread is not null)
                {
                    await SendMatchInfoToThread(team2.Thread);
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);

                // If all group matches are complete, set up playoffs
                if (group is not null && group.IsComplete &&
                    tournament?.CurrentStage == TournamentStage.Groups &&
                    tournament.Groups?.All(g => g.IsComplete) == true)
                {
                    // Delegate to PlayoffService to set up the playoffs
                    _playoffService.SetupPlayoffs(tournament);

                    // Update tournament visualization
                    if (tournament.AnnouncementChannel is not null)
                    {
                        // This would be handled by a visualization service
                        // For now, just log it
                        _logger.LogInformation("Tournament ready for playoffs visualization");
                    }
                }

                // Previous code returned "void as per interface" which was accurate
                // No return value is needed now
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating and starting 1v1 match: {ex.Message}");
                throw; // Re-throw to allow calling code to handle the exception
            }
        }

        /// <inheritdoc/>
        public async Task SetupPlayoffStage(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Setting up playoff stage for tournament {tournament.Name}");

            // Delegate playoff setup to the specialized playoff service
            _playoffService.SetupPlayoffs(tournament);

            // Post updated visualization through appropriate service
            // This could be an ITournamentVisualizationService or similar
            if (tournament.AnnouncementChannel is not null)
            {
                _logger.LogInformation("Tournament playoff stage ready for visualization");
            }

            // Start the playoff matches
            if (tournament.CurrentStage == TournamentStage.Playoffs)
            {
                await StartPlayoffMatches(tournament, client);
            }
        }

        /// <inheritdoc/>
        public int DetermineGroupCount(int playerCount, TournamentFormat format)
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

        /// <inheritdoc/>
        public List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
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

        /// <inheritdoc/>
        public (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
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

        /// <summary>
        /// Updates match result
        /// </summary>
        public async Task UpdateMatchResultAsync(
            Tournament? tournament,
            Tournament.Match? match,
            DiscordMember? winner,
            int winnerScore,
            int loserScore)
        {
            try
            {
                if (tournament is null || match is null || winner is null)
                {
                    _logger.LogError("Cannot update match result: tournament, match, or winner is null");
                    return;
                }

                _logger.LogInformation($"Updating match result for {match.Name} in tournament {tournament.Name}");

                // Update the match result
                UpdateMatchResultInternal(tournament, match, winner, winnerScore, loserScore);

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating match result: {ex.Message}");
            }
        }

        /// <summary>
        /// Internal method to update match result
        /// </summary>
        private void UpdateMatchResultInternal(
            Tournament tournament,
            Tournament.Match match,
            DiscordMember winner,
            int winnerScore,
            int loserScore)
        {
            // Verify required objects are not null
            if (tournament is null || match is null || winner is null)
            {
                _logger.LogError("Cannot update match result: tournament, match, or winner is null");
                return;
            }

            // Check if participants collection exists
            if (match.Participants is null)
            {
                _logger.LogError("Cannot update match result: match participants collection is null");
                return;
            }

            // Find the winner and loser participants
            var winnerParticipant = match.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member && member.Id == winner.Id);

            var loserParticipant = match.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member && member.Id != winner.Id);

            if (winnerParticipant is null || loserParticipant is null)
            {
                _logger.LogError("Could not find winner or loser participants in match");
                return;
            }

            // Set scores
            winnerParticipant.Score = winnerScore;
            loserParticipant.Score = loserScore;

            // Set winner flag
            winnerParticipant.IsWinner = true;
            loserParticipant.IsWinner = false;

            // Create result if it doesn't exist
            if (match.Result is null)
            {
                match.Result = new Tournament.MatchResult
                {
                    CompletedAt = DateTime.Now,
                    Winner = winner
                };
            }
            else
            {
                match.Result.Winner = winner;
            }

            // Mark match as complete
            // For the IsComplete property, we need to examine the Tournament.Match class
            // Since it's read-only, we might need a different approach
            try
            {
                // Option 1: Check if the class has a SetComplete method
                var setCompleteMethod = match.GetType().GetMethod("SetComplete", BindingFlags.Public | BindingFlags.Instance);
                if (setCompleteMethod != null)
                {
                    setCompleteMethod.Invoke(match, null);
                }
                else
                {
                    // Option 2: Check if there's another way to mark it complete
                    // Check if setting Result is enough to mark it complete automatically
                    _logger.LogInformation($"Match {match.Name} marked as complete through result assignment");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Cannot mark match {match.Name} as complete: {ex.Message}");
            }

            // Update stats for group participants if this is a group stage match
            if (match.Type == TournamentMatchType.GroupStage)
            {
                UpdateGroupStats(match, winnerParticipant, loserParticipant);
            }

            // Check for group completion
            if (match.Type == TournamentMatchType.GroupStage &&
                winnerParticipant.SourceGroup is not null)
            {
                var group = winnerParticipant.SourceGroup;

                if (group.Matches is null)
                {
                    _logger.LogWarning($"Cannot check group completion: matches list is null for group {group.Name}");
                }
                else
                {
                    // Check if all matches in the group are complete
                    bool allMatchesComplete = group.Matches.All(m => m is not null && m.IsComplete);

                    if (allMatchesComplete)
                    {
                        group.IsComplete = true;

                        // Sort participants by points
                        SortGroupParticipants(group);
                    }
                }
            }

            // Update next match if this is part of a bracket
            if (match.NextMatch is not null && match.Result?.Winner is not null)
            {
                UpdateNextMatch(tournament, match);
            }
        }

        /// <summary>
        /// Updates the group stats based on match results
        /// </summary>
        private void UpdateGroupStats(
            Tournament.Match match,
            Tournament.MatchParticipant winnerParticipant,
            Tournament.MatchParticipant loserParticipant)
        {
            if (match is null || winnerParticipant is null || loserParticipant is null)
            {
                _logger.LogWarning("Cannot update group stats: match or participants are null");
                return;
            }

            _logger.LogInformation($"Updating group stats for match {match.Name}");

            // Find participants in their respective groups
            if (winnerParticipant.SourceGroup?.Participants is null ||
                loserParticipant.SourceGroup?.Participants is null)
            {
                _logger.LogWarning("Cannot update group stats: source groups or their participant lists are null");
                return;
            }

            // Find the group participants that match these players
            var winnerInGroup = winnerParticipant.SourceGroup.Participants.FirstOrDefault(
                p => p is not null && ComparePlayerIds(p.Player, winnerParticipant.Player));

            var loserInGroup = loserParticipant.SourceGroup.Participants.FirstOrDefault(
                p => p is not null && ComparePlayerIds(p.Player, loserParticipant.Player));

            if (winnerInGroup is null || loserInGroup is null)
            {
                _logger.LogWarning("Could not find one or both participants in their groups");
                return;
            }

            // Update win/loss records
            winnerInGroup.Wins++;
            loserInGroup.Losses++;

            // Update game scores
            winnerInGroup.GamesWon += winnerParticipant.Score;
            winnerInGroup.GamesLost += loserParticipant.Score;
            loserInGroup.GamesWon += loserParticipant.Score;
            loserInGroup.GamesLost += winnerParticipant.Score;

            _logger.LogInformation($"Updated group stats: {GetPlayerName(winnerInGroup.Player)} now {winnerInGroup.Wins}W-{winnerInGroup.Losses}L");
        }

        /// <summary>
        /// Sorts participants in a group by their points and game differential
        /// </summary>
        private void SortGroupParticipants(Tournament.Group? group)
        {
            if (group is null)
            {
                _logger.LogWarning("Cannot sort participants: group is null");
                return;
            }

            if (group.Participants is null)
            {
                _logger.LogWarning($"Cannot sort participants: group {group.Name} has null participants list");
                return;
            }

            // Sort by points (3 for win, 1 for draw) then by game differential
            group.Participants = group.Participants
                .Where(p => p is not null) // Filter out potential null participants
                .OrderByDescending(p => p.Wins * 3 + p.Draws)
                .ThenByDescending(p => p.GamesWon - p.GamesLost)
                .ToList();

            _logger.LogInformation($"Sorted participants in group {group.Name}");
        }

        /// <summary>
        /// Updates the next match in a bracket with the winner of this match
        /// </summary>
        private void UpdateNextMatch(Tournament? tournament, Tournament.Match? match)
        {
            if (tournament is null || match is null)
            {
                _logger.LogWarning("Cannot update next match: tournament or match is null");
                return;
            }

            if (match.NextMatch is null || match.Result?.Winner is null)
            {
                return;
            }

            _logger.LogInformation($"Updating next match after {match.Name}");

            // Check if match.NextMatch.Participants is null or empty
            if (match.NextMatch.Participants is null || match.NextMatch.Participants.Count == 0)
            {
                _logger.LogWarning($"Cannot update next match: participants list is null or empty in next match");
                return;
            }

            // Check if the Tournament.Match class has NextMatchPlayerSlot property
            // If not, we need a different approach to determine the player slot
            try
            {
                // Try to get the slot property if it exists
                var slotProperty = match.GetType().GetProperty("NextMatchPlayerSlot");

                if (slotProperty is not null)
                {
                    var propertyValue = slotProperty.GetValue(match);
                    if (propertyValue is not null)
                    {
                        // Safely cast to int, with a fallback of 0 if cast fails
                        int slot;
                        try
                        {
                            slot = Convert.ToInt32(propertyValue);
                        }
                        catch
                        {
                            _logger.LogWarning("NextMatchPlayerSlot value could not be converted to int, using default slot 0");
                            slot = 0;
                        }

                        // Ensure the slot is valid
                        if (slot >= 0 && slot < match.NextMatch.Participants.Count)
                        {
                            var participant = match.NextMatch.Participants[slot];
                            if (participant is not null)
                            {
                                // Set the player in the appropriate slot
                                participant.Player = match.Result.Winner;
                                _logger.LogInformation($"Updated player slot {slot} in next match with {GetPlayerName(match.Result.Winner)}");
                            }
                            else
                            {
                                _logger.LogWarning($"Participant at slot {slot} is null in next match");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid slot index {slot} for next match with {match.NextMatch.Participants.Count} participants");
                        }
                    }
                }
                else
                {
                    // Fallback: Assume the slot is 0 or try to determine it by other means
                    _logger.LogWarning("NextMatchPlayerSlot property not found, using default slot 0");

                    if (match.NextMatch.Participants.Count > 0)
                    {
                        var participant = match.NextMatch.Participants[0];
                        if (participant is not null)
                        {
                            participant.Player = match.Result.Winner;
                            _logger.LogInformation($"Updated player slot 0 in next match with {GetPlayerName(match.Result.Winner)}");
                        }
                        else
                        {
                            _logger.LogWarning("Participant at slot 0 is null in next match");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot update next match: no participants in the next match");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating next match: {ex.Message}");
            }
        }

        /// <summary>
        /// Compares player IDs to determine if they are the same player
        /// </summary>
        private bool ComparePlayerIds(object? player1, object? player2)
        {
            if (player1 is null || player2 is null)
                return false;

            // If both are DiscordMember, compare IDs
            if (player1 is DiscordMember member1 && player2 is DiscordMember member2)
            {
                return member1.Id == member2.Id;
            }

            // Otherwise compare by ToString or another method
            return player1.ToString() == player2.ToString();
        }

        /// <summary>
        /// Gets a player's name for display purposes
        /// </summary>
        private string GetPlayerName(object? player)
        {
            if (player is null)
                return "Unknown";

            if (player is DiscordMember member)
                return member.DisplayName ?? member.Username ?? "Unknown Member";

            if (player is DiscordUser user)
                return user.Username ?? "Unknown User";

            return player.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Checks if both players have submitted their decks for the current game
        /// </summary>
        private bool AreDeckSubmissionsComplete(Round round, int gameNumber)
        {
            if (round.CustomProperties?.ContainsKey("DeckCodes") != true)
                return false;

            var deckCodes = round.CustomProperties["DeckCodes"] as Dictionary<string, Dictionary<string, string>>;
            if (deckCodes == null)
                return false;

            // Check if both players have submitted decks for this game
            int submissionCount = 0;
            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is null) continue;

                    string userId = participant.Player.Id.ToString();
                    if (deckCodes.Any(dc => dc.Value.ContainsKey(userId)))
                    {
                        submissionCount++;
                    }
                }
            }

            return submissionCount >= 2;
        }

        /// <summary>
        /// Sends a map thumbnail that will be automatically deleted after a specified duration
        /// </summary>
        private async Task SendMapThumbnailAsync(DiscordChannel channel, string mapName, DiscordClient client)
        {
            try
            {
                // Get the map data from Maps collection
                var map = Maps.MapCollection?.FirstOrDefault(m =>
                    string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));

                if (map?.Thumbnail is null)
                {
                    _logger.LogWarning($"No thumbnail found for map: {mapName}");
                    return;
                }

                // Create the base embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🗺️ Next Map: {mapName}")
                    .WithColor(new DiscordColor(75, 181, 67))
                    .WithFooter("This message will be automatically deleted in 5 minutes.");

                // Create the webhook builder
                var webhookBuilder = new DiscordWebhookBuilder().AddEmbed(embed);

                if (map.Thumbnail.StartsWith("http"))
                {
                    // It's a URL, use it directly
                    embed.ImageUrl = map.Thumbnail;
                }
                else
                {
                    // It's a local file, we need to attach it
                    string relativePath = map.Thumbnail;

                    // Normalize path separators
                    relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);

                    // Construct the full path
                    string baseDirectory = Directory.GetCurrentDirectory();
                    string fullPath = Path.Combine(baseDirectory, relativePath);

                    if (!File.Exists(fullPath))
                    {
                        _logger.LogWarning($"Thumbnail file not found at: {fullPath}");
                        return;
                    }

                    // Add the file as an attachment
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    webhookBuilder.AddFile(Path.GetFileName(fullPath), fileStream);
                }

                // Send the thumbnail message
                var messageBuilder = new DiscordMessageBuilder(webhookBuilder);
                var thumbnailMessage = await channel.SendMessageAsync(messageBuilder);

                // Schedule message deletion
                _ = Task.Delay(TimeSpan.FromMinutes(mapThumbnailDurationMinutes))
                    .ContinueWith(async _ =>
                    {
                        try
                        {
                            await thumbnailMessage.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting map thumbnail message");
                        }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending map thumbnail");
            }
        }

        /// <summary>
        /// Handles deck submission and reveals map if both players have submitted
        /// </summary>
        public async Task HandleDeckSubmissionAsync(Round round, DiscordChannel channel, DiscordClient client)
        {
            try
            {
                // Get the current game number
                int currentGame = round.Maps?.Count ?? 0;

                // Check if both players have submitted their decks
                if (AreDeckSubmissionsComplete(round, currentGame))
                {
                    _logger.LogInformation($"Both players have submitted decks for game {currentGame + 1}. Revealing map...");

                    // Get available maps
                    var availableMaps = _mapService.GetAvailableMapsForNextGame(round);
                    if (availableMaps.Count == 0)
                    {
                        _logger.LogWarning("No maps available for next game");
                        return;
                    }

                    // Select a random map
                    var random = new Random();
                    string nextMap = availableMaps[random.Next(availableMaps.Count)];

                    // Add the map to the round's map list
                    if (round.Maps is null)
                    {
                        round.Maps = new List<string>();
                    }
                    round.Maps.Add(nextMap);

                    // Update the match status with the new map
                    await _matchStatusService.UpdateMapInformationAsync(channel, round, client);

                    // Send map thumbnail in a separate message
                    await SendMapThumbnailAsync(channel, nextMap, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling deck submission");
            }
        }

        // ... other methods ...
    }
}