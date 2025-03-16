using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for handling tournament game logic, separate from command handling
    /// </summary>
    public class TournamentGameService : ITournamentGameService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentRepositoryService _repositoryService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentMapService _mapService;
        private readonly ILogger<TournamentGameService> _logger;
        private readonly IMatchStatusService _matchStatusService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITournamentMatchService _matchService;
        private readonly ITournamentStateValidator _stateValidator;
        private readonly ITournamentScoreManager _scoreManager;
        private readonly ITournamentProgressTracker _progressTracker;

        // Track matches being processed to prevent concurrent updates
        private readonly HashSet<string> _processingMatches = new HashSet<string>();

        public TournamentGameService(
            OngoingRounds ongoingRounds,
            ITournamentRepositoryService repositoryService,
            ITournamentStateService stateService,
            ITournamentPlayoffService playoffService,
            ITournamentMapService mapService,
            ILogger<TournamentGameService> logger,
            IMatchStatusService matchStatusService,
            IServiceScopeFactory scopeFactory,
            ITournamentMatchService matchService,
            ITournamentStateValidator stateValidator,
            ITournamentScoreManager scoreManager,
            ITournamentProgressTracker progressTracker)
        {
            _ongoingRounds = ongoingRounds;
            _repositoryService = repositoryService;
            _stateService = stateService;
            _playoffService = playoffService;
            _mapService = mapService;
            _logger = logger;
            _matchStatusService = matchStatusService;
            _scopeFactory = scopeFactory;
            _matchService = matchService;
            _stateValidator = stateValidator;
            _scoreManager = scoreManager;
            _progressTracker = progressTracker;
        }

        /// <summary>
        /// Handles game result selection and advances the match series
        /// </summary>
        public async Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client)
        {
            try
            {
                // Update the match status with the game result
                await _matchStatusService.UpdateMatchStatusAsync(thread, round, client);

                // If the match is not complete, select the next map
                if (!round.IsCompleted)
                {
                    await _matchStatusService.UpdateMapInformationAsync(thread, round, client);
                }
                else
                {
                    // Match is complete, finalize it
                    await _matchStatusService.FinalizeMatchAsync(thread, round, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling game result");
                await thread.SendMessageAsync("An error occurred while processing the game result. Please contact an administrator.");
            }
        }

        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        public async Task HandleMatchCompletionAsync(string matchId, DiscordClient client)
        {
            try
            {
                // Prevent concurrent processing of the same match
                lock (_processingMatches)
                {
                    if (_processingMatches.Contains(matchId))
                    {
                        _logger.LogWarning($"Match {matchId} is already being processed");
                        return;
                    }
                    _processingMatches.Add(matchId);
                }

                try
                {
                    var round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                        r.CustomProperties != null &&
                        r.CustomProperties.ContainsKey("RoundId") &&
                        r.CustomProperties["RoundId"].ToString() == matchId);
                    if (round == null)
                    {
                        _logger.LogError($"Round {matchId} not found");
                        return;
                    }

                    // Get tournament and match
                    var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t => t.Name == round.TournamentId);
                    if (tournament == null)
                    {
                        _logger.LogError($"Tournament not found for round {matchId}");
                        return;
                    }

                    Tournament.Match? match = null;
                    Tournament.Group? group = null;

                    // Find the match in groups
                    if (tournament.Groups != null)
                    {
                        foreach (var g in tournament.Groups)
                        {
                            if (g.Matches == null) continue;
                            match = g.Matches.FirstOrDefault(m =>
                                m.LinkedRound?.CustomProperties != null &&
                                m.LinkedRound.CustomProperties.ContainsKey("RoundId") &&
                                m.LinkedRound.CustomProperties["RoundId"].ToString() == matchId);
                            if (match != null)
                            {
                                group = g;
                                break;
                            }
                        }
                    }

                    // If not in groups, check playoff matches
                    if (match == null && tournament.PlayoffMatches != null)
                    {
                        match = tournament.PlayoffMatches.FirstOrDefault(m =>
                            m.LinkedRound?.CustomProperties != null &&
                            m.LinkedRound.CustomProperties.ContainsKey("RoundId") &&
                            m.LinkedRound.CustomProperties["RoundId"].ToString() == matchId);
                    }

                    if (match == null)
                    {
                        _logger.LogError($"Match not found for round {matchId}");
                        return;
                    }

                    // Update group scores and check completion
                    if (group != null)
                    {
                        _scoreManager.UpdateGroupScores(group, match);
                        bool isComplete = _scoreManager.IsGroupComplete(group);

                        if (isComplete && !group.IsComplete)
                        {
                            group.IsComplete = true;
                            _logger.LogInformation($"Group {group.Name} is now complete");
                        }
                    }

                    // Update tournament progress
                    var stageProgress = _progressTracker.GetStageProgress(tournament);
                    bool canProgress = _progressTracker.CanProgressToNextStage(tournament);

                    // Check if we can transition to playoffs
                    if (canProgress && tournament.CurrentStage == TournamentStage.Groups)
                    {
                        // Validate state transition
                        if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
                        {
                            await _playoffService.SetupPlayoffsAsync(tournament, client);
                            tournament.CurrentStage = TournamentStage.Playoffs;
                            _logger.LogInformation($"Setting up playoffs for tournament {tournament.Name}");
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid state transition from {tournament.CurrentStage} to Playoffs for tournament {tournament.Name}");
                        }
                    }
                    // Check if tournament is complete
                    else if (tournament.CurrentStage == TournamentStage.Playoffs &&
                           stageProgress.GetValueOrDefault("PlayoffProgress", 0) == 100)
                    {
                        // Validate state transition
                        if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Complete))
                        {
                            tournament.CurrentStage = TournamentStage.Complete;
                            tournament.IsComplete = true;
                            _logger.LogInformation($"Tournament {tournament.Name} is now complete");
                        }
                    }

                    // Save changes
                    await _repositoryService.SaveTournamentsAsync();
                    await _stateService.SaveTournamentStateAsync(client);
                }
                finally
                {
                    // Release the lock on this match
                    lock (_processingMatches)
                    {
                        _processingMatches.Remove(matchId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling match completion: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets available maps for the next game in a match
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>List of available map names</returns>
        public List<string> GetAvailableMapsForNextGame(Round round)
        {
            if (round == null)
            {
                _logger.LogWarning("Cannot get available maps: round is null");
                return new List<string>();
            }

            try
            {
                _logger.LogInformation($"Getting available maps for next game in {round.Name}");

                // Get the initial map pool based on game type (1v1 or team)
                var initialMapPool = _mapService.GetTournamentMapPool(round.OneVOne);

                // Get all banned maps from both teams
                var bannedMaps = new HashSet<string>();

                // Add maps from global bans stored in CustomProperties
                if (round.CustomProperties.ContainsKey("BannedMaps") && round.CustomProperties["BannedMaps"] is List<string> globalBannedMaps)
                {
                    foreach (var map in globalBannedMaps)
                    {
                        bannedMaps.Add(map);
                    }
                }

                // Add team-specific bans
                if (round.Teams != null)
                {
                    foreach (var team in round.Teams)
                    {
                        if (team?.MapBans != null)
                        {
                            foreach (var map in team.MapBans)
                            {
                                bannedMaps.Add(map);
                            }
                        }
                    }
                }

                // Filter out banned maps
                var availableMaps = initialMapPool.Where(map => !bannedMaps.Contains(map)).ToList();

                // For games after the first, also remove already played maps
                if (round.Maps != null && round.Maps.Count > 0)
                {
                    availableMaps = availableMaps.Where(map => !round.Maps.Contains(map)).ToList();

                    _logger.LogInformation($"Filtered out {round.Maps.Count} already played maps");
                }

                _logger.LogInformation($"Found {availableMaps.Count} available maps for next game");

                // If no maps available (all banned or played), return the initial pool with a warning
                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available after filtering. Using initial map pool.");
                    return initialMapPool;
                }

                return availableMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available maps for next game");
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets a random map for the next game, considering banned and played maps
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>A random map name, or null if no maps are available</returns>
        public string? GetRandomMapForNextGame(Round round)
        {
            if (round == null)
            {
                _logger.LogWarning("Cannot get random map: round is null");
                return null;
            }

            try
            {
                var availableMaps = GetAvailableMapsForNextGame(round);

                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available for random selection");
                    return null;
                }

                // Select a random map
                var random = new Random();
                int index = random.Next(availableMaps.Count);
                string selectedMap = availableMaps[index];

                _logger.LogInformation($"Randomly selected map: {selectedMap}");
                return selectedMap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map for next game");
                return null;
            }
        }

        public async Task RecordGameResultAsync(Round round, string winnerId, int gameNumber, DiscordClient client)
        {
            try
            {
                if (round?.Teams == null || !round.Teams.Any())
                {
                    _logger.LogError("Cannot record game result: round or teams are null");
                    return;
                }

                // Find the winning team
                var winningTeam = round.Teams.FirstOrDefault(t =>
                    t.Participants?.Any(p => p.Player?.Id.ToString() == winnerId) ?? false);

                if (winningTeam == null)
                {
                    _logger.LogError($"Cannot find winning team for player {winnerId}");
                    return;
                }

                // Update scores
                if (round.CustomProperties != null)
                {
                    string winnerKey = winningTeam == round.Teams[0] ? "Player1Wins" : "Player2Wins";
                    round.CustomProperties[winnerKey] = ((int)round.CustomProperties[winnerKey]) + 1;
                }

                // Handle match completion if needed
                var threadId = round?.Teams?.FirstOrDefault()?.Thread?.Id;
                if (threadId.HasValue && round is not null)
                {
                    await HandleGameResultAsync(round, await client.GetChannelAsync(threadId.Value), winnerId, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording game result");
            }
        }

        public async Task<bool> ProcessDeckSubmissionAsync(Round round, ulong playerId, string deckCode, int gameNumber)
        {
            try
            {
                // Initialize deck codes dictionary if needed
                if (!round.CustomProperties.ContainsKey("DeckCodes"))
                {
                    round.CustomProperties["DeckCodes"] = new Dictionary<string, Dictionary<string, string>>();
                }

                var deckCodes = round.CustomProperties["DeckCodes"] as Dictionary<string, Dictionary<string, string>>;
                if (deckCodes == null)
                {
                    _logger.LogError("Failed to initialize deck codes dictionary");
                    return false;
                }

                // Initialize game dictionary if needed
                string gameKey = $"Game{gameNumber}";
                if (!deckCodes.ContainsKey(gameKey))
                {
                    deckCodes[gameKey] = new Dictionary<string, string>();
                }

                // Store the deck code
                deckCodes[gameKey][playerId.ToString()] = deckCode;

                // Save state after updating deck codes
                await _stateService.SaveTournamentStateAsync();

                // Handle deck submission completion
                if (round.Teams?.FirstOrDefault()?.Thread is not null)
                {
                    // Check if both players have submitted decks
                    if (AreDeckSubmissionsComplete(round, gameNumber))
                    {
                        _logger.LogInformation("Both players have submitted decks");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deck submission");
                return false;
            }
        }

        public bool AreDeckSubmissionsComplete(Round round, int gameNumber)
        {
            if (round.CustomProperties?.ContainsKey("DeckCodes") != true)
                return false;

            var deckCodes = round.CustomProperties["DeckCodes"] as Dictionary<string, Dictionary<string, string>>;
            if (deckCodes == null)
                return false;

            string gameKey = $"Game{gameNumber}";
            if (!deckCodes.ContainsKey(gameKey))
                return false;

            // Check if both players have submitted decks for this game
            int submissionCount = 0;
            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is null) continue;

                    string userId = participant.Player.Id.ToString();
                    if (deckCodes[gameKey].ContainsKey(userId))
                    {
                        submissionCount++;
                    }
                }
            }

            return submissionCount >= 2;
        }

        public int GetCurrentGameNumber(Round round)
        {
            // The current game number is the count of maps played plus 1
            return (round.Maps?.Count ?? 0) + 1;
        }

        public int GetPlayerScore(Round round, ulong playerId)
        {
            if (round.CustomProperties == null)
                return 0;

            string playerKey = playerId.ToString();
            if (round.Teams?.FirstOrDefault()?.Participants?.FirstOrDefault()?.Player?.Id.ToString() == playerKey)
            {
                return round.CustomProperties.ContainsKey("Player1Wins") ?
                    Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
            }
            else if (round.Teams?.LastOrDefault()?.Participants?.FirstOrDefault()?.Player?.Id.ToString() == playerKey)
            {
                return round.CustomProperties.ContainsKey("Player2Wins") ?
                    Convert.ToInt32(round.CustomProperties["Player2Wins"]) : 0;
            }

            return 0;
        }

        public bool IsMatchComplete(Round round)
        {
            if (round.CustomProperties == null)
                return false;

            int player1Wins = round.CustomProperties.ContainsKey("Player1Wins") ?
                Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
            int player2Wins = round.CustomProperties.ContainsKey("Player2Wins") ?
                Convert.ToInt32(round.CustomProperties["Player2Wins"]) : 0;

            int winsNeeded = (round.Length + 1) / 2;
            return player1Wins >= winsNeeded || player2Wins >= winsNeeded;
        }

        public DiscordMember? GetMatchWinner(Round round)
        {
            if (!IsMatchComplete(round) || round.Teams == null)
                return null;

            int player1Wins = round.CustomProperties.ContainsKey("Player1Wins") ?
                Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
            int player2Wins = round.CustomProperties.ContainsKey("Player2Wins") ?
                Convert.ToInt32(round.CustomProperties["Player2Wins"]) : 0;

            if (player1Wins > player2Wins)
                return round.Teams.FirstOrDefault()?.Participants?.FirstOrDefault()?.Player as DiscordMember;
            else if (player2Wins > player1Wins)
                return round.Teams.LastOrDefault()?.Participants?.FirstOrDefault()?.Player as DiscordMember;

            return null;
        }

        public (int winner, int loser) GetFinalScore(Round round)
        {
            if (round.CustomProperties == null)
                return (0, 0);

            int player1Wins = round.CustomProperties.ContainsKey("Player1Wins") ?
                Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
            int player2Wins = round.CustomProperties.ContainsKey("Player2Wins") ?
                Convert.ToInt32(round.CustomProperties["Player2Wins"]) : 0;

            return player1Wins > player2Wins ? (player1Wins, player2Wins) : (player2Wins, player1Wins);
        }
    }
}