using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament playoff management
    /// </summary>
    public interface ITournamentPlayoffService
    {
        /// <summary>
        /// Sets up playoffs for a tournament
        /// </summary>
        /// <param name="tournament">The tournament to set up playoffs for</param>
        void SetupPlayoffs(Tournament tournament);

        /// <summary>
        /// Gets advancement criteria for playoff stage
        /// </summary>
        /// <param name="playerCount">Total number of players in the tournament</param>
        /// <param name="groupCount">Number of groups in the tournament</param>
        /// <returns>A tuple containing (groupWinners, bestThirdPlace)</returns>
        (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount);

        /// <summary>
        /// Updates bracket advancement after a match result
        /// </summary>
        /// <param name="tournament">The tournament to update</param>
        /// <param name="match">The match that was completed</param>
        /// <returns>True if advancement was successful, false otherwise</returns>
        bool UpdateBracketAdvancement(Tournament tournament, Tournament.Match match);

        /// <summary>
        /// Processes a forfeit in a playoff match
        /// </summary>
        /// <param name="tournament">The tournament containing the match</param>
        /// <param name="match">The match to forfeit</param>
        /// <param name="forfeitingPlayer">The player forfeiting the match</param>
        /// <returns>True if the forfeit was processed successfully, false otherwise</returns>
        bool ProcessForfeit(Tournament tournament, Tournament.Match match, object forfeitingPlayer);

        /// <summary>
        /// Gets visualization data for a tournament bracket
        /// </summary>
        /// <param name="tournament">The tournament to visualize</param>
        /// <returns>A dictionary containing visualization data</returns>
        Dictionary<string, object> GetBracketVisualizationData(Tournament tournament);

        /// <summary>
        /// Checks if all semifinals in the tournament are completed
        /// </summary>
        /// <param name="tournament">The tournament to check</param>
        /// <returns>True if all semifinals are completed, false otherwise</returns>
        bool AreSemifinalsCompleted(Tournament tournament);

        /// <summary>
        /// Checks if a third place match can be created for the tournament
        /// </summary>
        /// <param name="tournament">The tournament to check</param>
        /// <returns>True if a third place match can be created, false otherwise</returns>
        bool CanCreateThirdPlaceMatch(Tournament tournament);

        /// <summary>
        /// Creates a third place match on demand if all semifinals are completed
        /// </summary>
        /// <param name="tournament">The tournament to create the third place match for</param>
        /// <param name="requestedByUserId">The Discord ID of the admin/moderator who requested the match</param>
        /// <returns>True if the match was created successfully, false otherwise</returns>
        Task<bool> CreateThirdPlaceMatchOnDemand(Tournament tournament, ulong requestedByUserId);
    }
}