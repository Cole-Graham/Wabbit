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
        void SetupPlayoffs(Tournament tournament);

        /// <summary>
        /// Gets advancement criteria for playoff stage
        /// </summary>
        (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount);
    }
}