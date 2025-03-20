using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for managing scrimmage status and related operations
    /// </summary>
    public interface IScrimmageStatusService
    {
        /// <summary>
        /// Creates a new scrimmage between two players
        /// </summary>
        /// <param name="channel">The channel where the scrimmage command was initiated</param>
        /// <param name="player1">First player</param>
        /// <param name="deck1">Optional deck name for player 1</param>
        /// <param name="player2">Second player</param>
        /// <param name="deck2">Optional deck name for player 2</param>
        /// <param name="gameType">Type of game (1v1, 2v2, etc.)</param>
        /// <param name="matchLength">Length of match (Bo1, Bo3, Bo5)</param>
        /// <param name="isRated">Whether this is a rated match affecting player ratings</param>
        /// <param name="useTournamentMapPool">Whether to use tournament maps instead of casual maps</param>
        /// <returns>The created scrimmage</returns>
        Task<Scrimmage> CreateScrimmageAsync(
            DiscordChannel channel,
            DiscordUser player1,
            string? deck1,
            DiscordUser player2,
            string? deck2,
            ScrimmageGameType gameType,
            MatchLength matchLength,
            bool isRated,
            bool useTournamentMapPool);

        /// <summary>
        /// Updates the scrimmage status message with the current information
        /// </summary>
        /// <param name="scrimmage">The scrimmage to update</param>
        /// <returns>The updated status message</returns>
        Task<DiscordMessage> UpdateScrimmageStatusAsync(Scrimmage scrimmage);

        /// <summary>
        /// Marks a scrimmage as completed
        /// </summary>
        /// <param name="scrimmage">The scrimmage to complete</param>
        /// <returns>True if the scrimmage was successfully completed</returns>
        Task<bool> CompleteScrimmageAsync(Scrimmage scrimmage);

        /// <summary>
        /// Cancels an ongoing scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to cancel</param>
        /// <returns>True if the scrimmage was successfully cancelled</returns>
        Task<bool> CancelScrimmageAsync(Scrimmage scrimmage);

        /// <summary>
        /// Records a game result for a match in a scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to update</param>
        /// <param name="winningPlayer">The player number that won (1 or 2)</param>
        /// <returns>True if the game result was recorded successfully</returns>
        Task<bool> RecordGameResultAsync(Scrimmage scrimmage, int winningPlayer);

        /// <summary>
        /// Advances to the next game in a multi-game match
        /// </summary>
        /// <param name="scrimmage">The scrimmage to update</param>
        /// <returns>True if successfully advanced to the next game</returns>
        Task<bool> AdvanceToNextGameAsync(Scrimmage scrimmage);

        /// <summary>
        /// Gets an active scrimmage by thread ID
        /// </summary>
        /// <param name="threadId">The Discord thread ID</param>
        /// <returns>The scrimmage if found, otherwise null</returns>
        Task<Scrimmage?> GetScrimmageByThreadIdAsync(ulong threadId);

        /// <summary>
        /// Check if a user has admin privileges for scrimmage management
        /// </summary>
        /// <param name="userId">The Discord user ID to check</param>
        /// <returns>True if the user has admin privileges</returns>
        Task<bool> HasScrimmageAdminPrivilegesAsync(ulong userId);

        /// <summary>
        /// Check if a user can manage a specific scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to check</param>
        /// <param name="userId">The Discord user ID to check</param>
        /// <returns>True if the user can manage the scrimmage</returns>
        Task<bool> CanManageScrimmageAsync(Scrimmage scrimmage, ulong userId);
    }
}