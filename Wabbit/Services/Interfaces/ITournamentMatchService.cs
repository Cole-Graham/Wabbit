using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for managing tournament matches
    /// </summary>
    public interface ITournamentMatchService
    {
        /// <summary>
        /// Creates and starts a match between two teams
        /// </summary>
        /// <remarks>
        /// This method assumes that team scheduling (ensuring teams aren't double-booked)
        /// has already been handled by the TournamentManagerService's scheduling system.
        /// </remarks>
        Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember team1,
            DiscordMember team2,
            DiscordClient client,
            int matchLength,
            Tournament.Match? existingMatch = null);

        /// <summary>
        /// Creates and starts a match of any supported game type
        /// </summary>
        /// <remarks>
        /// This method assumes that participant scheduling (ensuring players/teams aren't double-booked)
        /// has already been handled by the TournamentManagerService's scheduling system.
        /// </remarks>
        Task CreateAndStartMatch(
            Tournament tournament,
            Tournament.Group? group,
            List<DiscordMember> teamA,
            List<DiscordMember> teamB,
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