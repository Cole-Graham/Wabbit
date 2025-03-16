using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    public interface ITournamentMatchService
    {
        /// <summary>
        /// Creates and starts a 1v1 match
        /// </summary>
        Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember player1,
            DiscordMember player2,
            DiscordClient client,
            int matchLength,
            Tournament.Match? existingMatch = null);

        /// <summary>
        /// Updates the result of a match with the winner and score
        /// </summary>
        Task UpdateMatchResultAsync(
            Tournament? tournament,
            Tournament.Match? match,
            DiscordMember? winner,
            int winnerScore,
            int loserScore);

        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        Task HandleMatchCompletion(
            Tournament tournament,
            Tournament.Match match,
            DiscordClient client);

        /// <summary>
        /// Archives threads for a completed match
        /// </summary>
        Task ArchiveMatchThreadsAsync(
            Tournament.Match match,
            DiscordClient client,
            TimeSpan? archiveDuration = null);
    }
}