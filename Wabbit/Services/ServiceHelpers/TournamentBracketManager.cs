using System;
using System.Collections.Generic;
using System.Linq;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Manages tournament bracket operations
    /// </summary>
    public class TournamentBracketManager : ITournamentBracketManager
    {
        private readonly ILogger<TournamentBracketManager> _logger;

        public TournamentBracketManager(ILogger<TournamentBracketManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Updates bracket advancement after a match is completed
        /// </summary>
        public bool UpdateBracketAdvancement(Tournament tournament, Tournament.Match match)
        {
            try
            {
                if (match.Result?.Winner == null)
                {
                    _logger.LogWarning($"Cannot advance bracket: match {match.Id} has no winner");
                    return false;
                }

                var nextMatch = match.NextMatch;
                if (nextMatch == null)
                {
                    _logger.LogWarning($"Match {match.Id} has no next match to advance to");
                    return true; // Not an error if this is the final match
                }

                // Find the winner in the match participants
                var winnerParticipant = match.Participants.FirstOrDefault(p =>
                    (p.Player as DiscordUser)?.Id == (match.Result.Winner as DiscordUser)?.Id);

                if (winnerParticipant == null)
                {
                    _logger.LogError($"Winner {match.Result.Winner} not found in match {match.Id} participants");
                    return false;
                }

                // Create new participant for next match
                var nextParticipant = new Tournament.MatchParticipant
                {
                    Player = winnerParticipant.Player,
                    SourceMatch = match
                };

                // Add to next match if not already present
                if (!nextMatch.Participants.Any(p =>
                    (p.Player as DiscordUser)?.Id == (nextParticipant.Player as DiscordUser)?.Id))
                {
                    nextMatch.Participants.Add(nextParticipant);
                    _logger.LogInformation($"Advanced winner {winnerParticipant.Player} to match {nextMatch.Id}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating bracket advancement for match {match.Id}");
                return false;
            }
        }

        /// <summary>
        /// Creates the initial playoff bracket based on qualified participants
        /// </summary>
        public List<Tournament.Match> CreatePlayoffBracket(Tournament tournament, List<Tournament.MatchParticipant> qualifiedParticipants)
        {
            try
            {
                int participantCount = qualifiedParticipants.Count;
                int bracketSize = GetNextPowerOfTwo(participantCount);

                // Create first round matches
                var firstRoundMatches = new List<Tournament.Match>();
                int byeCount = bracketSize - participantCount;

                for (int i = 0; i < bracketSize / 2; i++)
                {
                    var match = new Tournament.Match
                    {
                        Id = Guid.NewGuid().ToString(),
                        TournamentId = tournament.Name,
                        Type = DetermineBracketRound(bracketSize / 2),
                        BestOf = tournament.Settings?.BestOfQuarterfinals ?? 3
                    };

                    // Add participants directly since they're already MatchParticipants
                    int player1Index = i;
                    int player2Index = bracketSize - 1 - i;

                    if (player1Index < participantCount)
                    {
                        match.Participants.Add(qualifiedParticipants[player1Index]);
                    }

                    if (player2Index < participantCount)
                    {
                        match.Participants.Add(qualifiedParticipants[player2Index]);
                    }

                    firstRoundMatches.Add(match);
                }

                // Link matches together
                LinkBracketMatches(tournament, firstRoundMatches);

                // Add matches to tournament
                tournament.PlayoffMatches ??= [];
                tournament.PlayoffMatches.AddRange(firstRoundMatches);

                _logger.LogInformation($"Created playoff bracket with {firstRoundMatches.Count} first round matches");
                return firstRoundMatches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating playoff bracket");
                throw;
            }
        }

        /// <summary>
        /// Links matches in the bracket to form a complete structure
        /// </summary>
        public void LinkBracketMatches(Tournament tournament, List<Tournament.Match> firstRoundMatches)
        {
            int currentRoundSize = firstRoundMatches.Count;
            var currentRound = firstRoundMatches;
            var allMatches = new List<Tournament.Match>(firstRoundMatches);

            while (currentRoundSize > 1)
            {
                var nextRound = new List<Tournament.Match>();
                for (int i = 0; i < currentRoundSize; i += 2)
                {
                    var nextMatch = new Tournament.Match
                    {
                        Id = Guid.NewGuid().ToString(),
                        TournamentId = tournament.Name,
                        Type = DetermineBracketRound(currentRoundSize / 2)
                    };

                    // Link current round matches to next round
                    currentRound[i].NextMatch = nextMatch;
                    if (i + 1 < currentRoundSize)
                    {
                        currentRound[i + 1].NextMatch = nextMatch;
                    }

                    nextRound.Add(nextMatch);
                    allMatches.Add(nextMatch);
                }

                currentRound = nextRound;
                currentRoundSize = currentRound.Count;
            }

            // Add all matches to tournament
            tournament.PlayoffMatches.AddRange(allMatches.Except(firstRoundMatches));
        }

        /// <summary>
        /// Determines the type of match based on its position in the bracket
        /// </summary>
        public TournamentMatchType DetermineBracketRound(int matchesInRound)
        {
            return matchesInRound switch
            {
                8 => TournamentMatchType.RoundOf16,
                4 => TournamentMatchType.Quarterfinal,
                2 => TournamentMatchType.Semifinal,
                1 => TournamentMatchType.Final,
                _ => TournamentMatchType.RoundOf16
            };
        }

        /// <summary>
        /// Gets the next power of two that is greater than or equal to the input number.
        /// </summary>
        private int GetNextPowerOfTwo(int n)
        {
            int power = 1;
            while (power < n)
            {
                power *= 2;
            }
            return power;
        }
    }
}