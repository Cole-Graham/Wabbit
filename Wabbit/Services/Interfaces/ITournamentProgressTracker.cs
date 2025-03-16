using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tracking tournament stage progression
    /// </summary>
    public interface ITournamentProgressTracker
    {
        /// <summary>
        /// Checks if a tournament can move to the next stage
        /// </summary>
        bool CanProgressToNextStage(Tournament tournament);

        /// <summary>
        /// Gets progress metrics for the current tournament stage
        /// </summary>
        Dictionary<string, double> GetStageProgress(Tournament tournament);

        /// <summary>
        /// Gets a list of pending actions needed to progress the tournament
        /// </summary>
        List<string> GetPendingActions(Tournament tournament);
    }
}