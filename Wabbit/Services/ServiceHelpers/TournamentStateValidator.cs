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
    /// Validates tournament states and transitions
    /// </summary>
    public class TournamentStateValidator : ITournamentStateValidator
    {
        private readonly ILogger<TournamentStateValidator> _logger;

        public TournamentStateValidator(ILogger<TournamentStateValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Validates the current state and data integrity of a tournament
        /// </summary>
        public List<string> ValidateTournamentState(Tournament? tournament)
        {
            var errors = new List<string>();

            if (tournament == null)
            {
                errors.Add("Tournament object is null");
                return errors;
            }

            if (string.IsNullOrEmpty(tournament.Name))
            {
                errors.Add("Tournament name is missing");
            }

            if (tournament.Settings == null)
            {
                errors.Add("Tournament settings are missing");
                return errors;
            }

            // Validate settings first
            errors.AddRange(ValidateSettings(tournament));

            // Validate based on current stage
            switch (tournament.CurrentStage)
            {
                case TournamentStage.SignupOpen:
                    errors.AddRange(ValidateSignupPhase(tournament));
                    break;
                case TournamentStage.Groups:
                    errors.AddRange(ValidateGroupStagePhase(tournament));
                    break;
                case TournamentStage.Playoffs:
                    errors.AddRange(ValidatePlayoffPhase(tournament));
                    break;
                case TournamentStage.Complete:
                    errors.AddRange(ValidateCompletedState(tournament));
                    break;
                default:
                    errors.Add($"Invalid tournament stage: {tournament.CurrentStage}");
                    break;
            }

            return errors;
        }

        /// <summary>
        /// Determines if a transition between tournament states is valid
        /// </summary>
        public bool IsValidStateTransition(Tournament tournament, TournamentStage newState)
        {
            if (tournament == null)
            {
                _logger.LogError("Cannot validate state transition: tournament is null");
                return false;
            }

            // Define valid transitions
            var validTransitions = new Dictionary<TournamentStage, HashSet<TournamentStage>>
            {
                [TournamentStage.SignupOpen] = new() { TournamentStage.Groups },
                [TournamentStage.Groups] = new() { TournamentStage.Playoffs },
                [TournamentStage.Playoffs] = new() { TournamentStage.Complete },
                [TournamentStage.Complete] = new() { }
            };

            if (!validTransitions.ContainsKey(tournament.CurrentStage))
            {
                _logger.LogError($"Invalid current tournament stage: {tournament.CurrentStage}");
                return false;
            }

            bool isValid = validTransitions[tournament.CurrentStage].Contains(newState);
            if (!isValid)
            {
                _logger.LogError($"Invalid state transition from {tournament.CurrentStage} to {newState}");
            }

            return isValid;
        }

        /// <summary>
        /// Validates a match's state and data
        /// </summary>
        public List<string> ValidateMatch(Tournament? tournament, Tournament.Match? match)
        {
            var errors = new List<string>();
            var technicalErrors = new List<string>();  // For logging purposes

            if (tournament == null)
            {
                errors.Add("Tournament object is null");
                return errors;
            }

            if (match == null)
            {
                var error = "Unable to find the match data";
                errors.Add(error);
                technicalErrors.Add("Match object is null");
                return errors;
            }

            if (string.IsNullOrEmpty(match.Id))
            {
                var error = "The match is missing its identifier";
                errors.Add(error);
                technicalErrors.Add("Match ID is missing");
            }

            // Validate match type and format
            if (!Enum.IsDefined(typeof(TournamentMatchType), match.Type))
            {
                var error = "The match has an invalid type";
                errors.Add(error);
                technicalErrors.Add($"Invalid match type: {match.Type}");
            }

            if (match.BestOf <= 0 || match.BestOf % 2 == 0)
            {
                var error = $"The match format (Best of {match.BestOf}) is invalid - it must be a positive odd number";
                errors.Add(error);
                technicalErrors.Add($"Invalid match format: Bo{match.BestOf} (must be a positive odd number)");
            }

            // Validate participants
            if (match.Participants == null)
            {
                var error = "No players are assigned to this match";
                errors.Add(error);
                technicalErrors.Add("Match participants collection is null");
            }

            if ((match.Participants?.Count ?? 0) != 2)
            {
                var error = $"This match has {match.Participants?.Count ?? 0} players, but exactly 2 players are required";
                errors.Add(error);
                technicalErrors.Add($"Invalid number of participants: expected 2, got {match.Participants?.Count ?? 0}");
            }

            // Validate participant data
            for (int i = 0; i < (match.Participants?.Count ?? 0); i++)
            {
                var participant = match.Participants?[i];
                if (participant == null)
                {
                    var error = $"Player {i + 1}'s data is missing";
                    errors.Add(error);
                    technicalErrors.Add($"Participant {i + 1} is null");
                    continue;
                }

                if (participant.Player == null)
                {
                    var error = $"Player {i + 1}'s information is incomplete";
                    errors.Add(error);
                    technicalErrors.Add($"Participant {i + 1} has no player data");
                    continue;
                }

                if (participant.Player is not DiscordUser user)
                {
                    var error = $"Player {i + 1}'s Discord account information is invalid";
                    errors.Add(error);
                    technicalErrors.Add($"Participant {i + 1} has invalid player type: {participant.Player.GetType().Name}");
                    continue;
                }

                if (user.Id == 0)
                {
                    var error = $"Player {user.Username}'s Discord ID is invalid";
                    errors.Add(error);
                    technicalErrors.Add($"Participant {i + 1} ({user.Username}) has invalid Discord ID");
                }

                // Validate source match for playoff matches
                if (match.Type != TournamentMatchType.GroupStage && participant.SourceMatch == null)
                {
                    var error = $"Cannot verify how {user.Username} qualified for this {match.Type.ToString().ToLowerInvariant()} match";
                    errors.Add(error);
                    technicalErrors.Add($"Participant {i + 1} ({user.Username}) is missing source match in {match.Type} stage");
                }
            }

            // Validate match result if present
            if (match.Result != null)
            {
                if (match.Result.Winner == null)
                {
                    var error = "The match result is missing the winner";
                    errors.Add(error);
                    technicalErrors.Add("Match result has no winner");
                }
                else if (match.Result.Winner is not DiscordUser winner)
                {
                    var error = "The winner's Discord account information is invalid";
                    errors.Add(error);
                    technicalErrors.Add($"Invalid winner type: {match.Result.Winner.GetType().Name}");
                }
                else
                {
                    var winningParticipant = match.Participants?.FirstOrDefault(p =>
                        (p?.Player as DiscordUser)?.Id == winner.Id);

                    if (winningParticipant == null)
                    {
                        errors.Add($"Match winner ({winner.Username}) is not a participant in the match");
                    }
                }

                // Validate scores
                if (match.Result.WinnerScore < 0)
                {
                    errors.Add($"Invalid winner score: {match.Result.WinnerScore}");
                }

                if (match.Result.LoserScore < 0)
                {
                    errors.Add($"Invalid loser score: {match.Result.LoserScore}");
                }

                // Validate score consistency
                var maxScore = (match.BestOf + 1) / 2;
                var totalGames = match.Result.WinnerScore + match.Result.LoserScore;

                if (match.Result.WinnerScore < maxScore)
                {
                    errors.Add($"Winner score ({match.Result.WinnerScore}) is insufficient for Bo{match.BestOf} format (needs {maxScore})");
                }

                if (totalGames > match.BestOf)
                {
                    errors.Add($"Total games played ({totalGames}) exceeds Bo{match.BestOf} format limit");
                }

                if (match.Result.WinnerScore <= match.Result.LoserScore)
                {
                    errors.Add($"Winner's score ({match.Result.WinnerScore}) must be greater than loser's score ({match.Result.LoserScore})");
                }
            }

            if (errors.Any())
            {
                var matchDesc = $"{match.Type} match {match.Id}";
                _logger.LogError($"Validation failed for {matchDesc} with {errors.Count} errors");
                foreach (var error in errors)
                {
                    _logger.LogError($"{matchDesc}: {error}");
                }
            }

            return errors;
        }

        /// <summary>
        /// Validates the structure of playoff matches to ensure correct connections
        /// </summary>
        public List<string> ValidateBracketStructure(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament?.PlayoffMatches == null || !tournament.PlayoffMatches.Any())
            {
                errors.Add("No playoff matches found");
                return errors;
            }

            // Get matches by round
            var matchesByRound = tournament.PlayoffMatches
                .GroupBy(m => m.Type)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Validate round sequence
            var expectedSequence = new[]
            {
                TournamentMatchType.RoundOf16,
                TournamentMatchType.Quarterfinal,
                TournamentMatchType.Semifinal,
                TournamentMatchType.Final
            };

            var actualRounds = matchesByRound.Keys
                .Where(k => k != TournamentMatchType.PlayoffThirdPlace)
                .OrderBy(k => k)
                .ToList();

            var firstRoundIndex = Array.IndexOf(expectedSequence, actualRounds.FirstOrDefault());
            if (firstRoundIndex == -1)
            {
                errors.Add("Invalid starting playoff round");
                return errors;
            }

            for (int i = 0; i < actualRounds.Count - 1; i++)
            {
                var currentRound = actualRounds[i];
                var nextRound = actualRounds[i + 1];
                var expectedNextRound = expectedSequence[firstRoundIndex + i + 1];

                if (nextRound != expectedNextRound)
                {
                    errors.Add($"Invalid round sequence: {currentRound} followed by {nextRound}, expected {expectedNextRound}");
                }
            }

            // Validate number of matches in each round
            foreach (var round in matchesByRound)
            {
                var expectedMatches = round.Key switch
                {
                    TournamentMatchType.RoundOf16 => 8,
                    TournamentMatchType.Quarterfinal => 4,
                    TournamentMatchType.Semifinal => 2,
                    TournamentMatchType.Final => 1,
                    TournamentMatchType.PlayoffThirdPlace => 1,
                    _ => 0
                };

                if (round.Value.Count != expectedMatches)
                {
                    errors.Add($"Invalid number of matches in {round.Key}: expected {expectedMatches}, got {round.Value.Count}");
                }

                // Validate match formats
                var expectedFormat = round.Key switch
                {
                    TournamentMatchType.Final => tournament.Settings?.BestOfFinals ?? 3,
                    TournamentMatchType.Semifinal => tournament.Settings?.BestOfSemifinals ?? 3,
                    TournamentMatchType.Quarterfinal => tournament.Settings?.BestOfQuarterfinals ?? 3,
                    _ => tournament.Settings?.BestOfQuarterfinals ?? 3
                };

                foreach (var match in round.Value)
                {
                    if (match.BestOf != expectedFormat)
                    {
                        errors.Add($"Incorrect match format in {round.Key}: Bo{match.BestOf}, expected Bo{expectedFormat}");
                    }
                }
            }

            // Validate match connections
            foreach (var match in tournament.PlayoffMatches.Where(m => m.Type != TournamentMatchType.Final))
            {
                if (match.NextMatch == null)
                {
                    errors.Add($"Match {match.Id} ({match.Type}) has no next match connection");
                    continue;
                }

                if (!tournament.PlayoffMatches.Contains(match.NextMatch))
                {
                    errors.Add($"Match {match.Id} ({match.Type}) has invalid next match connection");
                    continue;
                }

                // Validate correct progression
                var expectedNextType = match.Type switch
                {
                    TournamentMatchType.RoundOf16 => (TournamentMatchType?)TournamentMatchType.Quarterfinal,
                    TournamentMatchType.Quarterfinal => (TournamentMatchType?)TournamentMatchType.Semifinal,
                    TournamentMatchType.Semifinal => (TournamentMatchType?)TournamentMatchType.Final,
                    _ => (TournamentMatchType?)null
                };

                if (match.NextMatch.Type != expectedNextType)
                {
                    errors.Add($"Invalid progression from {match.Type} to {match.NextMatch.Type}, expected {expectedNextType}");
                }
            }

            return errors;
        }

        private List<string> ValidateSignupPhase(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament.SignedUpPlayers == null)
            {
                errors.Add("No signed up players list");
                return errors;
            }

            var playerCount = tournament.SignedUpPlayers?.Count ?? 0;
            var minPlayers = tournament.Settings?.MinPlayers ?? 0;
            var maxPlayers = tournament.Settings?.MaxPlayers ?? int.MaxValue;

            if (minPlayers <= 0)
            {
                errors.Add("Tournament settings have an invalid minimum player count");
            }

            if (maxPlayers < minPlayers)
            {
                errors.Add($"Invalid player limits: maximum ({maxPlayers}) cannot be less than minimum ({minPlayers})");
            }

            if (playerCount < minPlayers)
            {
                errors.Add($"Not enough players signed up: {playerCount}/{minPlayers} required");
            }

            if (playerCount > maxPlayers)
            {
                errors.Add($"Too many players signed up: {playerCount}/{maxPlayers} maximum");
            }

            // Validate player data integrity
            var invalidPlayers = tournament.SignedUpPlayers?.Where(p => p is null || (p is DiscordUser user && user.Id == 0))
                .ToList() ?? [];

            if (invalidPlayers.Any())
            {
                errors.Add($"Found {invalidPlayers.Count} invalid player entries");
                _logger.LogError($"Invalid player data detected in tournament {tournament.Name}");
            }

            return errors;
        }

        private List<string> ValidateGroupStagePhase(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament.Groups == null || !tournament.Groups.Any())
            {
                errors.Add("No groups defined");
                return errors;
            }

            // Validate group count and distribution
            var totalPlayers = tournament.SignedUpPlayers?.Count ?? 0;
            if (totalPlayers == 0)
            {
                errors.Add("No players found for group distribution");
                return errors;
            }

            // Instead of using fixed sizes, validate that groups are reasonably balanced
            var minGroupSize = tournament.Groups.Min(g => g.Participants?.Count ?? 0);
            var maxGroupSize = tournament.Groups.Max(g => g.Participants?.Count ?? 0);

            if (maxGroupSize - minGroupSize > 1)
            {
                errors.Add($"Groups are not balanced: sizes range from {minGroupSize} to {maxGroupSize}");
            }

            var groupNames = new HashSet<string>();
            foreach (var group in tournament.Groups)
            {
                // Validate group name
                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    errors.Add("Found group with missing name");
                    continue;
                }

                if (!groupNames.Add(group.Name))
                {
                    errors.Add($"Duplicate group name found: {group.Name}");
                    continue;
                }

                // Validate group participants
                if (group.Participants == null)
                {
                    errors.Add($"Group {group.Name} has null participants collection");
                    continue;
                }

                if (!group.Participants.Any())
                {
                    errors.Add($"Group {group.Name} has no participants");
                    continue;
                }

                // Validate participant data and match records
                var seenPlayers = new HashSet<ulong>();
                foreach (var participant in group.Participants)
                {
                    if (participant == null)
                    {
                        errors.Add($"Found null participant in group {group.Name}");
                        continue;
                    }

                    if (participant.Player == null)
                    {
                        errors.Add($"Found participant with no player data in group {group.Name}");
                        continue;
                    }

                    if (participant.Player is not DiscordUser user)
                    {
                        errors.Add($"Invalid player type in group {group.Name}: {participant.Player.GetType().Name}");
                        continue;
                    }

                    if (user.Id == 0)
                    {
                        errors.Add($"Invalid Discord ID for player {user.Username} in group {group.Name}");
                        continue;
                    }

                    // Check for duplicate players
                    if (!seenPlayers.Add(user.Id))
                    {
                        errors.Add($"Player {user.Username} appears multiple times in group {group.Name}");
                        continue;
                    }

                    // Validate match records
                    if (participant.Wins < 0)
                    {
                        errors.Add($"Invalid win count for {user.Username} in group {group.Name}: {participant.Wins}");
                    }

                    if (participant.Losses < 0)
                    {
                        errors.Add($"Invalid loss count for {user.Username} in group {group.Name}: {participant.Losses}");
                    }

                    var totalMatches = participant.Wins + participant.Losses;
                    var expectedMatches = group.Participants.Count - 1; // Each player should play against every other player once
                    if (group.IsComplete && totalMatches != expectedMatches)
                    {
                        errors.Add($"Incorrect match count for {user.Username} in completed group {group.Name}: played {totalMatches}, expected {expectedMatches}");
                    }
                }

                // Validate group matches
                if (group.Matches == null)
                {
                    errors.Add($"Group {group.Name} has null matches collection");
                    continue;
                }

                // Validate match count
                var expectedMatchCount = (group.Participants.Count * (group.Participants.Count - 1)) / 2; // n(n-1)/2 for round robin
                if (group.IsComplete && group.Matches.Count != expectedMatchCount)
                {
                    errors.Add($"Incorrect total match count in completed group {group.Name}: {group.Matches.Count}/{expectedMatchCount}");
                }

                // Validate each match
                foreach (var match in group.Matches)
                {
                    if (match == null)
                    {
                        errors.Add($"Found null match in group {group.Name}");
                        continue;
                    }

                    if (match.Type != TournamentMatchType.GroupStage)
                    {
                        errors.Add($"Invalid match type in group {group.Name}: {match.Type}");
                    }

                    if (match.BestOf != tournament.Settings?.BestOfGroupStage)
                    {
                        errors.Add($"Incorrect match format in group {group.Name}: Bo{match.BestOf}, expected Bo{tournament.Settings?.BestOfGroupStage}");
                    }
                }
            }

            // Log validation results
            if (errors.Any())
            {
                _logger.LogError($"Group stage validation failed with {errors.Count} errors in tournament {tournament.Name}");
                foreach (var error in errors)
                {
                    _logger.LogError($"Group stage error: {error}");
                }
            }

            return errors;
        }

        private List<string> ValidatePlayoffPhase(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament.PlayoffMatches == null || !tournament.PlayoffMatches.Any())
            {
                errors.Add("No playoff matches defined");
                return errors;
            }

            // Validate bracket structure
            errors.AddRange(ValidateBracketStructure(tournament));

            // Validate third place match if enabled
            if (tournament.Settings?.IncludeThirdPlaceMatch == true)
            {
                var thirdPlaceMatches = tournament.PlayoffMatches
                    .Where(m => m.Type == TournamentMatchType.PlayoffThirdPlace)
                    .ToList();

                if (!thirdPlaceMatches.Any())
                {
                    errors.Add("Third place match is enabled but not created");
                }
                else if (thirdPlaceMatches.Count > 1)
                {
                    errors.Add($"Multiple third place matches found: {thirdPlaceMatches.Count}");
                }
            }

            // Validate match progression
            var completedMatches = tournament.PlayoffMatches
                .Where(m => m.Result != null)
                .ToList();

            foreach (var match in completedMatches)
            {
                if (match.NextMatch != null)
                {
                    var winner = match.Result?.Winner as DiscordUser;
                    var advancedPlayer = match.NextMatch.Participants
                        .FirstOrDefault(p => (p.Player as DiscordUser)?.Id == winner?.Id);

                    if (advancedPlayer == null)
                    {
                        errors.Add($"Winner of match {match.Id} ({winner?.Username ?? "Unknown"}) not found in next match {match.NextMatch.Id}");
                    }
                }
            }

            // Validate seeding integrity
            var playoffParticipants = tournament.PlayoffMatches
                .SelectMany(m => m.Participants)
                .Where(p => p?.Player != null)
                .ToList();

            var duplicatePlayers = playoffParticipants
                .GroupBy(p => (p.Player as DiscordUser)?.Id)
                .Where(g => g.Key.HasValue && g.Count() > g.Count(p => p.SourceMatch != null))
                .ToList();

            if (duplicatePlayers.Any())
            {
                foreach (var duplicate in duplicatePlayers)
                {
                    var player = duplicate.First().Player as DiscordUser;
                    errors.Add($"Player {player?.Username ?? "Unknown"} appears in multiple playoff matches without proper advancement");
                }
            }

            // Log validation results
            if (errors.Any())
            {
                _logger.LogError($"Playoff validation failed with {errors.Count} errors in tournament {tournament.Name}");
                foreach (var error in errors)
                {
                    _logger.LogError($"Playoff error: {error}");
                }
            }

            return errors;
        }

        private List<string> ValidateCompletedState(Tournament tournament)
        {
            var errors = new List<string>();

            // Ensure there is a winner
            var finalMatch = tournament.PlayoffMatches?.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
            if (finalMatch == null)
            {
                errors.Add("No final match found");
            }
            else if (finalMatch.Result?.Winner == null)
            {
                errors.Add("Final match has no winner");
            }

            // Ensure all matches are complete
            if (tournament.PlayoffMatches?.Any(m => m.Result == null) == true)
            {
                errors.Add("Not all playoff matches are complete");
            }

            return errors;
        }

        private List<string> ValidateSettings(Tournament tournament)
        {
            var errors = new List<string>();
            var settings = tournament.Settings;

            if (settings == null)
            {
                errors.Add("Tournament settings are null");
                return errors;
            }

            if (settings.MinPlayers <= 1)
            {
                errors.Add($"Minimum players must be greater than 1 (currently {settings.MinPlayers})");
            }

            if (settings.MaxPlayers < settings.MinPlayers)
            {
                errors.Add($"Maximum players ({settings.MaxPlayers}) cannot be less than minimum players ({settings.MinPlayers})");
            }

            if (settings.MaxPlayers > 128)
            {
                errors.Add($"Maximum players ({settings.MaxPlayers}) exceeds system limit of 128");
            }

            // Validate match formats
            if (settings.BestOfGroupStage % 2 == 0)
            {
                errors.Add($"Group stage match format (Bo{settings.BestOfGroupStage}) must be an odd number");
            }

            if (settings.BestOfQuarterfinals % 2 == 0)
            {
                errors.Add($"Quarterfinals match format (Bo{settings.BestOfQuarterfinals}) must be an odd number");
            }

            if (settings.BestOfSemifinals % 2 == 0)
            {
                errors.Add($"Semifinals match format (Bo{settings.BestOfSemifinals}) must be an odd number");
            }

            if (settings.BestOfFinals % 2 == 0)
            {
                errors.Add($"Finals match format (Bo{settings.BestOfFinals}) must be an odd number");
            }

            return errors;
        }

        /// <summary>
        /// Attempts to recover from a failed match result submission
        /// </summary>
        /// <returns>True if recovery was successful, false otherwise</returns>
        public bool TryRecoverMatchResult(Tournament tournament, Tournament.Match match, Tournament.MatchResult failedResult)
        {
            if (match == null || failedResult == null)
            {
                _logger.LogError("Cannot recover match result: match or result is null");
                return false;
            }

            try
            {
                // Store the previous state in case we need to rollback
                var previousResult = match.Result;
                var previousWinnerScore = previousResult?.WinnerScore;
                var previousLoserScore = previousResult?.LoserScore;
                var previousWinner = previousResult?.Winner;

                // Validate the failed result before attempting recovery
                var validationErrors = ValidateMatchResult(match, failedResult);
                if (validationErrors.Any())
                {
                    _logger.LogError($"Cannot recover invalid match result for match {match.Id}: {string.Join(", ", validationErrors)}");
                    return false;
                }

                // Attempt to apply the result
                match.Result = failedResult;

                // Validate the entire match state after applying the result
                var matchErrors = ValidateMatch(tournament, match);
                if (matchErrors.Any())
                {
                    // Rollback if validation fails
                    match.Result = previousResult;
                    _logger.LogError($"Match result recovery failed validation for match {match.Id}. Rolling back to previous state.");
                    return false;
                }

                _logger.LogInformation($"Successfully recovered match result for match {match.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during match result recovery for match {match.Id}");
                return false;
            }
        }

        /// <summary>
        /// Validates a match result before submission
        /// </summary>
        private List<string> ValidateMatchResult(Tournament.Match match, Tournament.MatchResult result)
        {
            var errors = new List<string>();

            if (result.Winner == null)
            {
                errors.Add("Match result is missing a winner");
                return errors;
            }

            if (result.Winner is not DiscordUser winner)
            {
                errors.Add("Winner must be a Discord user");
                return errors;
            }

            // Verify winner is a participant
            var winningParticipant = match.Participants?.FirstOrDefault(p =>
                (p?.Player as DiscordUser)?.Id == winner.Id);

            if (winningParticipant == null)
            {
                errors.Add($"Winner {winner.Username} is not a participant in this match");
                return errors;
            }

            // Validate scores
            if (result.WinnerScore < 0 || result.LoserScore < 0)
            {
                errors.Add("Scores cannot be negative");
                return errors;
            }

            var maxScore = (match.BestOf + 1) / 2;
            var totalGames = result.WinnerScore + result.LoserScore;

            if (result.WinnerScore < maxScore)
            {
                errors.Add($"Winner must have at least {maxScore} wins in a Best of {match.BestOf} match");
            }

            if (totalGames > match.BestOf)
            {
                errors.Add($"Total games ({totalGames}) cannot exceed match format (Best of {match.BestOf})");
            }

            if (result.WinnerScore <= result.LoserScore)
            {
                errors.Add("Winner's score must be greater than loser's score");
            }

            return errors;
        }

        /// <summary>
        /// Attempts to recover from bracket generation issues by validating and fixing the bracket structure
        /// </summary>
        public bool TryRecoverBracketGeneration(Tournament tournament)
        {
            if (tournament?.PlayoffMatches == null)
            {
                _logger.LogError("Cannot recover bracket: tournament or playoff matches are null");
                return false;
            }

            try
            {
                // Store the current state for potential rollback
                var originalMatches = tournament.PlayoffMatches.ToList();
                var errors = new List<string>();

                // Validate and fix match connections
                foreach (var match in tournament.PlayoffMatches.Where(m => m.Type != TournamentMatchType.Final))
                {
                    if (match.NextMatch == null)
                    {
                        var expectedNextType = match.Type switch
                        {
                            TournamentMatchType.RoundOf16 => (TournamentMatchType?)TournamentMatchType.Quarterfinal,
                            TournamentMatchType.Quarterfinal => (TournamentMatchType?)TournamentMatchType.Semifinal,
                            TournamentMatchType.Semifinal => (TournamentMatchType?)TournamentMatchType.Final,
                            _ => (TournamentMatchType?)null
                        };

                        if (expectedNextType.HasValue)
                        {
                            var potentialNextMatch = tournament.PlayoffMatches
                                .FirstOrDefault(m => m.Type == expectedNextType &&
                                    m.Participants?.Count(p => p?.Player != null) < 2);

                            if (potentialNextMatch != null)
                            {
                                match.NextMatch = potentialNextMatch;
                                _logger.LogInformation($"Recovered next match connection for {match.Type} match {match.Id}");
                            }
                            else
                            {
                                errors.Add($"Could not find valid next match for {match.Type} match {match.Id}");
                            }
                        }
                    }
                }

                // Validate third place match connections
                if (tournament.Settings?.IncludeThirdPlaceMatch == true)
                {
                    var semifinals = tournament.PlayoffMatches
                        .Where(m => m.Type == TournamentMatchType.Semifinal)
                        .ToList();

                    var thirdPlaceMatch = tournament.PlayoffMatches
                        .FirstOrDefault(m => m.Type == TournamentMatchType.PlayoffThirdPlace);

                    if (thirdPlaceMatch != null)
                    {
                        foreach (var semifinal in semifinals)
                        {
                            semifinal.ThirdPlaceMatch = thirdPlaceMatch;
                        }
                        _logger.LogInformation("Recovered third place match connections");
                    }
                }

                // Validate the recovered structure
                var structureErrors = ValidateBracketStructure(tournament);
                if (structureErrors.Any())
                {
                    // Rollback if validation fails
                    tournament.PlayoffMatches = originalMatches;
                    _logger.LogError($"Bracket recovery failed validation with {structureErrors.Count} errors");
                    return false;
                }

                return !errors.Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bracket generation recovery");
                return false;
            }
        }

        /// <summary>
        /// Validates and fixes participant seeding in the bracket
        /// </summary>
        private bool TryRecoverBracketSeeding(Tournament tournament)
        {
            if (tournament?.PlayoffMatches == null)
            {
                return false;
            }

            try
            {
                var firstRoundMatches = tournament.PlayoffMatches
                    .Where(m => !m.Participants.Any(p => p?.SourceMatch != null))
                    .OrderBy(m => m.DisplayPosition)
                    .ToList();

                var qualifiedPlayers = tournament.Groups?
                    .SelectMany(g => g.Participants?
                        .OrderByDescending(p => p.Wins)
                        .ThenByDescending(p => p.Wins - p.Losses)
                        .Take(2) ?? [])
                    .Where(p => p?.Player != null)
                    .ToList() ?? [];

                if (!qualifiedPlayers.Any() || !firstRoundMatches.Any())
                {
                    return false;
                }

                // Attempt to restore seeding
                for (int i = 0; i < Math.Min(firstRoundMatches.Count * 2, qualifiedPlayers.Count); i++)
                {
                    var matchIndex = i / 2;
                    var participantIndex = i % 2;
                    var match = firstRoundMatches[matchIndex];

                    if (match.Participants == null)
                    {
                        match.Participants = new List<Tournament.MatchParticipant>();
                    }

                    while (match.Participants.Count <= participantIndex)
                    {
                        match.Participants.Add(new Tournament.MatchParticipant());
                    }

                    match.Participants[participantIndex] = new Tournament.MatchParticipant
                    {
                        Player = qualifiedPlayers[i].Player,
                    };
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bracket seeding recovery");
                return false;
            }
        }

        /// <summary>
        /// Validates the integrity of the playoff bracket structure and seeding
        /// </summary>
        public List<string> ValidatePlayoffBracket(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament?.PlayoffMatches == null)
            {
                errors.Add("No playoff bracket found");
                return errors;
            }

            // Validate round progression
            var rounds = tournament.PlayoffMatches
                .GroupBy(m => m.Type)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Check round sizes
            foreach (var round in rounds)
            {
                var expectedCount = round.Key switch
                {
                    TournamentMatchType.RoundOf16 => 8,
                    TournamentMatchType.Quarterfinal => 4,
                    TournamentMatchType.Semifinal => 2,
                    TournamentMatchType.Final => 1,
                    TournamentMatchType.PlayoffThirdPlace => tournament.Settings?.IncludeThirdPlaceMatch == true ? 1 : 0,
                    _ => 0
                };

                if (round.Value.Count != expectedCount)
                {
                    errors.Add($"Invalid number of matches in {round.Key}: found {round.Value.Count}, expected {expectedCount}");
                }
            }

            // Validate seeding integrity
            var firstRoundMatches = tournament.PlayoffMatches
                .Where(m => m.Type == rounds.Keys.Min())
                .OrderBy(m => m.DisplayPosition)
                .ToList();

            foreach (var match in firstRoundMatches)
            {
                if (match.Participants?.Count != 2)
                {
                    errors.Add($"First round match {match.Id} has {match.Participants?.Count ?? 0} participants, expected 2");
                    continue;
                }

                foreach (var participant in match.Participants)
                {
                    if (participant?.Player == null)
                    {
                        errors.Add($"Missing player data in first round match {match.Id}");
                    }
                    else if (participant.SourceMatch != null)
                    {
                        errors.Add($"First round participant in match {match.Id} should not have a source match");
                    }
                }
            }

            // Validate advancement paths
            foreach (var match in tournament.PlayoffMatches.Where(m => m.Type != TournamentMatchType.Final))
            {
                if (match.NextMatch == null)
                {
                    errors.Add($"Match {match.Id} ({match.Type}) is missing next match connection");
                    continue;
                }

                var expectedNextType = match.Type switch
                {
                    TournamentMatchType.RoundOf16 => (TournamentMatchType?)TournamentMatchType.Quarterfinal,
                    TournamentMatchType.Quarterfinal => (TournamentMatchType?)TournamentMatchType.Semifinal,
                    TournamentMatchType.Semifinal => (TournamentMatchType?)TournamentMatchType.Final,
                    _ => (TournamentMatchType?)null
                };

                if (expectedNextType.HasValue && match.NextMatch.Type != expectedNextType)
                {
                    errors.Add($"Invalid progression from {match.Type} to {match.NextMatch.Type}");
                }

                // Validate winner advancement
                if (match.Result?.Winner != null)
                {
                    var winner = match.Result.Winner as DiscordUser;
                    var advanced = match.NextMatch.Participants?
                        .Any(p => (p?.Player as DiscordUser)?.Id == winner?.Id);

                    if (advanced != true)
                    {
                        errors.Add($"Winner of match {match.Id} ({winner?.Username ?? "Unknown"}) not found in next match");
                    }
                }
            }

            // Validate third place match connections
            if (tournament.Settings?.IncludeThirdPlaceMatch == true)
            {
                var thirdPlaceMatch = tournament.PlayoffMatches
                    .FirstOrDefault(m => m.Type == TournamentMatchType.PlayoffThirdPlace);

                if (thirdPlaceMatch == null)
                {
                    errors.Add("Third place match is enabled but not created");
                }
                else
                {
                    var semifinals = tournament.PlayoffMatches
                        .Where(m => m.Type == TournamentMatchType.Semifinal)
                        .ToList();

                    foreach (var semifinal in semifinals)
                    {
                        if (semifinal.ThirdPlaceMatch != thirdPlaceMatch)
                        {
                            errors.Add($"Semifinal match {semifinal.Id} not properly connected to third place match");
                        }
                    }

                    // Validate third place participants
                    if (thirdPlaceMatch.Participants?.Count != 2)
                    {
                        errors.Add($"Third place match has {thirdPlaceMatch.Participants?.Count ?? 0} participants, expected 2");
                    }
                    else
                    {
                        foreach (var participant in thirdPlaceMatch.Participants)
                        {
                            if (participant?.SourceMatch?.Type != TournamentMatchType.Semifinal)
                            {
                                errors.Add("Third place match participant not from semifinals");
                            }
                        }
                    }
                }
            }

            if (errors.Any())
            {
                _logger.LogError($"Playoff bracket validation failed with {errors.Count} errors");
                foreach (var error in errors)
                {
                    _logger.LogError($"Bracket error: {error}");
                }
            }

            return errors;
        }

        /// <summary>
        /// Attempts to transition a tournament to a new state with rollback capability
        /// </summary>
        public bool TryTransitionState(Tournament tournament, TournamentStage newState)
        {
            if (tournament == null)
            {
                _logger.LogError("Cannot transition state: tournament is null");
                return false;
            }

            try
            {
                // Store the current state for potential rollback
                var previousState = tournament.CurrentStage;
                var previousMatches = tournament.PlayoffMatches?.ToList();
                var previousGroups = tournament.Groups?.ToList();

                // Validate the transition
                if (!IsValidStateTransition(tournament, newState))
                {
                    _logger.LogError($"Invalid state transition from {previousState} to {newState}");
                    return false;
                }

                // Attempt the transition
                tournament.CurrentStage = newState;

                // Validate the new state
                var validationErrors = ValidateTournamentState(tournament);
                if (validationErrors.Any())
                {
                    // Rollback if validation fails
                    _logger.LogError($"State transition validation failed with {validationErrors.Count} errors");
                    foreach (var error in validationErrors)
                    {
                        _logger.LogError($"Validation error: {error}");
                    }

                    tournament.CurrentStage = previousState;
                    if (previousMatches != null)
                    {
                        tournament.PlayoffMatches = previousMatches;
                    }
                    if (previousGroups != null)
                    {
                        tournament.Groups = previousGroups;
                    }

                    return false;
                }

                _logger.LogInformation($"Successfully transitioned tournament {tournament.Name} from {previousState} to {newState}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during state transition from {tournament.CurrentStage} to {newState}");
                return false;
            }
        }

        /// <summary>
        /// Validates state-specific requirements before transition
        /// </summary>
        private List<string> ValidateStateTransitionRequirements(Tournament tournament, TournamentStage newState)
        {
            var errors = new List<string>();

            switch (newState)
            {
                case TournamentStage.Groups:
                    // Validate requirements for starting group stage
                    if (tournament.SignedUpPlayers?.Count < (tournament.Settings?.MinPlayers ?? 0))
                    {
                        errors.Add($"Not enough players to start groups: {tournament.SignedUpPlayers?.Count ?? 0}/{tournament.Settings?.MinPlayers ?? 0}");
                    }
                    break;

                case TournamentStage.Playoffs:
                    // Validate requirements for starting playoffs
                    if (tournament.Groups == null || !tournament.Groups.Any())
                    {
                        errors.Add("No groups defined for playoff transition");
                        break;
                    }

                    if (tournament.Groups.Any(g => !g.IsComplete))
                    {
                        var incompleteGroups = tournament.Groups.Count(g => !g.IsComplete);
                        errors.Add($"{incompleteGroups} groups are not complete");
                    }

                    // Validate qualified players
                    var qualifiedPlayers = tournament.Groups
                        .SelectMany(g => g.Participants?
                            .OrderByDescending(p => p.Wins)
                            .ThenByDescending(p => p.Wins - p.Losses)
                            .Take(2) ?? [])
                        .Where(p => p?.Player != null)
                        .ToList();

                    if (!qualifiedPlayers.Any())
                    {
                        errors.Add("No qualified players found for playoffs");
                    }
                    break;

                case TournamentStage.Complete:
                    // Validate requirements for completion
                    if (tournament.PlayoffMatches == null || !tournament.PlayoffMatches.Any())
                    {
                        errors.Add("No playoff matches found");
                        break;
                    }

                    var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
                    if (finalMatch?.Result == null)
                    {
                        errors.Add("Final match is not complete");
                    }

                    if (tournament.Settings?.IncludeThirdPlaceMatch == true)
                    {
                        var thirdPlaceMatch = tournament.PlayoffMatches
                            .FirstOrDefault(m => m.Type == TournamentMatchType.PlayoffThirdPlace);

                        if (thirdPlaceMatch?.Result == null)
                        {
                            errors.Add("Third place match is not complete");
                        }
                    }
                    break;
            }

            return errors;
        }

        public bool ValidatePlayoffSetup(Tournament tournament)
        {
            if (tournament == null)
            {
                _logger.LogError("Tournament cannot be null");
                return false;
            }

            if (tournament.Groups == null || tournament.Groups.Count == 0)
            {
                _logger.LogError("Tournament must have groups to set up playoffs");
                return false;
            }

            if (tournament.CurrentStage != TournamentStage.Groups)
            {
                _logger.LogError("Tournament must be in group stage to set up playoffs");
                return false;
            }

            // Check if all groups are complete
            if (!tournament.Groups.All(g => g.IsComplete))
            {
                _logger.LogError("All groups must be complete to set up playoffs");
                return false;
            }

            return true;
        }

        public bool ValidateGroupMatchCreation(Tournament tournament, Tournament.Group group)
        {
            if (tournament == null)
            {
                _logger.LogError("Tournament cannot be null");
                return false;
            }

            if (group == null)
            {
                _logger.LogError("Group cannot be null");
                return false;
            }

            if (tournament.CurrentStage != TournamentStage.Groups)
            {
                _logger.LogError("Tournament must be in group stage to create group matches");
                return false;
            }

            if (group.Participants == null || group.Participants.Count < 2)
            {
                _logger.LogError("Group must have at least 2 participants");
                return false;
            }

            return true;
        }

        public bool ValidateGroupCompletion(Tournament tournament, Tournament.Group group)
        {
            if (tournament == null)
            {
                _logger.LogError("Tournament cannot be null");
                return false;
            }

            if (group == null)
            {
                _logger.LogError("Group cannot be null");
                return false;
            }

            if (tournament.CurrentStage != TournamentStage.Groups)
            {
                _logger.LogError("Tournament must be in group stage to complete groups");
                return false;
            }

            if (group.Matches == null || group.Matches.Count == 0)
            {
                _logger.LogError("Group must have matches to be completed");
                return false;
            }

            // Check if all non-tiebreaker matches are complete
            var nonTiebreakerMatches = group.Matches.Where(m => !m.IsTiebreakerMatch);
            if (!nonTiebreakerMatches.All(m => m.Result != null))
            {
                _logger.LogError("All non-tiebreaker matches must be complete");
                return false;
            }

            return true;
        }
    }
}