using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.Threading.Tasks;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament playoff management
    /// </summary>
    public class TournamentPlayoffService : ITournamentPlayoffService
    {
        private readonly ILogger<TournamentPlayoffService> _logger;
        private readonly ITournamentGroupService _groupService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="groupService">Service for accessing group data</param>
        /// <param name="logger">Logger for logging events</param>
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
        /// <param name="tournament">The tournament to set up playoffs for</param>
        /// <exception cref="ArgumentNullException">Thrown when tournament is null</exception>
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

            try
            {
                // Get qualified participants
                var qualifiedParticipants = GetQualifiedParticipants(tournament, groupWinners, bestThirdPlace);

                // Create the playoff bracket
                List<Tournament.Match> roundMatches = CreateFirstRoundMatches(qualifiedParticipants, bracketSize, tournament);
                tournament.PlayoffMatches.AddRange(roundMatches);

                // Create the rest of the bracket
                CreateSubsequentRounds(tournament, roundMatches, bracketSize);

                // Create third place match if tournament settings require it
                if (tournament.Settings?.IncludeThirdPlaceMatch == true)
                {
                    CreateThirdPlaceMatch(tournament);
                }

                _logger.LogInformation($"Successfully created playoff bracket with {tournament.PlayoffMatches.Count} matches");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up playoffs for tournament {TournamentName}", tournament.Name);
                throw; // Re-throw after logging to allow caller to handle the exception
            }
        }

        /// <summary>
        /// Updates bracket advancement after a match result
        /// </summary>
        /// <param name="tournament">The tournament to update</param>
        /// <param name="match">The match that was completed</param>
        /// <returns>True if advancement was successful, false otherwise</returns>
        public bool UpdateBracketAdvancement(Tournament tournament, Tournament.Match match)
        {
            try
            {
                if (tournament == null) throw new ArgumentNullException(nameof(tournament));
                if (match == null) throw new ArgumentNullException(nameof(match));

                _logger.LogInformation($"Updating bracket advancement for match {match.Name} in tournament {tournament.Name}");

                // Verify the match has a result and a winner
                if (match.Result == null || match.Result.Winner == null)
                {
                    _logger.LogWarning("Cannot update bracket: match has no result or winner");
                    return false;
                }

                // Find the winning participant by checking ID based on player type
                var winningParticipant = match.Participants.FirstOrDefault(p =>
                {
                    if (p.Player == null) return false;

                    // Handle different player types
                    if (p.Player is DiscordUser discordUser && match.Result.Winner is DiscordUser winner1)
                    {
                        return discordUser.Id == winner1.Id;
                    }
                    else if (p.Player is DiscordMember discordMember && match.Result.Winner is DiscordMember winner2)
                    {
                        return discordMember.Id == winner2.Id;
                    }
                    else if (p.Player is DiscordUser playerUser && match.Result.Winner is DiscordMember winner3)
                    {
                        return playerUser.Id == winner3.Id;
                    }
                    else if (p.Player is DiscordMember playerMember && match.Result.Winner is DiscordUser winner4)
                    {
                        return playerMember.Id == winner4.Id;
                    }

                    // Fallback to regular equality check
                    return p.Player.Equals(match.Result.Winner);
                });

                if (winningParticipant == null)
                {
                    _logger.LogWarning("Cannot update bracket: winning participant not found in match");
                    return false;
                }

                // Mark the participant as winner
                winningParticipant.IsWinner = true;

                // Get the next match
                var nextMatch = match.NextMatch;
                if (nextMatch == null)
                {
                    _logger.LogInformation("Match is final, no next match to update");
                    return true; // Final match, no advancement needed
                }

                // Find the index of this match in the previous matches leading to nextMatch
                // This determines which slot in nextMatch to fill
                var prevMatches = tournament.PlayoffMatches
                    .Where(m => m.NextMatch == nextMatch)
                    .ToList();

                int matchIndex = prevMatches.IndexOf(match);
                if (matchIndex == -1)
                {
                    _logger.LogWarning("Cannot update bracket: match not found in previous matches leading to next match");
                    return false;
                }

                // Advance the winner to the next match
                int nextMatchSlot = matchIndex % 2; // 0 for first slot, 1 for second slot
                nextMatch.Participants[nextMatchSlot] = new Tournament.MatchParticipant
                {
                    Player = match.Result.Winner,
                    SourceMatch = match
                };

                // Update the next match name if both participants are known
                if (nextMatch.Participants[0]?.Player != null && nextMatch.Participants[1]?.Player != null)
                {
                    nextMatch.Name = $"{_groupService.GetPlayerDisplayName(nextMatch.Participants[0].Player)} vs {_groupService.GetPlayerDisplayName(nextMatch.Participants[1].Player)}";
                }
                else if (nextMatch.Participants[0]?.Player != null)
                {
                    nextMatch.Name = $"{_groupService.GetPlayerDisplayName(nextMatch.Participants[0].Player)} vs TBD";
                }
                else if (nextMatch.Participants[1]?.Player != null)
                {
                    nextMatch.Name = $"TBD vs {_groupService.GetPlayerDisplayName(nextMatch.Participants[1].Player)}";
                }

                // Check if this is a semifinal match (match has a ThirdPlaceMatch property set)
                // If so, we need to advance the loser to the third place match
                if (match.ThirdPlaceMatch != null)
                {
                    _logger.LogInformation($"Advancing loser to third place match for semifinal {match.Name}");

                    // Find the losing participant
                    var losingParticipant = match.Participants.FirstOrDefault(p =>
                    {
                        if (p.Player == null) return false;
                        if (p == winningParticipant) return false;

                        // Handle different player types
                        if (p.Player is DiscordUser discordUser && match.Result.Winner is DiscordUser winnerA)
                        {
                            return discordUser.Id != winnerA.Id;
                        }
                        else if (p.Player is DiscordMember discordMember && match.Result.Winner is DiscordMember winnerB)
                        {
                            return discordMember.Id != winnerB.Id;
                        }
                        else if (p.Player is DiscordUser playerUser && match.Result.Winner is DiscordMember winnerC)
                        {
                            return playerUser.Id != winnerC.Id;
                        }
                        else if (p.Player is DiscordMember playerMember && match.Result.Winner is DiscordUser winnerD)
                        {
                            return playerMember.Id != winnerD.Id;
                        }

                        // Fallback to regular equality check
                        return !p.Player.Equals(match.Result.Winner);
                    });

                    if (losingParticipant != null)
                    {
                        // Determine which slot in third place match to fill
                        // First semifinal loser goes to slot 0, second to slot 1
                        int thirdPlaceSlot = prevMatches.IndexOf(match);
                        if (thirdPlaceSlot >= 0 && thirdPlaceSlot < 2)
                        {
                            match.ThirdPlaceMatch.Participants[thirdPlaceSlot] = new Tournament.MatchParticipant
                            {
                                Player = losingParticipant.Player,
                                SourceMatch = match
                            };

                            // Update third place match name if both participants known
                            if (match.ThirdPlaceMatch.Participants[0]?.Player != null && match.ThirdPlaceMatch.Participants[1]?.Player != null)
                            {
                                match.ThirdPlaceMatch.Name = $"{_groupService.GetPlayerDisplayName(match.ThirdPlaceMatch.Participants[0].Player)} vs {_groupService.GetPlayerDisplayName(match.ThirdPlaceMatch.Participants[1].Player)}";
                            }
                            else if (match.ThirdPlaceMatch.Participants[0]?.Player != null)
                            {
                                match.ThirdPlaceMatch.Name = $"{_groupService.GetPlayerDisplayName(match.ThirdPlaceMatch.Participants[0].Player)} vs TBD";
                            }
                            else if (match.ThirdPlaceMatch.Participants[1]?.Player != null)
                            {
                                match.ThirdPlaceMatch.Name = $"TBD vs {_groupService.GetPlayerDisplayName(match.ThirdPlaceMatch.Participants[1].Player)}";
                            }

                            _logger.LogInformation($"Successfully updated third place match with loser {_groupService.GetPlayerDisplayName(losingParticipant.Player)}");
                        }
                        else
                        {
                            _logger.LogWarning("Cannot determine third place match slot for semifinal loser");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot find losing participant in semifinal match");
                    }
                }

                _logger.LogInformation($"Successfully updated bracket advancement for match {match.Name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating bracket advancement for match {MatchName} in tournament {TournamentName}",
                    match?.Name ?? "unknown", tournament?.Name ?? "unknown");
                return false;
            }
        }

        /// <summary>
        /// Creates a third place match between the losers of the semifinals
        /// </summary>
        /// <param name="tournament">The tournament to create the third place match for</param>
        /// <returns>The created third place match, or null if creation failed</returns>
        private Tournament.Match? CreateThirdPlaceMatch(Tournament tournament)
        {
            try
            {
                if (tournament == null || tournament.PlayoffMatches == null)
                {
                    _logger.LogError("Cannot create third place match: tournament or playoff matches are null");
                    return null;
                }

                // Find semifinal matches (matches leading directly to the final)
                var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
                if (finalMatch == null)
                {
                    _logger.LogWarning("Cannot create third place match: no final match found in tournament {TournamentName}", tournament.Name);
                    return null;
                }

                var semifinals = tournament.PlayoffMatches
                    .Where(m => m.NextMatch == finalMatch)
                    .ToList();

                if (semifinals.Count != 2)
                {
                    _logger.LogWarning("Cannot create third place match: expected 2 semifinal matches, found {SemifinalCount} in tournament {TournamentName}",
                        semifinals.Count, tournament.Name);
                    return null;
                }

                // Check if a third place match already exists
                var existingThirdPlaceMatch = tournament.PlayoffMatches
                    .FirstOrDefault(m => m.Type == TournamentMatchType.ThirdPlaceTiebreaker);

                if (existingThirdPlaceMatch != null)
                {
                    _logger.LogInformation("Third place match already exists for tournament {TournamentName}", tournament.Name);
                    return existingThirdPlaceMatch;
                }

                // Determine best-of based on settings
                int bestOf = tournament.Settings?.BestOfSemifinals ?? 3;

                // Ensure the value is odd (required for match formats)
                bestOf = bestOf % 2 == 0 ? bestOf + 1 : bestOf;

                // Create third place match
                var thirdPlaceMatch = new Tournament.Match
                {
                    Name = "Third Place Match",
                    Type = TournamentMatchType.ThirdPlaceTiebreaker,
                    DisplayPosition = "3rd Place",
                    BestOf = bestOf,
                    Participants = new List<Tournament.MatchParticipant>
                    {
                        new Tournament.MatchParticipant { Player = null },
                        new Tournament.MatchParticipant { Player = null }
                    }
                };

                // Link the semifinals to this match for the losers
                semifinals[0].ThirdPlaceMatch = thirdPlaceMatch;
                semifinals[1].ThirdPlaceMatch = thirdPlaceMatch;

                // Verify semifinal linkage
                bool linkedSuccessfully = semifinals.All(m => m.ThirdPlaceMatch == thirdPlaceMatch);
                if (!linkedSuccessfully)
                {
                    _logger.LogWarning("Third place match created but not all semifinals were properly linked");
                }

                tournament.PlayoffMatches.Add(thirdPlaceMatch);
                _logger.LogInformation("Third place match created successfully for tournament {TournamentName}", tournament.Name);

                return thirdPlaceMatch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating third place match for tournament {TournamentName}", tournament?.Name ?? "unknown");
                // Don't throw, as this is an optional feature
                return null;
            }
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

            // Seed the participants to create balanced brackets
            return SeedParticipants(qualifiedParticipants);
        }

        /// <summary>
        /// Seeds participants for a balanced bracket
        /// </summary>
        /// <param name="participants">List of participants to seed</param>
        /// <returns>The seeded list of participants</returns>
        private List<Tournament.MatchParticipant> SeedParticipants(List<Tournament.MatchParticipant> participants)
        {
            if (participants.Count <= 1)
                return participants;

            // Create seeds based on group position
            participants = participants
                .OrderBy(p => p.SourceGroupPosition)
                .ToList();

            int count = participants.Count;

            // If power of 2, use standard seeding
            if ((count & (count - 1)) == 0)
            {
                // Apply standard tournament seeding algorithm
                // 1 vs 8, 4 vs 5, 2 vs 7, 3 vs 6, etc.
                var seeded = new List<Tournament.MatchParticipant>(participants.Count);

                // Generate standard seeds (1 vs 16, 8 vs 9, etc.)
                for (int i = 0; i < count / 2; i++)
                {
                    seeded.Add(participants[i]);             // Top half seeds
                    seeded.Add(participants[count - 1 - i]); // Bottom half seeds
                }

                return seeded;
            }

            // Just return the sorted participants if not a power of 2
            // They will be matched with byes as needed
            return participants;
        }

        /// <summary>
        /// Creates first round matches for playoffs
        /// </summary>
        private List<Tournament.Match> CreateFirstRoundMatches(
            List<Tournament.MatchParticipant> qualifiedParticipants,
            int bracketSize,
            Tournament tournament)
        {
            var matches = new List<Tournament.Match>();

            // Fill in the bracket with the qualified participants
            // and add byes for empty spots
            for (int i = 0; i < bracketSize / 2; i++)
            {
                // Determine match type
                var matchType = DetermineTournamentMatchType(i, bracketSize);

                // Determine best-of based on match type and settings
                int bestOf = 3; // Default if settings not available
                if (tournament.Settings != null)
                {
                    if (matchType == TournamentMatchType.Final)
                    {
                        bestOf = tournament.Settings.BestOfFinals;
                    }
                    else if (matchType == TournamentMatchType.Semifinal)
                    {
                        bestOf = tournament.Settings.BestOfSemifinals;
                    }
                    else if (matchType == TournamentMatchType.Quarterfinal)
                    {
                        bestOf = tournament.Settings.BestOfQuarterfinals;
                    }
                }

                var match = new Tournament.Match
                {
                    Name = $"Playoff Match {i + 1}",
                    Type = matchType,
                    DisplayPosition = $"Match {i + 1}",
                    BestOf = bestOf,
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
                        CompletedAt = DateTime.Now,
                        Status = MatchStatus.Completed,
                        ResultType = MatchResultType.Default // Bye is treated as a default win
                    };
                }
                else if (match.Participants[1]?.Player != null)
                {
                    match.Name = $"{_groupService.GetPlayerDisplayName(match.Participants[1].Player)} - Bye";

                    // Auto-advance single player with bye
                    match.Participants[1].IsWinner = true;
                    match.Result = new Tournament.MatchResult
                    {
                        Winner = match.Participants[1].Player,
                        CompletedAt = DateTime.Now,
                        Status = MatchStatus.Completed,
                        ResultType = MatchResultType.Default // Bye is treated as a default win
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
            else if (bracketSize == 16)
            {
                if (matchIndex >= 0 && matchIndex <= 7)
                {
                    return TournamentMatchType.RoundOf16;
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
                // Determine match type
                var matchType = DetermineTournamentMatchType(i, nextRoundSize);

                // Determine best-of based on match type and settings
                int bestOf = 3; // Default if settings not available
                if (tournament.Settings != null)
                {
                    if (matchType == TournamentMatchType.Final)
                    {
                        bestOf = tournament.Settings.BestOfFinals;
                    }
                    else if (matchType == TournamentMatchType.Semifinal)
                    {
                        bestOf = tournament.Settings.BestOfSemifinals;
                    }
                    else if (matchType == TournamentMatchType.Quarterfinal)
                    {
                        bestOf = tournament.Settings.BestOfQuarterfinals;
                    }
                }

                var match = new Tournament.Match
                {
                    Name = $"TBD vs TBD",
                    Type = matchType,
                    DisplayPosition = nextRoundSize == 2 ? "Final" : $"Match {i + 1}",
                    BestOf = bestOf,
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

        /// <summary>
        /// Processes a forfeit in a playoff match
        /// </summary>
        /// <param name="tournament">The tournament containing the match</param>
        /// <param name="match">The match to forfeit</param>
        /// <param name="forfeitingPlayer">The player forfeiting the match</param>
        /// <returns>True if the forfeit was processed successfully, false otherwise</returns>
        public bool ProcessForfeit(Tournament tournament, Tournament.Match match, object forfeitingPlayer)
        {
            try
            {
                if (tournament == null)
                {
                    _logger.LogError("Cannot process forfeit: tournament is null");
                    return false;
                }

                if (match == null)
                {
                    _logger.LogError("Cannot process forfeit: match is null");
                    return false;
                }

                if (forfeitingPlayer == null)
                {
                    _logger.LogError("Cannot process forfeit: forfeiting player is null");
                    return false;
                }

                _logger.LogInformation($"Processing forfeit for match {match.Name} in tournament {tournament.Name}");

                // Check if the match is already complete
                if (match.IsComplete)
                {
                    _logger.LogWarning("Cannot process forfeit: match {MatchName} is already complete", match.Name);
                    return false;
                }

                // Find the forfeiting participant
                var forfeitingParticipant = match.Participants.FirstOrDefault(p =>
                {
                    if (p.Player == null) return false;

                    // Handle different player types
                    if (p.Player is DiscordUser discordUser && forfeitingPlayer is DiscordUser forfeitUser1)
                    {
                        return discordUser.Id == forfeitUser1.Id;
                    }
                    else if (p.Player is DiscordMember discordMember && forfeitingPlayer is DiscordMember forfeitMember1)
                    {
                        return discordMember.Id == forfeitMember1.Id;
                    }
                    else if (p.Player is DiscordUser playerUser && forfeitingPlayer is DiscordMember forfeitMember2)
                    {
                        return playerUser.Id == forfeitMember2.Id;
                    }
                    else if (p.Player is DiscordMember playerMember && forfeitingPlayer is DiscordUser forfeitUser2)
                    {
                        return playerMember.Id == forfeitUser2.Id;
                    }

                    // Fallback to regular equality check
                    return p.Player.Equals(forfeitingPlayer);
                });

                if (forfeitingParticipant == null)
                {
                    _logger.LogWarning("Cannot process forfeit: forfeiting player not found in match {MatchName}", match.Name);
                    return false;
                }

                // Find the opponent
                var opponentParticipant = match.Participants.FirstOrDefault(p =>
                    p != forfeitingParticipant && p.Player != null);

                if (opponentParticipant == null)
                {
                    _logger.LogWarning("Cannot process forfeit: no opponent found in match {MatchName}", match.Name);
                    return false;
                }

                // Mark opponent as winner
                opponentParticipant.IsWinner = true;

                // Create match result
                match.Result = new Tournament.MatchResult
                {
                    Winner = opponentParticipant.Player,
                    CompletedAt = DateTime.Now,
                    Status = MatchStatus.Completed,
                    ResultType = MatchResultType.Forfeit,
                    Forfeiter = forfeitingParticipant.Player
                };

                _logger.LogInformation("Successfully processed forfeit for match {MatchName}. {WinnerName} wins by forfeit.",
                    match.Name, _groupService.GetPlayerDisplayName(opponentParticipant.Player));

                // Update bracket advancement with this result
                return UpdateBracketAdvancement(tournament, match);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing forfeit for match {MatchName} in tournament {TournamentName}",
                    match?.Name ?? "unknown", tournament?.Name ?? "unknown");
                return false;
            }
        }

        /// <summary>
        /// Gets visualization data for a tournament bracket
        /// </summary>
        /// <param name="tournament">The tournament to visualize</param>
        /// <returns>A dictionary containing visualization data</returns>
        public Dictionary<string, object> GetBracketVisualizationData(Tournament tournament)
        {
            var visualizationData = new Dictionary<string, object>();

            if (tournament == null || tournament.PlayoffMatches == null || tournament.PlayoffMatches.Count == 0)
            {
                return visualizationData;
            }

            try
            {
                // Organize matches by type
                var rounds = new Dictionary<TournamentMatchType, List<Tournament.Match>>();
                foreach (var match in tournament.PlayoffMatches)
                {
                    if (match == null) continue;

                    if (!rounds.ContainsKey(match.Type))
                    {
                        rounds[match.Type] = new List<Tournament.Match>();
                    }
                    rounds[match.Type].Add(match);
                }

                // Add rounds to visualization data
                visualizationData["Rounds"] = rounds;

                // Add number of rounds
                visualizationData["RoundCount"] = rounds.Count;

                // Track if there's a third place match
                bool hasThirdPlaceMatch = rounds.ContainsKey(TournamentMatchType.ThirdPlaceTiebreaker) &&
                                         rounds[TournamentMatchType.ThirdPlaceTiebreaker].Count > 0;

                visualizationData["HasThirdPlaceMatch"] = hasThirdPlaceMatch;

                // Add final match (if it exists)
                var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
                if (finalMatch != null)
                {
                    visualizationData["FinalMatch"] = finalMatch;

                    if (finalMatch.Result != null && finalMatch.Result.Winner != null)
                    {
                        visualizationData["Champion"] = finalMatch.Result.Winner;
                    }
                }

                // Add third place match (if it exists)
                var thirdPlaceMatch = tournament.PlayoffMatches.FirstOrDefault(m =>
                    m.Type == TournamentMatchType.ThirdPlaceTiebreaker);

                if (thirdPlaceMatch != null)
                {
                    visualizationData["ThirdPlaceMatch"] = thirdPlaceMatch;

                    // Since third place match could be created later, mark it explicitly
                    visualizationData["ThirdPlaceMatchFormat"] = $"Best of {thirdPlaceMatch.BestOf}";

                    if (thirdPlaceMatch.Result != null && thirdPlaceMatch.Result.Winner != null)
                    {
                        visualizationData["ThirdPlace"] = thirdPlaceMatch.Result.Winner;
                    }

                    // Add information about how this match was created
                    bool wasCreatedOnDemand = !tournament.Settings?.IncludeThirdPlaceMatch ?? false;
                    visualizationData["ThirdPlaceMatchCreatedOnDemand"] = wasCreatedOnDemand;
                }

                return visualizationData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bracket visualization data for tournament {TournamentName}", tournament.Name);
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Checks if all semifinals in the tournament are completed
        /// </summary>
        /// <param name="tournament">The tournament to check</param>
        /// <returns>True if all semifinals are completed, false otherwise</returns>
        public bool AreSemifinalsCompleted(Tournament tournament)
        {
            if (tournament == null || tournament.PlayoffMatches == null)
            {
                _logger.LogError("Cannot check semifinals status: Tournament or playoff matches is null");
                return false;
            }

            try
            {
                // Find the final match first
                var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m?.Type == TournamentMatchType.Final);
                if (finalMatch == null)
                {
                    _logger.LogInformation("Cannot determine semifinals: No final match found in tournament {TournamentName}", tournament.Name);
                    return false;
                }

                // Find matches leading to the final (more precise than just looking for TournamentMatchType.Semifinal)
                var semifinalMatches = tournament.PlayoffMatches
                    .Where(m => m != null && m.NextMatch == finalMatch)
                    .ToList();

                // If there are no semifinals, they can't be completed
                if (!semifinalMatches.Any())
                {
                    _logger.LogInformation("No semifinal matches found in tournament {TournamentName}", tournament.Name);
                    return false;
                }

                // There should be exactly 2 semifinal matches
                if (semifinalMatches.Count != 2)
                {
                    _logger.LogInformation("Expected 2 semifinal matches, found {Count} in tournament {TournamentName}",
                        semifinalMatches.Count, tournament.Name);
                    return false;
                }

                // Check if all semifinals are completed
                bool allCompleted = semifinalMatches.All(m => m.IsComplete);

                _logger.LogInformation("Semifinals completed status for tournament {TournamentName}: {Status}",
                    tournament.Name, allCompleted);

                return allCompleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if semifinals are completed for tournament {TournamentName}", tournament.Name);
                return false;
            }
        }

        /// <summary>
        /// Checks if a third place match can be created for the tournament
        /// </summary>
        /// <param name="tournament">The tournament to check</param>
        /// <returns>True if a third place match can be created, false otherwise</returns>
        public bool CanCreateThirdPlaceMatch(Tournament tournament)
        {
            if (tournament == null || tournament.PlayoffMatches == null)
            {
                _logger.LogError("Cannot check third place match eligibility: Tournament or playoff matches is null");
                return false;
            }

            try
            {
                // Verify all semifinals are completed
                if (!AreSemifinalsCompleted(tournament))
                {
                    _logger.LogInformation("Cannot create third place match: Not all semifinals are completed");
                    return false;
                }

                // Check if third place match already exists
                var thirdPlaceExists = tournament.PlayoffMatches
                    .Any(m => m != null && m.Type == TournamentMatchType.ThirdPlaceTiebreaker);

                if (thirdPlaceExists)
                {
                    _logger.LogInformation("Cannot create third place match: Third place match already exists");
                    return false;
                }

                // Verify that we have two semifinal matches
                var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m?.Type == TournamentMatchType.Final);
                if (finalMatch == null)
                {
                    _logger.LogInformation("Cannot create third place match: Final match not found");
                    return false;
                }

                var semifinals = tournament.PlayoffMatches.Where(m => m?.NextMatch == finalMatch).ToList();
                if (semifinals.Count != 2)
                {
                    _logger.LogInformation("Cannot create third place match: Expected 2 semifinal matches, found {Count}", semifinals.Count);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if third place match can be created for tournament {TournamentName}", tournament.Name);
                return false;
            }
        }

        /// <summary>
        /// Creates a third place match on demand if all semifinals are completed
        /// </summary>
        /// <param name="tournament">The tournament to create the third place match for</param>
        /// <param name="requestedByUserId">The Discord ID of the admin/moderator who requested the match</param>
        /// <returns>True if the match was created successfully, false otherwise</returns>
        public Task<bool> CreateThirdPlaceMatchOnDemand(Tournament tournament, ulong requestedByUserId)
        {
            if (tournament == null || tournament.PlayoffMatches == null)
            {
                _logger.LogError("Cannot create third place match: Tournament or playoff matches are null");
                return Task.FromResult(false);
            }

            try
            {
                // Check if we can create a third place match
                if (!CanCreateThirdPlaceMatch(tournament))
                {
                    return Task.FromResult(false);
                }

                // Create the third place match with improved error handling
                var thirdPlaceMatch = CreateThirdPlaceMatch(tournament);

                if (thirdPlaceMatch != null)
                {
                    _logger.LogInformation("Third place match created on demand for tournament {TournamentName} by user {UserId}",
                        tournament.Name, requestedByUserId);

                    // Return successful match details
                    return Task.FromResult(true);
                }

                _logger.LogWarning("Failed to create third place match for tournament {TournamentName}", tournament.Name);
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating third place match on demand for tournament {TournamentName}", tournament.Name);
                return Task.FromResult(false);
            }
        }
    }
}