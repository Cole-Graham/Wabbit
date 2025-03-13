using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;
using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    public interface ITournamentMatchService
    {
        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client);

        /// <summary>
        /// Starts playoff matches for a tournament
        /// </summary>
        Task StartPlayoffMatches(Tournament tournament, DiscordClient client);

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
        /// Sets up the playoff stage of a tournament
        /// </summary>
        Task SetupPlayoffStage(Tournament tournament, DiscordClient client);

        /// <summary>
        /// Determines the appropriate group count based on player count and format
        /// </summary>
        int DetermineGroupCount(int playerCount, TournamentFormat format);

        /// <summary>
        /// Gets optimal group sizes for distribution of players
        /// </summary>
        List<int> GetOptimalGroupSizes(int playerCount, int groupCount);

        /// <summary>
        /// Gets advancement criteria for playoff stage
        /// </summary>
        (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount);

        /// <summary>
        /// Updates the result of a match with the winner and score
        /// </summary>
        /// <param name="tournament">The tournament containing the match</param>
        /// <param name="match">The match to update</param>
        /// <param name="winner">The winning player</param>
        /// <param name="winnerScore">The winner's score</param>
        /// <param name="loserScore">The loser's score</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task UpdateMatchResultAsync(Tournament? tournament, Tournament.Match? match, DiscordMember? winner, int winnerScore, int loserScore);
    }
}