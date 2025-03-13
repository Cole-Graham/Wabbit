using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Wabbit.Services.Interfaces
{
    public interface ITournamentGameService
    {
        /// <summary>
        /// Handles game result selection and advances the match series
        /// </summary>
        Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client);

        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client);

        /// <summary>
        /// Gets available maps for the next game in a match
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>List of available map names</returns>
        List<string> GetAvailableMapsForNextGame(Round round);

        /// <summary>
        /// Gets a random map for the next game, considering banned and played maps
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>A random map name, or null if no maps are available</returns>
        string? GetRandomMapForNextGame(Round round);
    }
}