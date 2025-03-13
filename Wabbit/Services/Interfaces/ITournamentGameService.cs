using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Models;
using System.Threading.Tasks;

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
    }
}