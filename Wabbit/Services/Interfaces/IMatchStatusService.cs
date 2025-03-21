using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for managing match status display and interactions
    /// </summary>
    public interface IMatchStatusService
    {
        /// <summary>
        /// Initializes or updates the match status message
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <returns>The created or updated message</returns>
        Task<DiscordMessage> UpdateMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Creates a new match status embed for a new match, preserving history of previous matches
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <returns>The newly created message</returns>
        Task<DiscordMessage> CreateNewMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Updates the match status to show map ban stage
        /// </summary>
        Task<DiscordMessage> UpdateToMapBanStageAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Updates the match status to show deck submission stage
        /// </summary>
        Task<DiscordMessage> UpdateToDeckSubmissionStageAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Updates the match status to show game results stage
        /// </summary>
        Task<DiscordMessage> UpdateToGameResultsStageAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Gets the match status message
        /// </summary>
        Task<DiscordMessage?> GetMatchStatusMessageAsync(DiscordChannel channel, DiscordClient client);

        /// <summary>
        /// Ensures the match status message exists and recreates it if needed
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <returns>The existing or newly created message</returns>
        Task<DiscordMessage> EnsureMatchStatusMessageExistsAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Records a map ban selection in the match status
        /// </summary>
        Task RecordMapBanAsync(DiscordChannel channel, Round round, string teamName, List<string> bannedMaps, DiscordClient client);

        /// <summary>
        /// Records a deck submission in the match status
        /// </summary>
        Task RecordDeckSubmissionAsync(DiscordChannel channel, Round round, ulong playerId, string deckCode, int gameNumber, DiscordClient client);

        /// <summary>
        /// Records a game result in the match status
        /// </summary>
        Task RecordGameResultAsync(DiscordChannel channel, Round round, string winnerName, int gameNumber, DiscordClient client);

        /// <summary>
        /// Finalizes a match with results and awards points
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        Task FinalizeMatchAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Adds a visual separator between matches in group stages
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="client">The Discord client</param>
        /// <param name="nextMatchNumber">The next match number in the sequence</param>
        /// <param name="totalMatches">Total matches in the group stage</param>
        /// <param name="nextOpponentName">The name of the next opponent</param>
        /// <returns>The separator message that was sent, or null if sending failed</returns>
        Task<DiscordMessage?> AddMatchSeparatorAsync(
            DiscordChannel channel,
            DiscordClient client,
            int nextMatchNumber,
            int totalMatches,
            string nextOpponentName);

        /// <summary>
        /// Creates and sends a game winner dropdown for the next game
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <param name="gameNumber">The game number</param>
        /// <param name="player1Name">First player's name</param>
        /// <param name="player2Name">Second player's name</param>
        /// <param name="player1Id">First player's Discord ID</param>
        /// <param name="player2Id">Second player's Discord ID</param>
        /// <returns>The sent dropdown message</returns>
        Task<DiscordMessage> CreateGameWinnerDropdownAsync(
            DiscordChannel channel,
            Round round,
            DiscordClient client,
            int gameNumber,
            string player1Name,
            string player2Name,
            ulong player1Id,
            ulong player2Id);

        /// <summary>
        /// Updates the match status to show the current map and map pool
        /// </summary>
        Task UpdateMapInformationAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Updates the match status in all threads
        /// </summary>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        Task UpdateMatchStatusInAllThreadsAsync(Round round, DiscordClient client);

        /// <summary>
        /// Confirms a team's map bans
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="teamName">The name of the team confirming bans</param>
        /// <param name="client">The Discord client</param>
        Task ConfirmMapBansAsync(DiscordChannel channel, Round round, string teamName, DiscordClient client);

        /// <summary>
        /// Confirms a team's map bans using the team from the current channel
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <returns>True if the maps were successfully confirmed, false otherwise</returns>
        Task<bool> ConfirmMapBansAsync(DiscordChannel channel, Round round, DiscordClient client);

        /// <summary>
        /// Checks if a refresh button is on cooldown for a specific user
        /// </summary>
        /// <param name="roundId">The ID of the round</param>
        /// <param name="userId">The ID of the user</param>
        /// <returns>True if the button is on cooldown, false otherwise</returns>
        bool IsRefreshButtonOnCooldown(string roundId, ulong userId);

        /// <summary>
        /// Revises a team's map ban selection by returning to the selection dropdown
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="teamName">The name of the team revising bans</param>
        /// <param name="client">The Discord client</param>
        Task ReviseMapBansAsync(DiscordChannel channel, Round round, string teamName, DiscordClient client);

        /// <summary>
        /// Confirms a player's deck submission
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="playerId">The ID of the player confirming the deck</param>
        /// <param name="client">The Discord client</param>
        Task ConfirmDeckAsync(DiscordChannel channel, Round round, ulong playerId, DiscordClient client);

        /// <summary>
        /// Revises a player's deck submission by clearing the temporary deck code
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="playerId">The ID of the player revising the deck</param>
        /// <param name="client">The Discord client</param>
        Task ReviseDeckAsync(DiscordChannel channel, Round round, ulong playerId, DiscordClient client);
    }
}