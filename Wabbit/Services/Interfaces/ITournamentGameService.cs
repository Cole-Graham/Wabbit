using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for managing individual games within tournament matches
    /// </summary>
    public interface ITournamentGameService
    {
        /// <summary>
        /// Handles game result selection and advances the match series
        /// </summary>
        Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client);

        /// <summary>
        /// Records a game result within a match
        /// </summary>
        Task RecordGameResultAsync(Round round, string winnerId, int gameNumber, DiscordClient client);

        /// <summary>
        /// Validates and processes a deck submission for a game
        /// </summary>
        Task<bool> ProcessDeckSubmissionAsync(Round round, ulong playerId, string deckCode, int gameNumber);

        /// <summary>
        /// Checks if both players have submitted decks for the current game
        /// </summary>
        bool AreDeckSubmissionsComplete(Round round, int gameNumber);

        /// <summary>
        /// Gets the current game number in a match
        /// </summary>
        int GetCurrentGameNumber(Round round);

        /// <summary>
        /// Gets the score for a specific player in the match
        /// </summary>
        int GetPlayerScore(Round round, ulong playerId);

        /// <summary>
        /// Gets the score for a given team in a round
        /// </summary>
        int GetTeamScore(Round round, ulong teamId);

        /// <summary>
        /// Determines if a match is complete based on game results
        /// </summary>
        bool IsMatchComplete(Round round);

        /// <summary>
        /// Gets the match winner if the match is complete
        /// </summary>
        DiscordMember? GetMatchWinner(Round round);

        /// <summary>
        /// Gets the final match score
        /// </summary>
        (int winner, int loser) GetFinalScore(Round round);

        /// <summary>
        /// Gets a random map for the next game, considering banned and played maps
        /// </summary>
        string? GetRandomMapForNextGame(Round round);
    }
}