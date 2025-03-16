using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.Threading.Tasks;
using Wabbit.Services.ServiceHelpers;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament playoff management
    /// </summary>
    public class TournamentPlayoffService : ITournamentPlayoffService
    {
        private readonly ILogger<TournamentPlayoffService> _logger;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentStateValidator _stateValidator;
        private readonly ITournamentBracketManager _bracketManager;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="groupService">Service for accessing group data</param>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateValidator">Service for validating tournament state transitions</param>
        /// <param name="bracketManager">Service for managing tournament brackets</param>
        public TournamentPlayoffService(
            ITournamentGroupService groupService,
            ILogger<TournamentPlayoffService> logger,
            ITournamentStateValidator stateValidator,
            ITournamentBracketManager bracketManager)
        {
            _groupService = groupService;
            _logger = logger;
            _stateValidator = stateValidator;
            _bracketManager = bracketManager;
        }

        /// <summary>
        /// Implements the async interface method by wrapping the synchronous implementation
        /// </summary>
        public Task SetupPlayoffsAsync(Tournament tournament, DiscordClient client)
        {
            SetupPlayoffs(tournament);
            return Task.CompletedTask;
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

            // Validate state transition
            if (!_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
            {
                _logger.LogWarning($"Invalid state transition from {tournament.CurrentStage} to Playoffs for tournament {tournament.Name}");
                return;
            }

            // Mark the tournament as being in the playoff stage
            tournament.CurrentStage = TournamentStage.Playoffs;

            // Clear existing playoff matches
            tournament.PlayoffMatches.Clear();

            try
            {
                // Get qualified participants
                var qualifiedParticipants = GetQualifiedParticipants(tournament, groupWinners, bestThirdPlace);

                // Create the playoff bracket using the bracket manager
                var matches = _bracketManager.CreatePlayoffBracket(tournament, qualifiedParticipants);
                tournament.PlayoffMatches.AddRange(matches);

                // Link the matches in the bracket
                _bracketManager.LinkBracketMatches(tournament, matches);

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
                throw;
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

                // Use the bracket manager to handle advancement
                bool success = _bracketManager.UpdateBracketAdvancement(tournament, match);

                // Handle third place match advancement if this is a semifinal
                if (success && match.ThirdPlaceMatch != null)
                {
                    HandleThirdPlaceAdvancement(tournament, match);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating bracket advancement for match {match.Id}");
                return false;
            }
        }

        private void HandleThirdPlaceAdvancement(Tournament tournament, Tournament.Match semifinalMatch)
        {
            if (semifinalMatch.Result?.Winner == null) return;

            // Find the losing participant
            var losingParticipant = semifinalMatch.Participants.FirstOrDefault(p =>
                (p.Player as DiscordUser)?.Id != (semifinalMatch.Result.Winner as DiscordUser)?.Id);

            if (losingParticipant == null) return;

            // Find the slot in the third place match
            var semifinals = tournament.PlayoffMatches
                .Where(m => m.Type == TournamentMatchType.Semifinal)
                .OrderBy(m => m.DisplayPosition)
                .ToList();

            int slot = semifinals.IndexOf(semifinalMatch);
            if (slot >= 0 && slot < 2 && semifinalMatch.ThirdPlaceMatch?.Participants != null)
            {
                semifinalMatch.ThirdPlaceMatch.Participants[slot] = new Tournament.MatchParticipant
                {
                    Player = losingParticipant.Player,
                    SourceMatch = semifinalMatch
                };

                // Update third place match name
                UpdateThirdPlaceMatchName(semifinalMatch.ThirdPlaceMatch);
            }
        }

        private void UpdateThirdPlaceMatchName(Tournament.Match thirdPlaceMatch)
        {
            if (thirdPlaceMatch.Participants[0]?.Player != null && thirdPlaceMatch.Participants[1]?.Player != null)
            {
                thirdPlaceMatch.Name = $"{_groupService.GetPlayerDisplayName(thirdPlaceMatch.Participants[0].Player)} vs {_groupService.GetPlayerDisplayName(thirdPlaceMatch.Participants[1].Player)}";
            }
            else if (thirdPlaceMatch.Participants[0]?.Player != null)
            {
                thirdPlaceMatch.Name = $"{_groupService.GetPlayerDisplayName(thirdPlaceMatch.Participants[0].Player)} vs TBD";
            }
            else if (thirdPlaceMatch.Participants[1]?.Player != null)
            {
                thirdPlaceMatch.Name = $"TBD vs {_groupService.GetPlayerDisplayName(thirdPlaceMatch.Participants[1].Player)}";
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
                    .FirstOrDefault(m => m.Type == TournamentMatchType.PlayoffThirdPlace);

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
                    Type = TournamentMatchType.PlayoffThirdPlace,
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
        /// Gets advancement criteria for playoff stage based on player and group count
        /// </summary>
        public (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
        {
            return (playerCount, groupCount) switch
            {
                // Single group formats
                (7, 1) => (4, 0),  // Top 4 advance to playoffs

                // Two group formats
                (8, 2) => (2, 0),  // Top 2 from each group
                (10, 2) => (2, 0), // Top 2 from each group
                (14, 2) => (4, 0), // Top 4 from each group

                // Three group formats
                (9, 3) => (2, 2),   // Top 2 + best 2 third-place
                (11, 3) => (2, 2),  // Top 2 + best 2 third-place
                (12, 3) => (2, 2),  // Top 2 + best 2 third-place
                (13, 3) => (2, 2),  // Top 2 + best 2 third-place
                (15, 3) => (2, 2),  // Top 2 + best 2 third-place
                (17, 3) => (2, 2),  // Top 2 + best 2 third-place
                (18, 3) => (2, 2),  // Top 2 + best 2 third-place

                // Four group formats
                (16, 4) => (2, 0),  // Top 2 from each group
                (19, 4) => (2, 0),  // Top 2 from each group
                (20, 4) => (2, 0),  // Top 2 from each group

                // Six group formats (21-30 players)
                ( >= 21 and <= 30, 6) => (2, 4),  // Top 2 + best 4 third-place

                // Eight group formats (31-32 players)
                ( >= 31 and <= 32, 8) => (2, 0),  // Top 2 from each group

                // Default case - use standard criteria
                _ => (2, 0)  // Default to top 2 from each group
            };
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
                bool hasThirdPlaceMatch = rounds.ContainsKey(TournamentMatchType.PlayoffThirdPlace) &&
                                         rounds[TournamentMatchType.PlayoffThirdPlace].Count > 0;

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
                    m.Type == TournamentMatchType.PlayoffThirdPlace);

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
                    .Any(m => m != null && m.Type == TournamentMatchType.PlayoffThirdPlace);

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

        /// <summary>
        /// Starts playoff matches asynchronously
        /// </summary>
        public async Task StartPlayoffMatchesAsync(Tournament tournament, DiscordClient client)
        {
            // This is a placeholder that needs to be implemented with actual playoff match starting logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Updates bracket advancement asynchronously
        /// </summary>
        public async Task<bool> UpdateBracketAdvancementAsync(Tournament tournament, Tournament.Match match)
        {
            return await Task.Run(() => UpdateBracketAdvancement(tournament, match));
        }

        /// <summary>
        /// Processes a forfeit asynchronously
        /// </summary>
        public async Task<bool> ProcessForfeitAsync(Tournament tournament, Tournament.Match match, object forfeitingPlayer)
        {
            return await Task.Run(() => ProcessForfeit(tournament, match, forfeitingPlayer));
        }

        /// <summary>
        /// Creates a third place match asynchronously
        /// </summary>
        public async Task<bool> CreateThirdPlaceMatchAsync(Tournament tournament, ulong requestedByUserId)
        {
            return await CreateThirdPlaceMatchOnDemand(tournament, requestedByUserId);
        }

        /// <summary>
        /// Determines if a match is a playoff match
        /// </summary>
        public bool IsPlayoffMatch(Tournament.Match match)
        {
            return match.Type is TournamentMatchType.Quarterfinal or
                               TournamentMatchType.Semifinal or
                               TournamentMatchType.Final or
                               TournamentMatchType.PlayoffThirdPlace;
        }

        /// <summary>
        /// Gets the playoff stage for a match
        /// </summary>
        public TournamentMatchType GetPlayoffStage(Tournament tournament, Tournament.Match match)
        {
            return match.Type;
        }

        /// <summary>
        /// Gets the default match length for a playoff stage
        /// </summary>
        public int GetDefaultMatchLength(TournamentMatchType matchType)
        {
            return matchType switch
            {
                TournamentMatchType.Quarterfinal => 3,  // Best of 3
                TournamentMatchType.Semifinal => 3,     // Best of 3
                TournamentMatchType.Final => 3,         // Best of 3
                TournamentMatchType.PlayoffThirdPlace => 3,    // Best of 3
                _ => 3                                  // Default to Best of 3
            };
        }
    }
}