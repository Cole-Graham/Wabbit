using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament state management
    /// </summary>
    public interface ITournamentStateService
    {
        /// <summary>
        /// Saves the current tournament state
        /// </summary>
        Task SaveTournamentStateAsync(DiscordClient? client = null);

        /// <summary>
        /// Safely saves the tournament state with retry logic and error handling
        /// </summary>
        /// <param name="client">Discord client for channel updates</param>
        /// <param name="caller">Optional caller information for logging</param>
        /// <returns>A boolean indicating whether the save operation was successful</returns>
        Task<bool> SafeSaveTournamentStateAsync(DiscordClient? client = null, string? caller = null);

        /// <summary>
        /// Loads the tournament state
        /// </summary>
        Task<Tournament?> LoadTournamentState();

        /// <summary>
        /// Links rounds to tournaments
        /// </summary>
        void LinkRoundsToTournaments();

        /// <summary>
        /// Converts rounds to state
        /// </summary>
        List<ActiveRound> ConvertRoundsToState(List<Round> rounds);

        /// <summary>
        /// Converts state to rounds
        /// </summary>
        List<Round> ConvertStateToRounds(List<ActiveRound> activeRounds);

        /// <summary>
        /// Gets active rounds for a tournament
        /// </summary>
        List<ActiveRound> GetActiveRoundsForTournament(string tournamentId);

        /// <summary>
        /// Gets the tournament map pool
        /// </summary>
        List<string> GetTournamentMapPool(bool oneVOne);

        /// <summary>
        /// Updates tournament from a round
        /// </summary>
        void UpdateTournamentFromRound(Tournament tournament);

        /// <summary>
        /// Validates and recovers team threads that might have been deleted
        /// </summary>
        /// <param name="tournament">Tournament to validate threads for</param>
        /// <param name="client">Discord client for channel operations</param>
        /// <returns>True if recovery was successful, false otherwise</returns>
        Task<bool> ValidateAndRecoverThreadsAsync(Tournament tournament, DiscordClient client);

        /// <summary>
        /// Recovers match status embeds for a specific player/team thread
        /// </summary>
        /// <param name="team">The team whose thread needs match status embeds recovered</param>
        /// <param name="match">The tournament match associated with the team</param>
        /// <param name="client">Discord client for channel operations</param>
        /// <returns>True if recovery was successful, false otherwise</returns>
        Task<bool> RecoverMatchStatusEmbedsAsync(Round round, Tournament.Match match, DiscordClient client);

        /// <summary>
        /// Rebuilds the relationship between tournament matches and Discord threads
        /// </summary>
        /// <param name="tournament">Tournament to rebuild associations for</param>
        /// <param name="client">Discord client for channel operations</param>
        /// <returns>True if rebuilding was successful, false otherwise</returns>
        Task<bool> RebuildMatchThreadAssociationsAsync(Tournament tournament, DiscordClient client);
    }
}