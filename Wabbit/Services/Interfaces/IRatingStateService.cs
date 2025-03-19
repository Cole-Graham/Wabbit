using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;
using Wabbit.Models.Rating;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for runtime player rating management
    /// </summary>
    public interface IRatingStateService
    {
        /// <summary>
        /// Initialize rating state
        /// </summary>
        Task Initialize();

        /// <summary>
        /// Get a player's rating data, creating it if needed
        /// </summary>
        /// <param name="user">Discord user</param>
        /// <returns>Player rating data</returns>
        Task<PlayerRating> GetOrCreatePlayerRatingAsync(DiscordUser user);

        /// <summary>
        /// Get a player's rating for a specific game type
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="gameType">Game type</param>
        /// <param name="tournamentRating">Whether to get tournament rating instead of regular rating</param>
        /// <returns>The player's rating or default rating if not found</returns>
        Task<int> GetPlayerRatingAsync(ulong userId, GameType gameType, bool tournamentRating = false);

        /// <summary>
        /// Calculate rating changes for a match result
        /// </summary>
        /// <param name="winnerId">Discord user ID of the winner</param>
        /// <param name="loserId">Discord user ID of the loser</param>
        /// <param name="gameType">Game type</param>
        /// <param name="isTournament">Whether this is a tournament match</param>
        /// <returns>Tuple containing (winnerOldRating, winnerNewRating, loserOldRating, loserNewRating)</returns>
        Task<(int WinnerOldRating, int WinnerNewRating, int LoserOldRating, int LoserNewRating)> CalculateRatingChangesAsync(
            ulong winnerId, ulong loserId, GameType gameType, bool isTournament = false);

        /// <summary>
        /// Calculate rating changes for a team match result
        /// </summary>
        /// <param name="winnerTeam">Winning team</param>
        /// <param name="loserTeam">Losing team</param>
        /// <param name="gameType">Game type</param>
        /// <param name="isTournament">Whether this is a tournament match</param>
        /// <returns>Tuple containing (winnerOldRating, winnerNewRating, loserOldRating, loserNewRating)</returns>
        Task<(int WinnerOldRating, int WinnerNewRating, int LoserOldRating, int LoserNewRating)> CalculateTeamRatingChangesAsync(
            Team winnerTeam, Team loserTeam, GameType gameType, bool isTournament = false);

        /// <summary>
        /// Record a match result
        /// </summary>
        /// <param name="winnerId">Discord user ID of the winner</param>
        /// <param name="loserId">Discord user ID of the loser</param>
        /// <param name="gameType">Game type</param>
        /// <param name="isTournament">Whether this is a tournament match</param>
        /// <returns>Tuple containing (winnerOldRating, winnerNewRating, loserOldRating, loserNewRating)</returns>
        Task<(int WinnerOldRating, int WinnerNewRating, int LoserOldRating, int LoserNewRating)> RecordMatchResultAsync(
            ulong winnerId, ulong loserId, GameType gameType, bool isTournament = false);

        /// <summary>
        /// Record a team match result
        /// </summary>
        /// <param name="winnerTeamId">ID of the winning team</param>
        /// <param name="loserTeamId">ID of the losing team</param>
        /// <param name="gameType">Game type</param>
        /// <param name="isTournament">Whether this is a tournament match</param>
        /// <returns>Tuple containing (winnerOldRating, winnerNewRating, loserOldRating, loserNewRating)</returns>
        Task<(int WinnerOldRating, int WinnerNewRating, int LoserOldRating, int LoserNewRating)> RecordTeamMatchResultAsync(
            string winnerTeamId, string loserTeamId, GameType gameType, bool isTournament = false);

        /// <summary>
        /// Get the top players by rating for a specific game type
        /// </summary>
        /// <param name="gameType">Game type</param>
        /// <param name="count">Number of players to return</param>
        /// <param name="tournamentRating">Whether to use tournament ratings</param>
        /// <returns>List of top players by rating</returns>
        Task<List<PlayerRating>> GetTopPlayersByRatingAsync(GameType gameType, int count = 10, bool tournamentRating = false);

        /// <summary>
        /// Get the rating history for a player
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="gameType">Game type</param>
        /// <param name="count">Number of entries to return</param>
        /// <param name="tournamentRating">Whether to get tournament rating history</param>
        /// <returns>List of rating history entries</returns>
        Task<List<RatingChange>> GetPlayerRatingHistoryAsync(ulong userId, GameType gameType, int count = 10, bool tournamentRating = false);

        /// <summary>
        /// Set a player's rating directly (admin only)
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="gameType">Game type</param>
        /// <param name="newRating">New rating value</param>
        /// <param name="tournamentRating">Whether to set tournament rating</param>
        /// <param name="adminId">ID of the admin making the change</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SetPlayerRatingAsync(ulong userId, GameType gameType, int newRating, bool tournamentRating, ulong adminId);

        /// <summary>
        /// Reset ratings for all players for a specific game type
        /// </summary>
        /// <param name="gameType">Game type</param>
        /// <param name="tournamentRating">Whether to reset tournament ratings</param>
        /// <returns>Number of players affected</returns>
        Task<int> ResetAllRatingsAsync(GameType gameType, bool tournamentRating = false);

        /// <summary>
        /// Calculate leaderboard statistics
        /// </summary>
        /// <returns>Leaderboard statistics</returns>
        Task<LeaderboardStats> GetLeaderboardStatsAsync();
    }
}