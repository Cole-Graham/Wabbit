using System;
using System.Threading.Tasks;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for managing tournament state concurrency and transactions
    /// </summary>
    public interface ITournamentStateManager : IDisposable
    {
        /// <summary>
        /// Executes a tournament update operation with proper locking and rollback capability
        /// </summary>
        /// <param name="tournament">The tournament to update</param>
        /// <param name="updateAction">The action to perform on the tournament</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        Task<bool> ExecuteTournamentUpdateAsync(
            Tournament tournament,
            Func<Tournament, Task<bool>> updateAction,
            string operationName);

        /// <summary>
        /// Executes a match update operation with proper locking and validation
        /// </summary>
        /// <param name="tournament">The tournament containing the match</param>
        /// <param name="match">The match to update</param>
        /// <param name="updateAction">The action to perform on the match</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        Task<bool> ExecuteMatchUpdateAsync(
            Tournament tournament,
            Tournament.Match match,
            Func<Tournament.Match, Task<bool>> updateAction,
            string operationName);
    }
}