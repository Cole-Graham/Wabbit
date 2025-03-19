using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for competitive season data storage and retrieval
    /// </summary>
    public interface ISeasonRepositoryService
    {
        /// <summary>
        /// Get the current active season if one exists
        /// </summary>
        /// <returns>The current season or null if no active season</returns>
        Task<Season?> GetCurrentSeasonAsync();

        /// <summary>
        /// Start a new season
        /// </summary>
        /// <param name="name">Name of the season</param>
        /// <param name="description">Description of the season</param>
        /// <param name="endDate">End date of the season</param>
        /// <param name="creator">User who created the season</param>
        /// <returns>The newly created season</returns>
        Task<Season> StartNewSeasonAsync(string name, string description, DateTime endDate, DiscordUser creator);

        /// <summary>
        /// End the current season
        /// </summary>
        /// <param name="playerRatings">Current player ratings to snapshot for final rankings</param>
        /// <returns>The ended season or null if no active season</returns>
        Task<Season?> EndCurrentSeasonAsync(List<PlayerRating> playerRatings);

        /// <summary>
        /// Get a season by ID
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <returns>The season or null if not found</returns>
        Task<Season?> GetSeasonByIdAsync(string seasonId);

        /// <summary>
        /// Get all seasons
        /// </summary>
        /// <returns>List of all seasons</returns>
        Task<List<Season>> GetAllSeasonsAsync();

        /// <summary>
        /// Get past seasons
        /// </summary>
        /// <param name="count">Number of past seasons to return</param>
        /// <returns>List of past seasons, most recent first</returns>
        Task<List<Season>> GetPastSeasonsAsync(int count = 5);

        /// <summary>
        /// Update an existing season
        /// </summary>
        /// <param name="season">The updated season</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateSeasonAsync(Season season);

        /// <summary>
        /// Delete a season
        /// </summary>
        /// <param name="seasonId">ID of the season to delete</param>
        /// <returns>True if successful, false if not found</returns>
        Task<bool> DeleteSeasonAsync(string seasonId);

        /// <summary>
        /// Add a message related to a season
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <param name="channelId">Channel ID where the message was sent</param>
        /// <param name="messageId">Message ID</param>
        /// <param name="type">Type of message</param>
        /// <returns>True if successful, false if the season wasn't found</returns>
        Task<bool> AddSeasonMessageAsync(string seasonId, ulong channelId, ulong messageId, SeasonMessageType type);

        /// <summary>
        /// Save seasons to the repository
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SaveSeasonsAsync();

        /// <summary>
        /// Load seasons from the repository
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> LoadSeasonsAsync();
    }
}