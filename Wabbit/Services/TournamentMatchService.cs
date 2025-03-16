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
            _ongoingRounds = ongoingRounds ?? throw new ArgumentNullException(nameof(ongoingRounds));
            _tournamentGameService = tournamentGameService ?? throw new ArgumentNullException(nameof(tournamentGameService));
            _playoffService = playoffService ?? throw new ArgumentNullException(nameof(playoffService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _matchStatusService = matchStatusService ?? throw new ArgumentNullException(nameof(matchStatusService));
        }

        /// <inheritdoc/>
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

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
            bool allPlayoffMatchesComplete = tournament.PlayoffMatches.Count > 0 &&
                tournament.PlayoffMatches.All(m => m.IsComplete);

            if (tournament.CurrentStage == TournamentStage.Playoffs && allPlayoffMatchesComplete)
            {
                tournament.CurrentStage = TournamentStage.Complete;
                tournament.IsComplete = true;
                _logger.LogInformation($"Tournament {tournament.Name} is now complete");
            }
        }

        /// <inheritdoc/>
        public async Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember player1,
            DiscordMember player2,
            DiscordClient client,
            int matchLength,
            Tournament.Match? existingMatch = null)
        {
            try
            {
                _logger.LogInformation($"Creating 1v1 match: {player1.DisplayName} vs {player2.DisplayName}");

                // Validate parameters
                if (tournament is null)
                    throw new ArgumentNullException(nameof(tournament), "Tournament cannot be null");

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
                        // Set match type based on context
                        Type = DetermineMatchType(group, existingMatch),
                        // Group stage is Bo1, playoffs and tiebreakers are Bo3
                        BestOf = ShouldUseBestOfThree(group, existingMatch) ? 3 : 1,
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
                        // Initialize CustomProperties and TeamThreads if they don't exist
                        tournament.CustomProperties ??= new Dictionary<string, object>();
                        if (!tournament.CustomProperties.ContainsKey("TeamThreads"))
                        {
                            tournament.CustomProperties["TeamThreads"] = new Dictionary<ulong, ulong>();
                        }

                        var teamThreads = tournament.CustomProperties["TeamThreads"] as Dictionary<ulong, ulong>;

                        // Try to find existing threads for the players
                        if (teamThreads is not null)
                        {
                            if (player1 is not null && teamThreads.ContainsKey(player1.Id))
                            {
                                try
                                {
                                    var threadChannel = await guild.GetChannelAsync(teamThreads[player1.Id]);
                                    if (threadChannel is not null)
                                    {
                                        player1Thread = threadChannel as DiscordThreadChannel;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Could not find thread for player1: {ex.Message}");
                                }
                            }

                            if (player2 is not null && teamThreads.ContainsKey(player2.Id))
                            {
                                try
                                {
                                    var threadChannel = await guild.GetChannelAsync(teamThreads[player2.Id]);
                                    if (threadChannel is not null)
                                    {
                                        player2Thread = threadChannel as DiscordThreadChannel;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Could not find thread for player2: {ex.Message}");
                                }
                            }
                        }

                        // Prefer player1's thread for consistency
                        if (player1Thread is not null)
                        {
                            thread = player1Thread;
                        }
                        else if (player2Thread is not null)
                        {
                            thread = player2Thread;
                        }
                    }

                    // If we don't have a thread yet, create a new one
                    if (thread is null)
                    {
                        // Get the bot channel from config
                        var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s.ServerId == guild.Id);
                        DiscordChannel? tournamentChannel = null;

                        if (server?.BotChannelId != null)
                        {
                            try
                            {
                                tournamentChannel = await guild.GetChannelAsync(server.BotChannelId.Value);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error getting bot channel with ID {server.BotChannelId.Value}");
                            }
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

                            // Store the new thread ID in TeamThreads
                            if (tournament is not null && thread is not null)
                            {
                                tournament.CustomProperties ??= new Dictionary<string, object>();
                                tournament.CustomProperties["TeamThreads"] ??= new Dictionary<ulong, ulong>();
                                var teamThreads = tournament.CustomProperties["TeamThreads"] as Dictionary<ulong, ulong>;
                                if (teamThreads is not null)
                                {
                                    if (player1 is not null && !teamThreads.ContainsKey(player1.Id))
                                    {
                                        teamThreads[player1.Id] = thread.Id;
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogError("Could not find bot channel. Please configure BotChannelId in the server settings.");
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
                    Length = matchLength,
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
                    round.CustomProperties["MatchLength"] = matchLength;
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
                                // Always create a new match status for each match
                                // This preserves match history in the thread
                                await _matchStatusService.CreateNewMatchStatusAsync(threadChannel, round, client);

                                // Set initial stage to map banning
                                await _matchStatusService.UpdateToMapBanStageAsync(threadChannel, round, client);

                                _logger.LogInformation($"Match status initialized for thread {threadChannel.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to initialize match status for thread {threadChannel.Id}");
                        }
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
                    await _playoffService.SetupPlayoffsAsync(tournament, client);

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

        /// <summary>
        /// Determines the match type based on context
        /// </summary>
        private TournamentMatchType DetermineMatchType(Tournament.Group? group, Tournament.Match? existingMatch)
        {
            if (existingMatch?.IsTiebreakerMatch == true)
            {
                return TournamentMatchType.GroupStageTiebreaker;
            }
            return group is not null ? TournamentMatchType.GroupStage : TournamentMatchType.Quarterfinal;
        }

        /// <summary>
        /// Determines if the match should use Best-of-3 format
        /// </summary>
        private bool ShouldUseBestOfThree(Tournament.Group? group, Tournament.Match? existingMatch)
        {
            // Tiebreaker matches are always Bo3
            if (existingMatch?.IsTiebreakerMatch == true)
            {
                return true;
            }

            // Group stage matches are Bo1, playoffs are Bo3
            return group is null || existingMatch?.Type == TournamentMatchType.GroupStageTiebreaker;
        }

        /// <inheritdoc/>
        public async Task SetupPlayoffStage(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Setting up playoff stage for tournament {tournament.Name}");

            // Delegate playoff setup to the specialized playoff service
            await _playoffService.SetupPlayoffsAsync(tournament, client);

            // Post updated visualization through appropriate service
            // This could be an ITournamentVisualizationService or similar
            if (tournament.AnnouncementChannel is not null)
            {
                _logger.LogInformation("Tournament playoff stage ready for visualization");
            }

            // Start the playoff matches
            if (tournament.CurrentStage == TournamentStage.Playoffs)
            {
                await _playoffService.StartPlayoffMatchesAsync(tournament, client);
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
                if (tournament is null)
                    throw new ArgumentNullException(nameof(tournament), "Tournament cannot be null");
                if (match is null)
                    throw new ArgumentNullException(nameof(match), "Match cannot be null");
                if (winner is null)
                    throw new ArgumentNullException(nameof(winner), "Winner cannot be null");

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
            if (tournament is null)
                throw new ArgumentNullException(nameof(tournament), "Tournament cannot be null");
            if (match is null)
                throw new ArgumentNullException(nameof(match), "Match cannot be null");
            if (winner is null)
                throw new ArgumentNullException(nameof(winner), "Winner cannot be null");

            // Check if participants collection exists
            if (match.Participants == null)
            {
                _logger.LogError("Cannot update match result: match participants collection is null");
                return;
            }

            // Find the winner and loser participants
            var winnerParticipant = match.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member && member.Id == winner.Id);

            var loserParticipant = match.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member && member.Id != winner.Id);

            if (winnerParticipant == null || loserParticipant == null)
            {
                _logger.LogError("Could not find winner or loser participants in match");
                return;
            }

            // Set scores
            winnerParticipant.Score = winnerScore;
            loserParticipant.Score = loserScore;

            // Create or update result
            match.Result ??= new Tournament.MatchResult();
            match.Result.Winner = winner;
            match.Result.WinnerScore = winnerScore;
            match.Result.LoserScore = loserScore;
            match.Result.CompletedAt = DateTime.Now;

            // Update group stats if this is a group stage match
            if (match.Type == TournamentMatchType.GroupStage)
            {
                UpdateGroupStats(match, winnerParticipant, loserParticipant);
            }

            // Check for group completion
            if (match.Type == TournamentMatchType.GroupStage &&
                winnerParticipant.SourceGroup != null)
            {
                var group = winnerParticipant.SourceGroup;

                if (group.Matches == null)
                {
                    _logger.LogWarning($"Cannot check group completion: matches list is null for group {group.Name}");
                }
                else
                {
                    // Check if all matches in the group are complete
                    bool allMatchesComplete = group.Matches.All(m => m != null && m.Result != null);

                    if (allMatchesComplete)
                    {
                        group.IsComplete = true;
                        SortGroupParticipants(group);
                    }
                }
            }

            // Update next match if this is part of a bracket
            if (match.NextMatch != null && match.Result?.Winner != null)
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
            if (match is null)
                throw new ArgumentNullException(nameof(match));
            if (winnerParticipant is null)
                throw new ArgumentNullException(nameof(winnerParticipant));
            if (loserParticipant is null)
                throw new ArgumentNullException(nameof(loserParticipant));

            _logger.LogInformation($"Updating group stats for match {match.Name}");

            // Find participants in their respective groups
            if (winnerParticipant.SourceGroup?.Participants == null ||
                loserParticipant.SourceGroup?.Participants == null)
            {
                _logger.LogWarning("Cannot update group stats: source groups or their participant lists are null");
                return;
            }

            var winnerInGroup = winnerParticipant.SourceGroup.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member &&
                member.Id == (winnerParticipant.Player as DiscordMember)?.Id);

            var loserInGroup = loserParticipant.SourceGroup.Participants.FirstOrDefault(p =>
                p?.Player is DiscordMember member &&
                member.Id == (loserParticipant.Player as DiscordMember)?.Id);

            if (winnerInGroup == null || loserInGroup == null)
            {
                _logger.LogError("Could not find participants in their groups");
                return;
            }

            // Update stats
            winnerInGroup.Wins++;
            loserInGroup.Losses++;
            winnerInGroup.GamesWon += match.Result?.WinnerScore ?? 0;
            loserInGroup.GamesWon += match.Result?.LoserScore ?? 0;
            winnerInGroup.GamesLost += match.Result?.LoserScore ?? 0;
            loserInGroup.GamesLost += match.Result?.WinnerScore ?? 0;
        }

        /// <summary>
        /// Sorts participants in a group by their points and game differential
        /// </summary>
        private void SortGroupParticipants(Tournament.Group group)
        {
            if (group is null)
                throw new ArgumentNullException(nameof(group));
            if (group.Participants == null)
            {
                _logger.LogWarning($"Cannot sort participants: participant list is null for group {group.Name}");
                return;
            }

            group.Participants = group.Participants
                .OrderByDescending(p => p?.Points ?? 0)
                .ThenByDescending(p => p?.GamesWon ?? 0)
                .ThenByDescending(p => (p?.GamesWon ?? 0) - (p?.GamesLost ?? 0))
                .ToList();

            // Update positions
            for (int i = 0; i < group.Participants.Count; i++)
            {
                if (group.Participants[i] != null)
                {
                    group.Participants[i].Position = i + 1;
                }
            }
        }

        /// <summary>
        /// Updates the next match in a bracket with the winner of this match
        /// </summary>
        private void UpdateNextMatch(Tournament tournament, Tournament.Match match)
        {
            if (tournament is null)
                throw new ArgumentNullException(nameof(tournament));
            if (match is null)
                throw new ArgumentNullException(nameof(match));
            if (match.NextMatch == null)
            {
                _logger.LogWarning($"Cannot update next match: no next match defined for {match.Name}");
                return;
            }
            if (match.Result?.Winner == null)
            {
                _logger.LogWarning($"Cannot update next match: no winner defined for {match.Name}");
                return;
            }

            var nextMatch = match.NextMatch;
            var winner = match.Result.Winner;

            // Find the slot in the next match where this winner should go
            var slot = nextMatch.Participants?.FirstOrDefault(p => p?.SourceMatch == match);
            if (slot == null)
            {
                _logger.LogError($"Could not find slot in next match {nextMatch.Name} for winner of {match.Name}");
                return;
            }

            // Update the slot with the winner
            slot.Player = winner;
            slot.SourceMatch = match;
            slot.Score = 0; // Reset score for new match

            _logger.LogInformation($"Updated next match {nextMatch.Name} with winner {(winner as DiscordMember)?.Username ?? "Unknown"} from {match.Name}");
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

        /// <inheritdoc/>
        public async Task ArchiveMatchThreadsAsync(
            Tournament.Match match,
            DiscordClient client,
            TimeSpan? archiveDuration = null)
        {
            try
            {
                _logger.LogInformation($"Archiving threads for match {match.Name}");

                // Convert TimeSpan to DiscordAutoArchiveDuration
                var archiveDurationEnum = archiveDuration?.TotalMinutes switch
                {
                    <= 60 => DiscordAutoArchiveDuration.Hour,
                    <= 1440 => DiscordAutoArchiveDuration.Day,
                    <= 10080 => DiscordAutoArchiveDuration.Week,
                    _ => DiscordAutoArchiveDuration.Week // Default to a week for longer durations
                };

                // Find and archive team threads
                if (match.LinkedRound?.Teams != null)
                {
                    foreach (var team in match.LinkedRound.Teams)
                    {
                        try
                        {
                            if (team.Thread is not null)
                            {
                                await team.Thread.ModifyAsync(props =>
                                {
                                    props.Locked = true;
                                    props.AutoArchiveDuration = archiveDurationEnum;
                                });
                                _logger.LogInformation($"Archived team thread {team.Thread.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error archiving team thread: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error archiving match threads: {ex.Message}");
            }
        }

        // ... other methods ...
    }
}