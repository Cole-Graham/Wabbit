using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament data storage and retrieval
    /// </summary>
    public interface ITournamentRepositoryService
    {
        /// <summary>
        /// Initializes the repository by loading tournaments
        /// </summary>
        void Initialize();

        /// <summary>
        /// Save tournaments to file
        /// </summary>
        Task SaveTournamentsAsync();

        /// <summary>
        /// Get a tournament by name
        /// </summary>
        Tournament? GetTournament(string name);

        /// <summary>
        /// Get all tournaments
        /// </summary>
        List<Tournament> GetAllTournaments();

        /// <summary>
        /// Add a tournament to the repository
        /// </summary>
        void AddTournament(Tournament tournament);

        /// <summary>
        /// Delete a tournament by name
        /// </summary>
        Task DeleteTournamentAsync(string name, DiscordClient? client = null);

        /// <summary>
        /// Archive tournament data
        /// </summary>
        Task ArchiveTournamentDataAsync(string tournamentName, DiscordClient? client = null);

        /// <summary>
        /// Repair data files
        /// </summary>
        Task RepairDataFilesAsync(DiscordClient? client = null);
    }
}