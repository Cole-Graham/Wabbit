using System;
using System.Collections.Generic;
using Wabbit.Models;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Wabbit.Services
{
    public class TournamentStateValidator
    {
        public List<string> ValidateTournamentState(Tournament tournament)
        {
            var errors = new List<string>();

            if (tournament == null)
            {
                errors.Add("Tournament is null");
                return errors;
            }

            // Basic tournament property validation
            if (string.IsNullOrEmpty(tournament.Name))
            {
                errors.Add("Tournament name is missing");
            }

            // Validate groups
            if (tournament.Groups == null)
            {
                errors.Add("Tournament groups collection is null");
            }
            else
            {
                for (int i = 0; i < tournament.Groups.Count; i++)
                {
                    var group = tournament.Groups[i];
                    if (group == null)
                    {
                        errors.Add($"Group at index {i} is null");
                        continue;
                    }

                    // Check group properties
                    if (string.IsNullOrEmpty(group.Name))
                    {
                        errors.Add($"Group '{i}' name is missing");
                    }

                    // Check participants
                    if (group.Participants == null)
                    {
                        errors.Add($"Group '{group.Name}' participants collection is null");
                    }
                    else if (group.Participants.Count == 0)
                    {
                        errors.Add($"Group '{group.Name}' has no participants");
                    }
                    else
                    {
                        // Check individual participants
                        for (int j = 0; j < group.Participants.Count; j++)
                        {
                            var participant = group.Participants[j];
                            if (participant == null)
                            {
                                errors.Add($"Participant at index {j} in group '{group.Name}' is null");
                            }
                            else if (participant.Player == null)
                            {
                                errors.Add($"Participant at index {j} in group '{group.Name}' has null player");
                            }
                        }
                    }

                    // Check matches
                    if (group.Matches == null)
                    {
                        errors.Add($"Group '{group.Name}' matches collection is null");
                    }
                    else
                    {
                        for (int j = 0; j < group.Matches.Count; j++)
                        {
                            var match = group.Matches[j];
                            if (match == null)
                            {
                                errors.Add($"Match at index {j} in group '{group.Name}' is null");
                                continue;
                            }

                            ValidateMatch(match, errors, $"group '{group.Name}'");
                        }
                    }
                }
            }

            // Validate playoff matches
            if (tournament.PlayoffMatches == null)
            {
                errors.Add("Tournament playoff matches collection is null");
            }
            else
            {
                for (int i = 0; i < tournament.PlayoffMatches.Count; i++)
                {
                    var match = tournament.PlayoffMatches[i];
                    if (match == null)
                    {
                        errors.Add($"Playoff match at index {i} is null");
                        continue;
                    }

                    ValidateMatch(match, errors, "playoffs");
                }
            }

            // Validate playoff bracket structure
            if (tournament.CurrentStage == TournamentStage.Playoffs || tournament.CurrentStage == TournamentStage.Complete)
            {
                errors.AddRange(ValidateBracketStructure(tournament));
            }

            return errors;
        }

        // Helper method to validate a single match
        private void ValidateMatch(Tournament.Match match, List<string> errors, string context)
        {
            if (string.IsNullOrEmpty(match.Name))
            {
                errors.Add($"Match in {context} has no name");
            }

            if (match.Participants == null)
            {
                errors.Add($"Match '{match.Name}' in {context} has null participants collection");
            }
            else if (match.Participants.Count == 0)
            {
                errors.Add($"Match '{match.Name}' in {context} has no participants");
            }
            else if (match.Participants.Count != 2)
            {
                errors.Add($"Match '{match.Name}' in {context} has {match.Participants.Count} participants, expected 2");
            }
            else
            {
                // Check individual participants
                for (int i = 0; i < match.Participants.Count; i++)
                {
                    var participant = match.Participants[i];
                    if (participant == null)
                    {
                        errors.Add($"Participant at index {i} in match '{match.Name}' in {context} is null");
                    }
                    else if (participant.Player == null)
                    {
                        errors.Add($"Participant at index {i} in match '{match.Name}' in {context} has null player");
                    }
                }
            }

            // Check if match is complete but has no result
            if (match.IsComplete && match.Result == null)
            {
                errors.Add($"Match '{match.Name}' in {context} is marked complete but has no result");
            }

            // Check if match has result but incorrect completion state
            if (match.Result != null && !match.IsComplete)
            {
                errors.Add($"Match '{match.Name}' in {context} has result but is not marked as complete");
            }

            // Check the result if present
            if (match.Result != null)
            {
                if (match.Result.Winner == null)
                {
                    errors.Add($"Match '{match.Name}' in {context} has a result with null winner");
                }
            }

            // Validate rounds
            if (match.LinkedRound == null && match.IsComplete)
            {
                errors.Add($"Match '{match.Name}' in {context} is complete but has no linked round");
            }
        }

        // Helper method to validate bracket structure
        private List<string> ValidateBracketStructure(Tournament tournament)
        {
            // This is a stub - implementation should validate the bracket structure
            return new List<string>();
        }
    }
}