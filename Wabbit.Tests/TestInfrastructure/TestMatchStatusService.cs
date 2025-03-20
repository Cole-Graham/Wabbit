using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Test-specific adapter for MatchStatusService that adds methods needed for testing
    /// </summary>
    public class TestMatchStatusService
    {
        private readonly MatchStatusService _matchStatusService;
        private readonly ILogger<MatchStatusService> _logger;
        private readonly TestTournamentMapService _mapService;

        public TestMatchStatusService(
            ILogger<MatchStatusService> logger,
            TestTournamentMapService mapService)
        {
            _logger = logger;
            _mapService = mapService;
            _matchStatusService = new MatchStatusService(
                logger,
                mapService);
        }

        /// <summary>
        /// Determines the winner of a match
        /// </summary>
        public Round.Team? DetermineWinner(Round match)
        {
            if (match.Teams.Count != 2)
                return null;

            var team1 = match.Teams[0];
            var team2 = match.Teams[1];

            int requiredWins = match.Length == 5 ? 3 : (match.Length == 3 ? 2 : 1);

            if (team1.Wins >= requiredWins)
                return team1;

            if (team2.Wins >= requiredWins)
                return team2;

            return null;
        }

        /// <summary>
        /// Checks if a match is complete
        /// </summary>
        public bool IsMatchComplete(Round match)
        {
            if (match.Teams.Count != 2)
                return false;

            var team1 = match.Teams[0];
            var team2 = match.Teams[1];

            int requiredWins = match.Length == 5 ? 3 : (match.Length == 3 ? 2 : 1);

            return team1.Wins >= requiredWins || team2.Wins >= requiredWins;
        }

        /// <summary>
        /// Updates a match's result
        /// </summary>
        public async Task UpdateMatchResultAsync(Round match, int team1Score, int team2Score)
        {
            if (match.Teams.Count != 2)
                return;

            match.Teams[0].Wins = team1Score;
            match.Teams[1].Wins = team2Score;

            // Set match result text
            if (team1Score > team2Score)
            {
                match.WinMsg = $"{match.Teams[0].Name} won the match ({team1Score}-{team2Score})";
                match.PointsAwarded = 3;
            }
            else if (team2Score > team1Score)
            {
                match.WinMsg = $"{match.Teams[1].Name} won the match ({team2Score}-{team1Score})";
                match.PointsAwarded = 0;
            }
            else
            {
                match.WinMsg = $"The match ended in a draw ({team1Score}-{team2Score})";
                match.PointsAwarded = 1;
            }

            // Set IsCompleted based on win condition
            int requiredWins = match.Length == 5 ? 3 : (match.Length == 3 ? 2 : 1);
            match.IsCompleted = team1Score >= requiredWins || team2Score >= requiredWins;

            // Set match result
            match.MatchResult = $"{match.Teams[0].Name} {team1Score} - {team2Score} {match.Teams[1].Name}";

            await Task.CompletedTask;
        }
    }
}