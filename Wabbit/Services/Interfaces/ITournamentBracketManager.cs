using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for managing tournament bracket operations
    /// </summary>
    public interface ITournamentBracketManager
    {
        /// <summary>
        /// Updates bracket advancement after a match is completed
        /// </summary>
        bool UpdateBracketAdvancement(Tournament tournament, Tournament.Match match);

        /// <summary>
        /// Creates the initial playoff bracket based on qualified participants
        /// </summary>
        List<Tournament.Match> CreatePlayoffBracket(Tournament tournament, List<Tournament.MatchParticipant> qualifiedParticipants);

        /// <summary>
        /// Links matches in the bracket to form a complete structure
        /// </summary>
        void LinkBracketMatches(Tournament tournament, List<Tournament.Match> matches);

        /// <summary>
        /// Determines the type of match based on its position in the bracket
        /// </summary>
        TournamentMatchType DetermineBracketRound(int matchesInRound);
    }
}