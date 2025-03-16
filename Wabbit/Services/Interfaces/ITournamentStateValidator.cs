using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for validating tournament states and transitions
    /// </summary>
    public interface ITournamentStateValidator
    {
        /// <summary>
        /// Validates the current state and data integrity of a tournament
        /// </summary>
        List<string> ValidateTournamentState(Tournament tournament);

        /// <summary>
        /// Determines if a transition between tournament states is valid
        /// </summary>
        bool IsValidStateTransition(Tournament tournament, TournamentStage newState);

        /// <summary>
        /// Validates a match's state and data
        /// </summary>
        List<string> ValidateMatch(Tournament tournament, Tournament.Match match);

        /// <summary>
        /// Validates the structure of playoff matches to ensure correct connections
        /// </summary>
        List<string> ValidateBracketStructure(Tournament tournament);
    }
}