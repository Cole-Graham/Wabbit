using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for runtime competitive season management
    /// </summary>
    public interface ISeasonStateService
    {
        /// <summary>
        /// Initialize season state
        /// </summary>
        Task Initialize();

        /// <summary>
        /// Get the current active season if one exists
        /// </summary>
        /// <returns>The current season or null if no active season</returns>
        Task<Season?> GetCurrentSeasonAsync();

        /// <summary>
        /// Check if a season is currently active
        /// </summary>
        /// <returns>True if a season is active, false otherwise</returns>
        Task<bool> IsSeasonActiveAsync();

        /// <summary>
        /// Start a new competitive season
        /// </summary>
        /// <param name="name">Season name</param>
        /// <param name="description">Season description</param>
        /// <param name="endDate">End date for the season</param>
        /// <param name="creator">Creator of the season</param>
        /// <returns>The newly created season</returns>
        Task<Season> StartNewSeasonAsync(string name, string description, DateTime endDate, DiscordUser creator);

        /// <summary>
        /// End the current season
        /// </summary>
        /// <returns>The ended season or null if no active season</returns>
        Task<Season?> EndCurrentSeasonAsync();

        /// <summary>
        /// Get a season by ID
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <returns>The season or null if not found</returns>
        Task<Season?> GetSeasonByIdAsync(string seasonId);

        /// <summary>
        /// Get past seasons
        /// </summary>
        /// <param name="count">Number of past seasons to return</param>
        /// <returns>List of past seasons, most recent first</returns>
        Task<List<Season>> GetPastSeasonsAsync(int count = 5);

        /// <summary>
        /// Add a related message to a season
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <param name="channel">Channel where the message was sent</param>
        /// <param name="message">Message to add</param>
        /// <param name="messageType">Type of message</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> AddSeasonMessageAsync(string seasonId, DiscordChannel channel, DiscordMessage message, SeasonMessageType messageType);

        /// <summary>
        /// Update a season's end date
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <param name="newEndDate">New end date</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateSeasonEndDateAsync(string seasonId, DateTime newEndDate, DiscordUser requester);

        /// <summary>
        /// Delete a season (admin only)
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <param name="requester">User making the request</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteSeasonAsync(string seasonId, DiscordUser requester);

        /// <summary>
        /// Get all seasons
        /// </summary>
        /// <returns>List of all seasons</returns>
        Task<List<Season>> GetAllSeasonsAsync();

        /// <summary>
        /// Get the final rankings for a specific season
        /// </summary>
        /// <param name="seasonId">ID of the season</param>
        /// <param name="gameType">Game type to get rankings for</param>
        /// <param name="count">Number of top players to return</param>
        /// <returns>List of season rankings</returns>
        Task<List<SeasonRanking>> GetSeasonFinalRankingsAsync(string seasonId, GameType gameType, int count = 10);

        /// <summary>
        /// Check if a user has admin privileges for season management
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <returns>True if the user has admin privileges, false otherwise</returns>
        Task<bool> HasSeasonAdminPrivilegesAsync(ulong userId);

        /// <summary>
        /// Generate a preview of what the final rankings would be if the season ended now
        /// </summary>
        /// <param name="gameType">Game type to preview rankings for</param>
        /// <param name="count">Number of top players to include</param>
        /// <returns>List of season rankings</returns>
        Task<List<SeasonRanking>> PreviewSeasonFinalRankingsAsync(GameType gameType, int count = 10);
    }
}