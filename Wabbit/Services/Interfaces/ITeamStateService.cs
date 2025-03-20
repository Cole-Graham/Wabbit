using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for runtime team state management
    /// </summary>
    public interface ITeamStateService
    {
        /// <summary>
        /// Initialize team state
        /// </summary>
        Task Initialize();

        /// <summary>
        /// Create a new team
        /// </summary>
        /// <param name="name">Team name</param>
        /// <param name="type">Team type</param>
        /// <param name="creator">User who created the team</param>
        /// <returns>The created team</returns>
        Task<Team> CreateTeamAsync(string name, Wabbit.Models.TeamGameType type, DiscordUser creator);

        /// <summary>
        /// Check if a user can create a new team
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="type">Team type to check</param>
        /// <returns>True if the user can create a team, false otherwise</returns>
        Task<bool> CanCreateTeamAsync(ulong userId, Wabbit.Models.TeamGameType type);

        /// <summary>
        /// Check if a user has admin privileges for team management
        /// </summary>
        /// <param name="userId">Discord user ID to check</param>
        /// <returns>True if the user has admin privileges</returns>
        Task<bool> HasTeamAdminPrivilegesAsync(ulong userId);

        /// <summary>
        /// Check if a team name is available
        /// </summary>
        /// <param name="name">Team name to check</param>
        /// <returns>True if the name is available, false otherwise</returns>
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
        Task<List<Team>> GetPlayerTeamsByTypeAsync(ulong userId, Wabbit.Models.TeamGameType type);

        /// <summary>
        /// Add a player to a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="user">User to add</param>
        /// <param name="role">Role to add the player as</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> AddPlayerToTeamAsync(string teamId, DiscordUser user, PlayerRole role, DiscordUser requester);

        /// <summary>
        /// Remove a player from a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player to remove</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> RemovePlayerFromTeamAsync(string teamId, ulong userId, DiscordUser requester);

        /// <summary>
        /// Change a player's role in a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player</param>
        /// <param name="newRole">New role for the player</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> ChangePlayerRoleAsync(string teamId, ulong userId, PlayerRole newRole, DiscordUser requester);

        /// <summary>
        /// Update a team's name
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="newName">New team name</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateTeamNameAsync(string teamId, string newName, DiscordUser requester);

        /// <summary>
        /// Check if a player can modify a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="userId">Discord user ID of the player</param>
        /// <returns>True if the player can modify the team, false otherwise</returns>
        Task<bool> CanModifyTeamAsync(string teamId, ulong userId);

        /// <summary>
        /// Check if a player is on cooldown for making changes to a specific team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="changeType">Type of change to check</param>
        /// <returns>Remaining cooldown time in minutes, or 0 if not on cooldown</returns>
        Task<int> GetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType);

        /// <summary>
        /// Delete a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteTeamAsync(string teamId, DiscordUser requester);

        /// <summary>
        /// Transfer team ownership
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="newOwnerId">Discord user ID of the new owner</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> TransferTeamOwnershipAsync(string teamId, ulong newOwnerId, DiscordUser requester);

        /// <summary>
        /// Get the top teams by rating for a specific type
        /// </summary>
        /// <param name="type">Team type</param>
        /// <param name="count">Number of teams to return</param>
        /// <returns>List of top teams by rating</returns>
        Task<List<Team>> GetTopTeamsByRatingAsync(Wabbit.Models.TeamGameType type, int count = 10);

        /// <summary>
        /// Get the number of teams by type
        /// </summary>
        /// <returns>Dictionary with team counts by type</returns>
        Task<Dictionary<Wabbit.Models.TeamGameType, int>> GetTeamCountsByTypeAsync();

        /// <summary>
        /// Record a match result for a team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="opponentId">Opponent team ID or player ID</param>
        /// <param name="isWin">Whether the team won</param>
        /// <param name="oldRating">Old rating</param>
        /// <param name="newRating">New rating</param>
        /// <param name="ratingChange">Rating change</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> RecordMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange);

        /// <summary>
        /// Reset a team change cooldown (admin only)
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="changeType">Type of change to reset</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> ResetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType);
    }

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
}