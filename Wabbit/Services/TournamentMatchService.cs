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
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentMapService _mapService;
        private readonly ILogger<TournamentMatchService> _logger;
        private readonly IMatchStatusService _matchStatusService;
        private readonly ITournamentMatchOperationsService _matchOperations;
        private readonly ITournamentStateValidator _stateValidator;
        private readonly ITournamentScoreManager _scoreManager;

        private const int autoDeleteSeconds = 30;
        private const int mapThumbnailDurationMinutes = 5;

        public TournamentMatchService(
            OngoingRounds ongoingRounds,
            ITournamentStateService stateService,
            ITournamentMapService mapService,
            ILogger<TournamentMatchService> logger,
            IMatchStatusService matchStatusService,
            ITournamentMatchOperationsService matchOperations,
            ITournamentStateValidator stateValidator,
            ITournamentScoreManager scoreManager)
        {
            _ongoingRounds = ongoingRounds ?? throw new ArgumentNullException(nameof(ongoingRounds));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _matchStatusService = matchStatusService ?? throw new ArgumentNullException(nameof(matchStatusService));
            _matchOperations = matchOperations ?? throw new ArgumentNullException(nameof(matchOperations));
            _stateValidator = stateValidator ?? throw new ArgumentNullException(nameof(stateValidator));
            _scoreManager = scoreManager ?? throw new ArgumentNullException(nameof(scoreManager));
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
                if (!_matchOperations.ValidateMatchCreation(tournament, player1, player2))
                {
                    _logger.LogError("Match creation validation failed");
                    return;
                }

                // Check if players are already in active matches
                bool player1InActiveMatch = false;
                bool player2InActiveMatch = false;

                // Check ongoing rounds first
                foreach (var ongoingRound in _ongoingRounds.TourneyRounds)
                {
                    if (ongoingRound.IsCompleted) continue;

                    foreach (var team in ongoingRound.Teams ?? Enumerable.Empty<Round.Team>())
                    {
                        foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                        {
                            if (participant?.Player is DiscordMember member)
                            {
                                if (member.Id == player1?.Id)
                                {
                                    player1InActiveMatch = true;
                                    _logger.LogWarning($"Player {player1?.DisplayName ?? "Unknown"} (ID: {player1?.Id ?? 0}) is already in an active match");
                                }

                                if (member.Id == player2?.Id)
                                {
                                    player2InActiveMatch = true;
                                    _logger.LogWarning($"Player {player2?.DisplayName ?? "Unknown"} (ID: {player2?.Id ?? 0}) is already in an active match");
                                }
                            }
                        }
                    }
                }

                // Check tournament matches that haven't been completed yet
                if (tournament != null)
                {
                    // Check group stage matches
                    if (group != null && group.Matches != null)
                    {
                        foreach (var groupMatch in group.Matches)
                        {
                            if (groupMatch.IsComplete || groupMatch == existingMatch) continue;

                            foreach (var participant in groupMatch.Participants ?? Enumerable.Empty<Tournament.MatchParticipant>())
                            {
                                if (participant?.Player is DiscordMember member)
                                {
                                    if (member.Id == player1?.Id)
                                    {
                                        player1InActiveMatch = true;
                                    }

                                    if (member.Id == player2?.Id)
                                    {
                                        player2InActiveMatch = true;
                                    }
                                }
                            }
                        }
                    }

                    // Check playoff matches
                    if (tournament.PlayoffMatches != null)
                    {
                        foreach (var playoffMatch in tournament.PlayoffMatches)
                        {
                            if (playoffMatch.IsComplete || playoffMatch == existingMatch) continue;

                            foreach (var participant in playoffMatch.Participants ?? Enumerable.Empty<Tournament.MatchParticipant>())
                            {
                                if (participant?.Player is DiscordMember member)
                                {
                                    if (member.Id == player1?.Id)
                                    {
                                        player1InActiveMatch = true;
                                    }

                                    if (member.Id == player2?.Id)
                                    {
                                        player2InActiveMatch = true;
                                    }
                                }
                            }
                        }
                    }
                }

                // Log and exit if players are already in active matches
                if (player1InActiveMatch || player2InActiveMatch)
                {
                    string errorMsg = "Cannot create match: ";
                    if (player1InActiveMatch && player2InActiveMatch)
                    {
                        errorMsg += $"Both {player1?.DisplayName ?? "Unknown"} and {player2?.DisplayName ?? "Unknown"} are already assigned to active matches";
                    }
                    else if (player1InActiveMatch)
                    {
                        errorMsg += $"{player1?.DisplayName ?? "Unknown"} is already assigned to an active match";
                    }
                    else
                    {
                        errorMsg += $"{player2?.DisplayName ?? "Unknown"} is already assigned to an active match";
                    }

                    _logger.LogError(errorMsg);
                    return;
                }

                Tournament.Match match;
                if (existingMatch != null)
                {
                    match = existingMatch;
                }
                else
                {
                    // Add null checks before accessing DisplayName
                    string matchName = $"{player1?.DisplayName ?? "Player 1"} vs {player2?.DisplayName ?? "Player 2"}";

                    // Add null checks before creating match
                    if (player1 is null || player2 is null)
                    {
                        _logger.LogError("Cannot create match: player1 or player2 is null");
                        return;
                    }

                    match = _matchOperations.CreateMatch(
                        matchName,
                        group != null ? TournamentMatchType.GroupStage : TournamentMatchType.Quarterfinal,
                        matchLength,
                        player1,
                        player2,
                        group);

                    // Make sure tournament is not null before accessing its properties
                    if (group == null && tournament != null)
                    {
                        tournament.PlayoffMatches ??= new List<Tournament.Match>();
                        tournament.PlayoffMatches.Add(match);
                    }
                }

                // Create the Round object
                var round = new Round
                {
                    Name = match.Name,
                    Length = matchLength,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    TournamentId = tournament?.Name,
                    MsgToDel = new List<DiscordMessage>(),
                    TournamentRound = true,
                    CustomProperties = new Dictionary<string, object>()
                };

                // Set group stage match information
                if (match.Type == TournamentMatchType.GroupStage && group is not null)
                {
                    // Calculate total matches per player in this group
                    int groupSize = group.Participants?.Count ?? 0;
                    int totalMatchesPerPlayer = Math.Max(0, groupSize - 1); // Each player plays against every other player once

                    // Find completed matches for player1
                    int completedMatches = 0;

                    // If there's an existing tournament, check for previous matches in this group
                    if (tournament != null && player1 is not null)
                    {
                        // Get all completed matches for this player in the group
                        completedMatches = group.Matches?
                            .Where(m => m != match) // Don't count current match
                            .Where(m => m.IsComplete)
                            .Count(m => m.Participants
                                .Any(p => (p.Player as DiscordMember)?.Id == player1.Id)) ?? 0;

                        // GroupMatchNumber should be completedMatches + 1 (i.e., next match number)
                        round.CustomProperties["GroupMatchNumber"] = completedMatches + 1;
                    }
                    else
                    {
                        // Default to first match if we can't determine
                        round.CustomProperties["GroupMatchNumber"] = 1;
                    }

                    round.CustomProperties["TotalGroupMatches"] = totalMatchesPerPlayer;
                }

                // Create team objects
                var team1 = new Round.Team
                {
                    Name = player1?.DisplayName ?? "Player 1",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player1 }
                    },
                    MapBans = new List<string>()
                };

                var team2 = new Round.Team
                {
                    Name = player2?.DisplayName ?? "Player 2",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player2 }
                    },
                    MapBans = new List<string>()
                };

                // Add teams to round
                round.Teams.Add(team1);
                round.Teams.Add(team2);

                // Set up metadata for the match
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

                // Add the round to ongoing rounds
                _ongoingRounds.TourneyRounds.Add(round);

                // Link the round to the match
                match.LinkedRound = round;

                // Get or create threads for teams
                if (tournament is null)
                {
                    _logger.LogError("Cannot manage team threads: tournament is null");
                    return;
                }

                await GetOrCreateTeamThreadsAsync(tournament, round.Teams, client);

                // Check if this is a subsequent match for the players
                bool isFirstMatch = true;
                if (group != null)
                {
                    isFirstMatch = (group.Matches?.Count(m =>
                        (m.Participants?.Any(p => (p.Player as DiscordMember)?.Id == player1?.Id) ?? false) ||
                        (m.Participants?.Any(p => (p.Player as DiscordMember)?.Id == player2?.Id) ?? false)) ?? 0) <= 1;
                }
                else if (tournament.PlayoffMatches != null)
                {
                    isFirstMatch = tournament.PlayoffMatches.Count(m =>
                        (m.Participants?.Any(p => (p.Player as DiscordMember)?.Id == player1?.Id) ?? false) ||
                        (m.Participants?.Any(p => (p.Player as DiscordMember)?.Id == player2?.Id) ?? false)) <= 1;
                }

                // Initialize the match status system
                try
                {
                    if (tournament?.AnnouncementChannel is null)
                    {
                        _logger.LogWarning($"No announcement channel found for tournament {tournament?.Name}");
                        return;
                    }

                    // For each team's thread
                    foreach (var team in round.Teams)
                    {
                        if (team.Thread is null)
                        {
                            _logger.LogWarning($"No thread found for team {team.Name}");
                            continue;
                        }

                        // If not the first match, add a separator before creating a new match status
                        if (!isFirstMatch)
                        {
                            int matchNumber = (int)(round.CustomProperties.ContainsKey("GroupMatchNumber") ?
                                round.CustomProperties["GroupMatchNumber"] : 1);
                            int totalMatches = (int)(round.CustomProperties.ContainsKey("TotalGroupMatches") ?
                                round.CustomProperties["TotalGroupMatches"] : 1);

                            // Get opponent name
                            string opponentName = team == round.Teams[0] ?
                                round.Teams[1].Name ?? "Opponent" :
                                round.Teams[0].Name ?? "Opponent";

                            await _matchStatusService.AddMatchSeparatorAsync(
                                team.Thread,
                                client,
                                matchNumber,
                                totalMatches,
                                opponentName);
                        }

                        // For existing threads with previous matches, ensure we create a new match status
                        // message rather than updating an old one if the previous match was completed
                        if (!isFirstMatch)
                        {
                            // Find the existing round for this team in this thread, if any
                            var existingRound = _ongoingRounds.TourneyRounds
                                .Where(r => r.TournamentId == tournament.Name)
                                .Where(r => r != round) // Not the current round
                                .Where(r => r.Teams.Any(t => t.Thread?.Id == team.Thread?.Id))
                                .OrderByDescending(r => r.CustomProperties.ContainsKey("GroupMatchNumber") ?
                                    Convert.ToInt32(r.CustomProperties["GroupMatchNumber"]) : 0)
                                .FirstOrDefault();

                            // If we found a previous round and it's completed, create a new status
                            if (existingRound != null && existingRound.IsCompleted)
                            {
                                await _matchStatusService.CreateNewMatchStatusAsync(team.Thread, round, client);
                            }
                            else
                            {
                                // Otherwise, check if there's an existing message
                                var existingStatus = await _matchStatusService.GetMatchStatusMessageAsync(team.Thread, client);

                                if (existingStatus == null)
                                {
                                    // If no existing message, create a new one
                                    await _matchStatusService.CreateNewMatchStatusAsync(team.Thread, round, client);
                                }
                                else
                                {
                                    // Update existing status
                                    await _matchStatusService.UpdateMatchStatusAsync(team.Thread, round, client);
                                }
                            }
                        }
                        else
                        {
                            // First match, always create a new status
                            await _matchStatusService.CreateNewMatchStatusAsync(team.Thread, round, client);
                        }

                        await _matchStatusService.UpdateToMapBanStageAsync(team.Thread, round, client);
                        _logger.LogInformation($"Match status initialized for match {match.Name} in thread for {team.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to initialize match status for match {match.Name}");
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating match");
                throw;
            }
        }

        /// <summary>
        /// Gets existing threads for team participants or creates new ones if they don't exist
        /// </summary>
        private async Task GetOrCreateTeamThreadsAsync(Tournament tournament, List<Round.Team> teams, DiscordClient client)
        {
            if (tournament?.AnnouncementChannel is null)
            {
                _logger.LogError("Cannot manage team threads: Announcement channel is null");
                return;
            }

            // Find existing rounds for the tournament
            var tournamentRounds = _ongoingRounds.TourneyRounds
                .Where(r => r.TournamentId == tournament.Name)
                .ToList();

            // Process each team
            foreach (var team in teams)
            {
                if (team.Participants?.Count == 0 || team.Participants?.All(p => p.Player is null) == true)
                {
                    _logger.LogWarning($"Team {team.Name} has no valid participants");
                    continue;
                }

                // Try to find an existing thread for the first participant
                DiscordThreadChannel? existingThread = null;

                if (team.Participants?.FirstOrDefault()?.Player is DiscordMember member)
                {
                    // Look through all rounds to find a thread for this participant
                    foreach (var round in tournamentRounds)
                    {
                        var existingTeam = round.Teams?.FirstOrDefault(t =>
                            t.Participants?.Any(p => p.Player is DiscordMember pm && pm.Id == member.Id) == true);

                        if (existingTeam?.Thread is not null)
                        {
                            existingThread = existingTeam.Thread;
                            _logger.LogInformation($"Found existing thread {existingThread.Name} for {team.Name}");
                            break;
                        }
                    }
                }

                // If no existing thread was found, create a new one
                if (existingThread is null)
                {
                    _logger.LogInformation($"Creating new thread for team {team.Name}");

                    try
                    {
                        var newThread = await DiscordUtilities.CreateThreadAsync(
                            tournament.AnnouncementChannel,
                            team.Name ?? "Team Thread",
                            _logger,
                            DiscordChannelType.PrivateThread,
                            DiscordAutoArchiveDuration.Day);

                        if (newThread is not null)
                        {
                            existingThread = newThread;
                        }
                        else
                        {
                            _logger.LogError($"Failed to create thread for team {team.Name}");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error creating thread for team {team.Name}");
                        continue;
                    }
                }

                // Assign the thread to the team
                team.Thread = existingThread;

                // Add all participants to the thread
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is not null)
                    {
                        try
                        {
                            await existingThread.AddThreadMemberAsync(participant.Player);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Could not add {participant.Player} to thread {existingThread.Name}");
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task UpdateMatchResultAsync(
            Tournament? tournament,
            Tournament.Match? match,
            DiscordMember? winner,
            int winnerScore,
            int loserScore)
        {
            if (tournament == null || match == null || winner is null)
            {
                _logger.LogError("Cannot update match result: one or more parameters are null");
                return;
            }

            await _matchOperations.UpdateMatchResultAsync(tournament, match, winner, winnerScore, loserScore);

            // Update group stats if this is a group stage match
            var winnerParticipant = match.Participants.FirstOrDefault(p =>
                (p.Player as DiscordMember)?.Id == winner.Id);

            if (match.Type == TournamentMatchType.GroupStage &&
                winnerParticipant?.SourceGroup != null)
            {
                _scoreManager.UpdateGroupScores(winnerParticipant.SourceGroup, match);
            }
        }

        /// <inheritdoc/>
        public async Task HandleMatchCompletion(
            Tournament tournament,
            Tournament.Match match,
            DiscordClient client)
        {
            try
            {
                // Only archive match threads if tournament is complete
                if (client != null && tournament.IsComplete)
                {
                    await ArchiveMatchThreadsAsync(match, client);
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling match completion for {match.Name}");
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
                if (match.LinkedRound?.Teams == null || !match.LinkedRound.Teams.Any())
                {
                    _logger.LogInformation($"No threads to archive for match {match.Name}");
                    return;
                }

                foreach (var team in match.LinkedRound.Teams)
                {
                    try
                    {
                        if (team.Thread is not null)
                        {
                            // Log that we're archiving this thread
                            await team.Thread.SendMessageAsync(
                                "🔒 **This tournament is now complete.** This thread will be archived but remain available for viewing match history.");

                            await team.Thread.ModifyAsync(props =>
                            {
                                props.IsArchived = true;
                                if (archiveDuration.HasValue)
                                {
                                    props.AutoArchiveDuration = archiveDuration.Value switch
                                    {
                                        var d when d.TotalMinutes <= 60 => DiscordAutoArchiveDuration.Hour,
                                        var d when d.TotalMinutes <= 1440 => DiscordAutoArchiveDuration.Day,
                                        _ => DiscordAutoArchiveDuration.Week
                                    };
                                }
                            });
                            _logger.LogInformation($"Archived thread {team.Thread.Name} for match {match.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error archiving thread for team {team.Name} in match {match.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error archiving threads for match {match.Name}");
            }
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
        public async Task SetupPlayoffStage(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Setting up playoff stage for tournament {tournament.Name}");

            // Instead of delegating to playoff service, we'll just validate the state transition
            if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
            {
                tournament.CurrentStage = TournamentStage.Playoffs;
                await _stateService.SaveTournamentStateAsync(client);
            }
            else
            {
                _logger.LogWarning($"Invalid state transition from {tournament.CurrentStage} to Playoffs for tournament {tournament.Name}");
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

        /// <summary>
        /// Checks if the tournament can progress to the next stage
        /// </summary>
        private bool CanProgressToNextStage(Tournament tournament)
        {
            // Implement the logic to determine if the tournament can progress to the next stage
            // This is a placeholder and should be replaced with the actual implementation
            return true; // Placeholder return, actual implementation needed
        }

        /// <summary>
        /// Handles the progression of the tournament to the next stage
        /// </summary>
        public async Task HandleTournamentProgressionAsync(Tournament tournament, DiscordClient client)
        {
            try
            {
                // Check if we can transition to playoffs
                if (tournament.CurrentStage == TournamentStage.Groups &&
                    tournament.Groups?.All(g => g.IsComplete) == true)
                {
                    // Validate state transition
                    if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
                    {
                        tournament.CurrentStage = TournamentStage.Playoffs;
                        _logger.LogInformation($"Tournament {tournament.Name} is transitioning to playoffs");
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid state transition from {tournament.CurrentStage} to Playoffs for tournament {tournament.Name}");
                    }
                }
                // Check if tournament is complete
                else if (tournament.CurrentStage == TournamentStage.Playoffs &&
                        tournament.PlayoffMatches?.All(m => m.IsComplete) == true)
                {
                    // Validate state transition
                    if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Complete))
                    {
                        tournament.CurrentStage = TournamentStage.Complete;
                        tournament.IsComplete = true;
                        _logger.LogInformation($"Tournament {tournament.Name} is now complete");
                    }
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tournament progression");
            }
        }

        // ... other methods ...
    }
}