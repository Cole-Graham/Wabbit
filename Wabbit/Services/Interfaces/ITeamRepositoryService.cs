using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for team repository operations
    /// </summary>
    public interface ITeamRepositoryService
    {
        /// <summary>
        /// Add a new team to the repository
        /// </summary>
        Task<bool> AddTeamAsync(Team team);

        /// <summary>
        /// Get a team by ID
        /// </summary>
        Task<Team?> GetTeamByIdAsync(string teamId);

        /// <summary>
        /// Get a team by name
        /// </summary>
        Task<Team?> GetTeamByNameAsync(string teamName);

        /// <summary>
        /// Get all teams
        /// </summary>
        Task<List<Team>> GetAllTeamsAsync();

        /// <summary>
        /// Get teams by type
        /// </summary>
        Task<List<Team>> GetTeamsByTypeAsync(Wabbit.Models.TeamGameType type);

        /// <summary>
        /// Get teams that a player is a member of
        /// </summary>
        Task<List<Team>> GetTeamsByPlayerIdAsync(ulong playerId);

        /// <summary>
        /// Update a team
        /// </summary>
        Task<bool> UpdateTeamAsync(Team team);

        /// <summary>
        /// Delete a team
        /// </summary>
        Task<bool> DeleteTeamAsync(string teamId);

        /// <summary>
        /// Get top teams by rating for a specific type
        /// </summary>
        Task<List<Team>> GetTopTeamsByRatingAsync(Wabbit.Models.TeamGameType type, int count = 10);

        /// <summary>
        /// Gets all teams a player is a member of (in any role)
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        /// <returns>List of teams the player is a member of</returns>
        Task<List<Team>> GetTeamsByPlayerAsync(ulong userId);

        /// <summary>
        /// Gets teams where a player has a specific role
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        /// <param name="role">The player role (Core, Secondary, Substitute)</param>
        /// <returns>List of teams where the player has the specified role</returns>
        Task<List<Team>> GetTeamsByPlayerRoleAsync(ulong userId, PlayerRole role);

        /// <summary>
        /// Validates if a team name is available
        /// </summary>
        /// <param name="name">Team name to check</param>
        /// <returns>True if the name is available</returns>
        Task<bool> IsTeamNameAvailableAsync(string name);

        /// <summary>
        /// Saves the current teams to the repository
        /// </summary>
        /// <returns>True if the save was successful</returns>
        Task<bool> SaveTeamsAsync();

        /// <summary>
        /// Loads teams from the repository
        /// </summary>
        /// <returns>True if the load was successful</returns>
        Task<bool> LoadTeamsAsync();

        /// <summary>
        /// Get count of teams by type
        /// </summary>
        Task<Dictionary<Wabbit.Models.TeamGameType, int>> GetTeamCountsByTypeAsync();
    }
}