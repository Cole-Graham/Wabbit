using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Types of team changes that might have cooldowns
    /// </summary>
    public enum TeamChangeType
    {
        /// <summary>
        /// Team name change
        /// </summary>
        NameChange,

        /// <summary>
        /// Core player changes
        /// </summary>
        CorePlayerChange,

        /// <summary>
        /// Secondary player changes
        /// </summary>
        SecondaryPlayerChange,

        /// <summary>
        /// Substitute player changes
        /// </summary>
        SubstitutePlayerChange
    }

    /// <summary>
    /// Unified interface for team management, combining data persistence and runtime state
    /// </summary>
    public interface ITeamService
    {
        /// <summary>
        /// Initialize the team service
        /// </summary>
        Task InitializeAsync();

        #region Team Creation and Management

        /// <summary>
        /// Create a new team
        /// </summary>
        /// <param name="name">Team name</param>
        /// <param name="type">Team type</param>
        /// <param name="creator">User who created the team</param>
        /// <param name="managementOverride">Whether to override restrictions with management privileges</param>
        /// <returns>The created team</returns>
        Task<Team> CreateTeamAsync(string name, TeamGameType type, DiscordUser creator, bool managementOverride = false);

        /// <summary>
        /// Update a team's information
        /// </summary>
        /// <param name="team">The team with updated information</param>
        /// <returns>True if successful</returns>
        Task<bool> UpdateTeamAsync(Team team);

        /// <summary>
        /// Delete a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="requester">User requesting the deletion</param>
        /// <returns>True if successful</returns>
        Task<bool> DeleteTeamAsync(string teamId, DiscordUser requester);

        /// <summary>
        /// Update a team's name
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="newName">New team name</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful</returns>
        Task<bool> UpdateTeamNameAsync(string teamId, string newName, DiscordUser requester);

        /// <summary>
        /// Transfer team ownership
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="newOwnerId">Discord user ID of the new owner</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful</returns>
        Task<bool> TransferTeamOwnershipAsync(string teamId, ulong newOwnerId, DiscordUser requester);

        #endregion

        #region Team Queries

        /// <summary>
        /// Check if a team name is available
        /// </summary>
        /// <param name="name">Team name to check</param>
        /// <returns>True if the name is available</returns>
        Task<bool> IsTeamNameAvailableAsync(string name);

        /// <summary>
        /// Get a team by ID
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <returns>The team or null if not found</returns>
        Task<Team?> GetTeamByIdAsync(string teamId);

        /// <summary>
        /// Get a team by name
        /// </summary>
        /// <param name="teamName">Team name</param>
        /// <returns>The team or null if not found</returns>
        Task<Team?> GetTeamByNameAsync(string teamName);

        /// <summary>
        /// Get top teams by rating for a specific type
        /// </summary>
        /// <param name="type">Team type</param>
        /// <param name="count">Maximum number of teams to return</param>
        /// <returns>List of top teams by rating</returns>
        Task<List<Team>> GetTopTeamsByRatingAsync(TeamGameType type, int count = 10);

        /// <summary>
        /// Get all teams
        /// </summary>
        /// <returns>List of all teams</returns>
        Task<List<Team>> GetAllTeamsAsync();

        /// <summary>
        /// Get teams by type
        /// </summary>
        /// <param name="type">Team type</param>
        /// <returns>List of teams of specified type</returns>
        Task<List<Team>> GetTeamsByTypeAsync(TeamGameType type);

        /// <summary>
        /// Get the number of teams by type
        /// </summary>
        /// <returns>Dictionary with team counts by type</returns>
        Task<Dictionary<TeamGameType, int>> GetTeamCountsByTypeAsync();

        #endregion

        #region Player Management

        /// <summary>
        /// Add a player to a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="user">Discord user to add</param>
        /// <param name="role">Role in the team</param>
        /// <param name="requester">User making the request</param>
        /// <param name="managementOverride">Whether to override restrictions with management privileges</param>
        /// <returns>True if successful</returns>
        Task<bool> AddPlayerToTeamAsync(string teamId, DiscordUser user, PlayerRole role, DiscordUser requester, bool managementOverride = false);

        /// <summary>
        /// Remove a player from a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player to remove</param>
        /// <param name="requester">User making the request</param>
        /// <param name="managementOverride">Whether to override restrictions with management privileges</param>
        /// <returns>True if successful</returns>
        Task<bool> RemovePlayerFromTeamAsync(string teamId, ulong userId, DiscordUser requester, bool managementOverride = false);

        /// <summary>
        /// Change a player's role in a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player</param>
        /// <param name="newRole">New role for the player</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful</returns>
        Task<bool> ChangePlayerRoleAsync(string teamId, ulong userId, PlayerRole newRole, DiscordUser requester);

        /// <summary>
        /// Get all teams a player is a member of
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <returns>List of teams the player is a member of</returns>
        Task<List<Team>> GetPlayerTeamsAsync(ulong userId);

        /// <summary>
        /// Get teams a player is a member of by type
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="type">Team type</param>
        /// <returns>List of teams the player is a member of the specified type</returns>
        Task<List<Team>> GetPlayerTeamsByTypeAsync(ulong userId, TeamGameType type);

        /// <summary>
        /// Check if a user can create a new team
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="type">Team type to check</param>
        /// <returns>True if the user can create a team</returns>
        Task<bool> CanCreateTeamAsync(ulong userId, TeamGameType type);

        #endregion

        #region Match Results and Rating

        /// <summary>
        /// Record a match result for a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="opponentId">Opponent team ID or player ID</param>
        /// <param name="isWin">Whether the team won</param>
        /// <param name="oldRating">Old rating</param>
        /// <param name="newRating">New rating</param>
        /// <param name="ratingChange">Rating change</param>
        /// <returns>True if successful</returns>
        Task<bool> RecordMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange);

        /// <summary>
        /// Record a tournament match result for a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="opponentId">Opponent team ID</param>
        /// <param name="isWin">Whether the team won</param>
        /// <param name="oldRating">Old tournament rating</param>
        /// <param name="newRating">New tournament rating</param>
        /// <param name="ratingChange">Rating change</param>
        /// <param name="tournamentName">Name of the tournament</param>
        /// <returns>True if successful</returns>
        Task<bool> RecordTournamentMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange, string tournamentName);

        #endregion

        #region Permissions and Cooldowns

        /// <summary>
        /// Check if a player can modify a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player</param>
        /// <returns>True if the player can modify the team</returns>
        Task<bool> CanModifyTeamAsync(string teamId, ulong userId);

        /// <summary>
        /// Check if a user has special Discord server privileges for team management
        /// </summary>
        /// <param name="userId">Discord user ID to check</param>
        /// <returns>True if the user has management privileges (Administrator/Moderator)</returns>
        Task<bool> HasServerManagementPrivilegesAsync(ulong userId);

        /// <summary>
        /// Check if a player is on cooldown for making changes to a specific team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="changeType">Type of change to check</param>
        /// <returns>Remaining cooldown time in minutes, or 0 if not on cooldown</returns>
        Task<int> GetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType);

        /// <summary>
        /// Reset a team change cooldown (requires management privileges)
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="changeType">Type of change to reset</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful</returns>
        Task<bool> ResetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType, DiscordUser requester);

        #endregion

        #region Data Persistence

        /// <summary>
        /// Save all teams to persistent storage
        /// </summary>
        /// <returns>True if successful</returns>
        Task<bool> SaveTeamsAsync();

        #endregion
    }
}