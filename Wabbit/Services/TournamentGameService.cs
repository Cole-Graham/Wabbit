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

        // Track matches being processed to prevent concurrent updates
        private readonly HashSet<string> _processingMatches = new HashSet<string>();

        public TournamentGameService(
            OngoingRounds ongoingRounds,
            ITournamentRepositoryService repositoryService,
            ITournamentStateService stateService,
            ITournamentPlayoffService playoffService,
            ITournamentMapService mapService,
            ILogger<TournamentGameService> logger)
        {
            _ongoingRounds = ongoingRounds;
            _repositoryService = repositoryService;
            _stateService = stateService;
            _playoffService = playoffService;
            _mapService = mapService;
            _logger = logger;
        }

        /// <summary>
        /// Handles game result selection and advances the match series
        /// </summary>
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

        /// <summary>
        /// Internal method to handle game result selection and advance the match series
        /// </summary>
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
                            await _stateService.SaveTournamentStateAsync(client);

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

                    // Select a random map for the next game
                    string? nextGameMap = GetRandomMapForNextGame(round);

                    // If a map was found, track it in played maps
                    if (!string.IsNullOrEmpty(nextGameMap))
                    {
                        _logger.LogInformation($"Selected map for game {currentGame}: {nextGameMap}");

                        // Ensure PlayedMaps is initialized
                        if (round.Maps == null)
                        {
                            round.Maps = new List<string>();
                        }

                        // Add to played maps
                        round.Maps.Add(nextGameMap);

                        // Announce the map
                        foreach (var currentThread in threadsToUpdate)
                        {
                            await currentThread.SendMessageAsync($"🗺️ Map for Game {currentGame}: **{nextGameMap}**");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Could not select a map for game {currentGame}. All maps may be banned or played.");

                        // Inform players of the issue
                        foreach (var currentThread in threadsToUpdate)
                        {
                            await currentThread.SendMessageAsync("⚠️ Unable to select a map for the next game. Please contact an administrator.");
                        }
                    }

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
                        "Select game winner",
                        winnerOptions
                    );

                    // Send the dropdown to all threads
                    foreach (var currentThread in threadsToUpdate)
                    {
                        await currentThread.SendMessageAsync(
                            new DiscordMessageBuilder()
                                .WithContent($"Game {currentGame}: Select the winner")
                                .AddComponents(winnerDropdown)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling game result");
                await reportingThread.SendMessageAsync($"⚠️ Error processing game result: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            try
            {
                string matchId = match.Name ?? Guid.NewGuid().ToString();

                // Prevent concurrent processing of the same match
                lock (_processingMatches)
                {
                    if (_processingMatches.Contains(matchId))
                    {
                        _logger.LogWarning($"Match {matchId} is already being processed. Skipping.");
                        return;
                    }
                    _processingMatches.Add(matchId);
                }

                try
                {
                    _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

                    // Update next match if defined
                    if (match.NextMatch is not null && match.Result?.Winner is not null)
                    {
                        // Find the next match and add the winner to it
                        var nextMatch = match.NextMatch;

                        // Create a new participant for the next match
                        var advancingParticipant = new Tournament.MatchParticipant
                        {
                            Player = match.Result.Winner
                        };

                        // Add the advancing participant if they're not already there
                        if (!nextMatch.Participants.Any(p => p.Player == match.Result.Winner))
                        {
                            nextMatch.Participants.Add(advancingParticipant);
                        }
                    }

                    // Save tournament state
                    await _stateService.SaveTournamentStateAsync(client);

                    // Check if tournament is complete
                    bool allPlayoffMatchesComplete = true;
                    if (tournament.PlayoffMatches != null && tournament.PlayoffMatches.Any())
                    {
                        allPlayoffMatchesComplete = tournament.PlayoffMatches.All(m => m.IsComplete);
                    }

                    if (allPlayoffMatchesComplete && tournament.CurrentStage == TournamentStage.Playoffs)
                    {
                        // Find the final match to determine the winner
                        var finalMatch = tournament.PlayoffMatches?
                            .OrderByDescending(m => m.DisplayPosition)
                            .FirstOrDefault();

                        if (finalMatch?.Result?.Winner != null)
                        {
                            tournament.IsComplete = true;
                            // Set the tournament stage to Complete
                            tournament.CurrentStage = TournamentStage.Complete;

                            _logger.LogInformation($"Tournament {tournament.Name} completed with winner: {finalMatch.Result.Winner}");

                            // Get winner display name
                            string winnerName = "Unknown";
                            if (finalMatch.Result.Winner is DiscordMember member)
                            {
                                winnerName = member.DisplayName ?? member.Username;
                            }
                            else if (finalMatch.Result.Winner is DiscordUser user)
                            {
                                winnerName = user.Username;
                            }

                            // Announce the champion in the announcement channel if available
                            if (tournament.AnnouncementChannel is not null)
                            {
                                try
                                {
                                    var winnerEmbed = new DiscordEmbedBuilder()
                                        .WithTitle($"🏆 Tournament Champion: {tournament.Name}")
                                        .WithDescription($"**{winnerName}** has won the tournament!")
                                        .WithColor(DiscordColor.Gold)
                                        .WithTimestamp(DateTime.Now);

                                    await tournament.AnnouncementChannel.SendMessageAsync(winnerEmbed);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error announcing tournament winner: {ex.Message}");
                                }
                            }

                            // Generate final standings visualization
                            try
                            {
                                await Wabbit.Misc.TournamentVisualization.GenerateStandingsImage(tournament, client, _stateService);
                                _logger.LogInformation($"Generated final standings visualization for tournament {tournament.Name}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error generating final standings visualization: {ex.Message}");
                            }
                        }
                    }

                    // Save tournament data
                    await _repositoryService.SaveTournamentsAsync();

                    // If tournament is still ongoing, check for playoff start
                    if (!tournament.IsComplete)
                    {
                        // If all groups are complete, set up playoffs (if not already in playoffs)
                        bool allGroupsComplete = tournament.Groups.All(g => g.IsComplete);
                        if (allGroupsComplete && tournament.CurrentStage == TournamentStage.Groups)
                        {
                            _playoffService.SetupPlayoffs(tournament);
                            tournament.CurrentStage = TournamentStage.Playoffs;

                            _logger.LogInformation($"Setting up playoffs for tournament {tournament.Name}");

                            // Save changes after playoff setup
                            await _repositoryService.SaveTournamentsAsync();
                        }
                    }
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
    }
}