using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Core service for match operations, used by both group and playoff services
    /// </summary>
    public interface ITournamentMatchOperationsService
    {
        /// <summary>
        /// Creates a new match between two players
        /// </summary>
        Tournament.Match CreateMatch(
            string matchName,
            TournamentMatchType matchType,
            int bestOf,
            DiscordMember player1,
            DiscordMember player2,
            Tournament.Group? sourceGroup = null);

        /// <summary>
        /// Updates match result and related statistics
        /// </summary>
        Task UpdateMatchResultAsync(
            Tournament tournament,
            Tournament.Match match,
            DiscordMember winner,
            int winnerScore,
            int loserScore);

        /// <summary>
        /// Validates if a match can be created between two players
        /// </summary>
        bool ValidateMatchCreation(
            Tournament tournament,
            DiscordMember player1,
            DiscordMember player2);
    }
}