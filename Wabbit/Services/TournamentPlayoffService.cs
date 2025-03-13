using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament playoff management
    /// </summary>
    public class TournamentPlayoffService : ITournamentPlayoffService
    {
        private readonly ILogger<TournamentPlayoffService> _logger;
        private readonly ITournamentGroupService _groupService;

        public TournamentPlayoffService(
            ITournamentGroupService groupService,
            ILogger<TournamentPlayoffService> logger)
        {
            _groupService = groupService;
            _logger = logger;
        }

        /// <summary>
        /// Sets up playoffs for a tournament
        /// </summary>
        public void SetupPlayoffs(Tournament tournament)
        {
            if (tournament == null) throw new ArgumentNullException(nameof(tournament));

            _logger.LogInformation($"Setting up playoffs for tournament {tournament.Name}");

            // Get the total number of participants across all groups
            int totalParticipants = tournament.Groups.Sum(g => g.Participants.Count);
            int groupCount = tournament.Groups.Count;

            // Get advancement criteria
            (int groupWinners, int bestThirdPlace) = GetAdvancementCriteria(totalParticipants, groupCount);

            _logger.LogInformation($"Advancement criteria: {groupWinners} group winners + {bestThirdPlace} best third-place");

            // Mark the tournament as being in the playoff stage
            tournament.CurrentStage = TournamentStage.Playoffs;

            // Clear existing playoff matches
            tournament.PlayoffMatches.Clear();

            // Calculate total playoff participants
            int playoffParticipants = (groupWinners * groupCount) + bestThirdPlace;

            // Determine bracket size (next power of 2)
            int bracketSize = 1;
            while (bracketSize < playoffParticipants)
            {
                bracketSize *= 2;
            }

            _logger.LogInformation($"Creating playoff bracket with size {bracketSize}");

            // Get qualified participants
            var qualifiedParticipants = GetQualifiedParticipants(tournament, groupWinners, bestThirdPlace);

            // Create the playoff bracket
            List<Tournament.Match> roundMatches = CreateFirstRoundMatches(qualifiedParticipants, bracketSize);
            tournament.PlayoffMatches.AddRange(roundMatches);

            // Create the rest of the bracket
            CreateSubsequentRounds(tournament, roundMatches, bracketSize);
        }

        /// <summary>
        /// Gets qualified participants for playoffs
        /// </summary>
        private List<Tournament.MatchParticipant> GetQualifiedParticipants(
            Tournament tournament,
            int groupWinners,
            int bestThirdPlace)
        {
            var qualifiedParticipants = new List<Tournament.MatchParticipant>();

            // Get top N players from each group
            foreach (var group in tournament.Groups)
            {
                // Sort participants by points, then by wins, then by game differential
                var sortedParticipants = group.Participants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.Wins)
                    .ThenByDescending(p => p.GamesWon - p.GamesLost)
                    .ToList();

                // Add top N players to qualified participants
                for (int i = 0; i < Math.Min(groupWinners, sortedParticipants.Count); i++)
                {
                    var participant = sortedParticipants[i];
                    participant.AdvancedToPlayoffs = true;
                    participant.QualificationInfo = $"Group {group.Name} - Position {i + 1}";

                    qualifiedParticipants.Add(new Tournament.MatchParticipant
                    {
                        Player = participant.Player,
                        SourceGroup = group,
                        SourceGroupPosition = i + 1
                    });
                }

                // Add the third-place player to a separate list for potential best third-place
                if (groupWinners < 3 && sortedParticipants.Count >= 3)
                {
                    var thirdPlace = sortedParticipants[2];

                    // Add metadata to the participant for sorting
                    thirdPlace.QualificationInfo = $"Group {group.Name} - Position 3";
                }
            }

            // Add best third-place teams if needed
            if (bestThirdPlace > 0)
            {
                var thirdPlaceParticipants = new List<Tournament.GroupParticipant>();

                foreach (var group in tournament.Groups)
                {
                    if (group.Participants.Count >= 3)
                    {
                        var sortedParticipants = group.Participants
                            .OrderByDescending(p => p.Points)
                            .ThenByDescending(p => p.Wins)
                            .ThenByDescending(p => p.GamesWon - p.GamesLost)
                            .ToList();

                        if (sortedParticipants.Count >= 3)
                        {
                            thirdPlaceParticipants.Add(sortedParticipants[2]);
                        }
                    }
                }

                // Sort and take best third-place participants
                var bestThirdPlaces = thirdPlaceParticipants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.Wins)
                    .ThenByDescending(p => p.GamesWon - p.GamesLost)
                    .Take(bestThirdPlace)
                    .ToList();

                foreach (var participant in bestThirdPlaces)
                {
                    participant.AdvancedToPlayoffs = true;
                    participant.QualificationInfo += " (Best Third Place)";

                    var group = tournament.Groups.FirstOrDefault(g => g.Participants.Contains(participant));

                    qualifiedParticipants.Add(new Tournament.MatchParticipant
                    {
                        Player = participant.Player,
                        SourceGroup = group,
                        SourceGroupPosition = 3
                    });
                }
            }

            return qualifiedParticipants;
        }

        /// <summary>
        /// Creates first round matches for playoffs
        /// </summary>
        private List<Tournament.Match> CreateFirstRoundMatches(
            List<Tournament.MatchParticipant> qualifiedParticipants,
            int bracketSize)
        {
            var matches = new List<Tournament.Match>();

            // Fill in the bracket with the qualified participants
            // and add byes for empty spots
            for (int i = 0; i < bracketSize / 2; i++)
            {
                var match = new Tournament.Match
                {
                    Name = $"Playoff Match {i + 1}",
                    Type = DetermineTournamentMatchType(i, bracketSize),
                    DisplayPosition = $"Match {i + 1}",
                    BestOf = 3,
                    Participants = new List<Tournament.MatchParticipant>()
                };

                // Add participants or placeholders based on seeding
                // This uses a simple bracket ordering
                if (i < qualifiedParticipants.Count)
                {
                    match.Participants.Add(qualifiedParticipants[i]);
                }
                else
                {
                    match.Participants.Add(new Tournament.MatchParticipant { Player = null });
                }

                // Add opponent based on bracket position
                int oppositeIndex = bracketSize / 2 - 1 - i;
                if (oppositeIndex < qualifiedParticipants.Count)
                {
                    match.Participants.Add(qualifiedParticipants[oppositeIndex]);
                }
                else
                {
                    match.Participants.Add(new Tournament.MatchParticipant { Player = null });
                }

                // Set appropriate match name based on participant information
                if (match.Participants[0].Player != null && match.Participants[1].Player != null)
                {
                    match.Name = $"{_groupService.GetPlayerDisplayName(match.Participants[0].Player)} vs {_groupService.GetPlayerDisplayName(match.Participants[1].Player)}";
                }
                else if (match.Participants[0].Player != null)
                {
                    match.Name = $"{_groupService.GetPlayerDisplayName(match.Participants[0].Player)} - Bye";

                    // Auto-advance single player with bye
                    match.Participants[0].IsWinner = true;
                    match.Result = new Tournament.MatchResult
                    {
                        Winner = match.Participants[0].Player,
                        CompletedAt = DateTime.Now
                    };
                }
                else if (match.Participants[1].Player != null)
                {
                    match.Name = $"{_groupService.GetPlayerDisplayName(match.Participants[1].Player)} - Bye";

                    // Auto-advance single player with bye
                    match.Participants[1].IsWinner = true;
                    match.Result = new Tournament.MatchResult
                    {
                        Winner = match.Participants[1].Player,
                        CompletedAt = DateTime.Now
                    };
                }

                matches.Add(match);
            }

            return matches;
        }

        /// <summary>
        /// Determines the match type for a playoff match
        /// </summary>
        private TournamentMatchType DetermineTournamentMatchType(int matchIndex, int bracketSize)
        {
            // For simplicity, use PlayoffStage for all matches except finals
            if (bracketSize == 2)
            {
                return TournamentMatchType.Final;
            }
            else if (bracketSize == 4)
            {
                if (matchIndex == 0 || matchIndex == 1)
                {
                    return TournamentMatchType.Semifinal;
                }
            }
            else if (bracketSize == 8)
            {
                if (matchIndex >= 0 && matchIndex <= 3)
                {
                    return TournamentMatchType.Quarterfinal;
                }
            }

            return TournamentMatchType.PlayoffStage;
        }

        /// <summary>
        /// Creates subsequent rounds in the playoff bracket
        /// </summary>
        private void CreateSubsequentRounds(Tournament tournament, List<Tournament.Match> currentRound, int bracketSize)
        {
            // No need to continue if we're at the final
            if (bracketSize <= 2) return;

            int nextRoundSize = bracketSize / 2;
            var nextRound = new List<Tournament.Match>();

            // Create the next round of matches
            for (int i = 0; i < nextRoundSize / 2; i++)
            {
                var match = new Tournament.Match
                {
                    Name = $"TBD vs TBD",
                    Type = DetermineTournamentMatchType(i, nextRoundSize),
                    DisplayPosition = nextRoundSize == 2 ? "Final" : $"Match {i + 1}",
                    BestOf = nextRoundSize == 2 ? 5 : 3, // Finals are Bo5, others Bo3
                    Participants = new List<Tournament.MatchParticipant>
                    {
                        new Tournament.MatchParticipant { Player = null },
                        new Tournament.MatchParticipant { Player = null }
                    }
                };

                // Link the previous round matches to this one
                currentRound[i * 2].NextMatch = match;
                currentRound[i * 2 + 1].NextMatch = match;

                nextRound.Add(match);
            }

            // Add the matches to the tournament's playoff matches
            tournament.PlayoffMatches.AddRange(nextRound);

            // Recursively create subsequent rounds
            CreateSubsequentRounds(tournament, nextRound, nextRoundSize);
        }

        /// <summary>
        /// Gets advancement criteria for playoff stage
        /// </summary>
        public (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
        {
            // Default values
            int groupWinners = 2;
            int bestThirdPlace = 0;

            // Adjust based on player count and group count
            if (groupCount == 1)
            {
                // Special case for single group tournaments
                if (playerCount <= 4)
                {
                    groupWinners = 2;
                }
                else if (playerCount <= 8)
                {
                    groupWinners = 4;
                }
                else
                {
                    groupWinners = 8;
                }
            }
            else if (groupCount == 2)
            {
                // For 2 groups, advance 2 from each
                groupWinners = 2;
            }
            else if (groupCount == 3)
            {
                // For 3 groups, advance 2 from each + best third
                groupWinners = 2;
                bestThirdPlace = 2;
            }
            else if (groupCount == 4)
            {
                // For 4 groups, advance 2 from each
                groupWinners = 2;
            }
            else if (groupCount == 5 || groupCount == 6)
            {
                // For 5-6 groups, advance 1 from each + best seconds
                groupWinners = 1;
                bestThirdPlace = 8 - groupCount;
            }
            else if (groupCount == 7 || groupCount == 8)
            {
                // For 7-8 groups, advance 1 from each + best seconds
                groupWinners = 1;
                bestThirdPlace = 8 - groupCount;
            }
            else
            {
                // For many groups, advance only the winners
                groupWinners = 1;
                bestThirdPlace = 0;
            }

            return (groupWinners, bestThirdPlace);
        }
    }
}