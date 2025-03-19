using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for managing leaderboard data and displays
    /// </summary>
    public interface ILeaderboardService
    {
        /// <summary>
        /// Get player and team rankings for a specific game type and season
        /// </summary>
        /// <param name="gameType">Game type to get rankings for</param>
        /// <param name="seasonId">Season ID to get rankings for, null for current season</param>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="itemsPerPage">Items per page</param>
        /// <returns>Tuple containing rankings and pagination info</returns>
        Task<(List<LeaderboardEntry> Rankings, int TotalPages, int TotalEntries)> GetLeaderboardAsync(
            Wabbit.Models.TeamGameType gameType,
            string? seasonId = null,
            int page = 1,
            int itemsPerPage = 25);

        /// <summary>
        /// Create a leaderboard embed for display
        /// </summary>
        /// <param name="gameType">Game type to show leaderboard for</param>
        /// <param name="seasonId">Season ID to show leaderboard for, null for current season</param>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="itemsPerPage">Items per page</param>
        /// <returns>The leaderboard message components</returns>
        Task<(DiscordEmbed LeaderboardEmbed, DiscordButtonComponent[] NavigationButtons, DiscordSelectComponent SeasonSelector)>
            CreateLeaderboardEmbedAsync(
                Wabbit.Models.TeamGameType gameType,
                string? seasonId = null,
                int page = 1,
                int itemsPerPage = 25);

        /// <summary>
        /// Create and post a new leaderboard embed to a channel
        /// </summary>
        /// <param name="channel">Channel to post to</param>
        /// <param name="gameType">Game type to show leaderboard for</param>
        /// <param name="seasonId">Season ID to show leaderboard for, null for current season</param>
        /// <returns>Posted message</returns>
        Task<DiscordMessage> PostLeaderboardAsync(DiscordChannel channel, Wabbit.Models.TeamGameType gameType, string? seasonId = null);

        /// <summary>
        /// Update an existing leaderboard message
        /// </summary>
        /// <param name="message">Message to update</param>
        /// <param name="gameType">Game type to show leaderboard for</param>
        /// <param name="seasonId">Season ID to show leaderboard for, null for current season</param>
        /// <param name="page">Page number (1-based)</param>
        /// <returns>Updated message</returns>
        Task<DiscordMessage> UpdateLeaderboardAsync(DiscordMessage message, Wabbit.Models.TeamGameType gameType, string? seasonId = null, int page = 1);

        /// <summary>
        /// Get player ranking for a specific player
        /// </summary>
        /// <param name="playerId">Discord user ID of the player</param>
        /// <param name="gameType">Game type to get ranking for</param>
        /// <param name="seasonId">Season ID to get ranking for, null for current season</param>
        /// <returns>Player ranking information</returns>
        Task<LeaderboardEntry?> GetPlayerRankingAsync(ulong playerId, Wabbit.Models.TeamGameType gameType, string? seasonId = null);

        /// <summary>
        /// Get team ranking for a specific team
        /// </summary>
        /// <param name="teamId">Team ID</param>
        /// <param name="gameType">Game type to get ranking for</param>
        /// <param name="seasonId">Season ID to get ranking for, null for current season</param>
        /// <returns>Team ranking information</returns>
        Task<LeaderboardEntry?> GetTeamRankingAsync(string teamId, Wabbit.Models.TeamGameType gameType, string? seasonId = null);
    }
}