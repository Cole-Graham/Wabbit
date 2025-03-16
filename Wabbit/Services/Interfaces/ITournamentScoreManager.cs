using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for managing tournament scores and standings
    /// </summary>
    public interface ITournamentScoreManager
    {
        /// <summary>
        /// Updates scores for a group after a match is completed
        /// </summary>
        void UpdateGroupScores(Tournament.Group group, Tournament.Match match);

        /// <summary>
        /// Gets sorted standings for a group based on points, wins, and game differentials
        /// </summary>
        List<Tournament.GroupParticipant> GetGroupStandings(Tournament.Group group);

        /// <summary>
        /// Checks for ties among participants at a specified position
        /// </summary>
        List<Tournament.GroupParticipant> CheckForTie(Tournament.Group group, int position);

        /// <summary>
        /// Gets the best third-place finishers across multiple groups
        /// </summary>
        List<Tournament.GroupParticipant> GetBestThirdPlace(Tournament tournament, int count);

        /// <summary>
        /// Determines if all matches in a group are complete and checks for ties
        /// </summary>
        bool IsGroupComplete(Tournament.Group group);

        /// <summary>
        /// Gets the head-to-head record between two participants
        /// </summary>
        (int wins, int losses) GetHeadToHeadRecord(Tournament.Group group, Tournament.GroupParticipant participant1, Tournament.GroupParticipant participant2);
    }
}