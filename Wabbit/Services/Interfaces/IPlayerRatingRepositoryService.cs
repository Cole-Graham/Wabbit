using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models.Rating;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for player rating data storage and retrieval
    /// </summary>
    public interface IPlayerRatingRepositoryService
    {
        /// <summary>
        /// Get a player's rating information
        /// </summary>
        /// <param name="userId">Discord user ID of the player</param>
        /// <returns>The player rating object or null if not found</returns>
        Task<PlayerRating?> GetPlayerRatingAsync(ulong userId);

        /// <summary>
        /// Get a player's rating information, creating a new rating profile if one doesn't exist
        /// </summary>
        /// <param name="user">Discord user object</param>
        /// <returns>The player rating object</returns>
        Task<PlayerRating> GetOrCreatePlayerRatingAsync(DiscordUser user);

        /// <summary>
        /// Update a player's rating information
        /// </summary>
        /// <param name="rating">Updated player rating</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdatePlayerRatingAsync(PlayerRating rating);

        /// <summary>
        /// Get all player ratings
        /// </summary>
        /// <returns>List of all player ratings</returns>
        Task<List<PlayerRating>> GetAllPlayerRatingsAsync();

        /// <summary>
        /// Get the top rated players for a specific game type
        /// </summary>
        /// <param name="gameType">Type of game (1v1, 2v2, etc.)</param>
        /// <param name="count">Number of players to return</param>
        /// <param name="tournamentRatings">Whether to use tournament ratings instead of regular ratings</param>
        /// <returns>List of top rated players</returns>
        Task<List<PlayerRating>> GetTopPlayersByRatingAsync(GameType gameType, int count = 10, bool tournamentRatings = false);

        /// <summary>
        /// Save all player ratings to the repository
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SavePlayerRatingsAsync();

        /// <summary>
        /// Load player ratings from the repository
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> LoadPlayerRatingsAsync();

        /// <summary>
        /// Reset all ratings for a specific game type to the default value
        /// </summary>
        /// <param name="gameType">Type of game to reset ratings for</param>
        /// <param name="tournamentRatings">Whether to reset tournament ratings instead of regular ratings</param>
        /// <returns>Number of players affected</returns>
        Task<int> ResetRatingsAsync(GameType gameType, bool tournamentRatings = false);

        /// <summary>
        /// Delete a player's rating profile
        /// </summary>
        /// <param name="userId">Discord user ID of the player</param>
        /// <returns>True if successful, false if the player wasn't found</returns>
        Task<bool> DeletePlayerRatingAsync(ulong userId);

        /// <summary>
        /// Calculate global leaderboard statistics
        /// </summary>
        /// <returns>Statistics about current player ratings</returns>
        Task<LeaderboardStats> GetLeaderboardStatsAsync();
    }

    /// <summary>
    /// Statistics about the current player ratings
    /// </summary>
    public class LeaderboardStats
    {
        /// <summary>
        /// Total number of rated players
        /// </summary>
        public int TotalPlayers { get; set; }

        /// <summary>
        /// Number of players with at least one rating per game type
        /// </summary>
        public Dictionary<GameType, int> PlayerCountByGameType { get; set; } = new();

        /// <summary>
        /// Highest rating per game type
        /// </summary>
        public Dictionary<GameType, int> HighestRatingByGameType { get; set; } = new();

        /// <summary>
        /// Average rating per game type
        /// </summary>
        public Dictionary<GameType, double> AverageRatingByGameType { get; set; } = new();

        /// <summary>
        /// Total matches played per game type
        /// </summary>
        public Dictionary<GameType, int> TotalMatchesByGameType { get; set; } = new();
    }
}