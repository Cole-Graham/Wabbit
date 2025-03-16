using System.Collections.Generic;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament state validation
    /// </summary>
    public interface ITournamentStateValidator
    {
        /// <summary>
        /// Validates a tournament state transition
        /// </summary>
        bool IsValidStateTransition(Tournament tournament, TournamentStage newStage);

        /// <summary>
        /// Validates the tournament state for data consistency
        /// </summary>
        /// <param name="tournament">Tournament to validate</param>
        /// <returns>List of validation errors, empty if no errors</returns>
        List<string> ValidateTournamentState(Tournament tournament);

        /// <summary>
        /// Validates tournament playoff setup
        /// </summary>
        bool ValidatePlayoffSetup(Tournament tournament);

        /// <summary>
        /// Validates the playoff bracket is correctly linked
        /// </summary>
        bool ValidatePlayoffBracket(Tournament tournament);

        /// <summary>
        /// Validates a match's state and data
        /// </summary>
        List<string> ValidateMatch(Tournament tournament, Tournament.Match match);

        /// <summary>
        /// Validates the structure of playoff matches to ensure correct connections
        /// </summary>
        List<string> ValidateBracketStructure(Tournament tournament);

        bool ValidateGroupMatchCreation(Tournament tournament, Tournament.Group group);
        bool ValidateGroupCompletion(Tournament tournament, Tournament.Group group);
    }
}