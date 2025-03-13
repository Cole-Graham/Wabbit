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
        private readonly TournamentManager _tournamentManager;
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentGameService> _logger;

        // Track matches being processed to prevent concurrent updates
        private readonly HashSet<string> _processingMatches = new HashSet<string>();

        public TournamentGameService(
            TournamentManager tournamentManager,
            OngoingRounds ongoingRounds,
            ILogger<TournamentGameService> logger)
        {
            _tournamentManager = tournamentManager;
            _ongoingRounds = ongoingRounds;
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
                // Create a unique key for this match to avoid concurrent processing
                // Since Match doesn't have an ID field, use Name and participants as the key
                string matchKey = $"{tournament.Name}_{match.Name}_{match.Participants.Count}";

                // If this match is already being processed, just return
                if (_processingMatches.Contains(matchKey))
                {
                    return;
                }

                // Mark this match as being processed
                _processingMatches.Add(matchKey);

                try
                {
                    // Check if the match is a playoff match based on its type
                    bool isPlayoffMatch = match.Type == TournamentMatchType.PlayoffStage ||
                                         match.Type == TournamentMatchType.Final ||
                                         match.Type == TournamentMatchType.ThirdPlace ||
                                         match.Type == TournamentMatchType.ThirdPlaceTiebreaker;

                    // If this match is a playoff match, we need to update the bracket
                    if (isPlayoffMatch)
                    {
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

                        // Save state
                        await _tournamentManager.SaveTournamentState(client);

                        // Check if tournament is complete
                        bool allPlayoffMatchesComplete = true;
                        if (tournament.PlayoffMatches != null && tournament.PlayoffMatches.Any())
                        {
                            foreach (var playoffMatch in tournament.PlayoffMatches)
                            {
                                if (!playoffMatch.IsComplete)
                                {
                                    allPlayoffMatchesComplete = false;
                                    break;
                                }
                            }

                            if (allPlayoffMatchesComplete)
                            {
                                tournament.CurrentStage = TournamentStage.Complete;
                                await _tournamentManager.SaveTournamentState(client);
                                await _tournamentManager.PostTournamentVisualization(tournament, client);
                            }
                        }
                        return;
                    }

                    // Group stage match logic - check if group is complete, etc.
                    var group = tournament.Groups.FirstOrDefault(g => g.Matches.Contains(match));
                    if (group == null)
                        return; // Not a group stage match

                    // Check if the group is complete after this match
                    _tournamentManager.UpdateTournamentFromRound(tournament);

                    // If the group isn't complete, schedule new matches for these players if needed
                    if (!group.IsComplete)
                    {
                        // This would be implemented based on your tournament logic
                        // for creating new matches in the group stage
                    }
                    // If group is complete, check if all groups are complete to set up playoffs
                    else if (tournament.Groups.All(g => g.IsComplete) && tournament.CurrentStage == TournamentStage.Groups)
                    {
                        // Set up playoffs using TournamentManager
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
                finally
                {
                    // Make sure we remove this match from processing, even if there's an error
                    _processingMatches.Remove(matchKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling match completion");
            }
        }

        /// <summary>
        /// Starts playoff matches for a tournament
        /// </summary>
        private Task StartPlayoffMatches(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Starting playoff matches for tournament {tournament.Name}");

            // Since we're not actually awaiting anything in this stub implementation,
            // change the method to return a completed task instead of using async/await
            return Task.CompletedTask;
        }
    }
}