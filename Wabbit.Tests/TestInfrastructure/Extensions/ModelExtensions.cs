using System.Collections.Generic;
using Wabbit.Models;
using TestTournament = Wabbit.Tests.TestInfrastructure.Models.Tournament;
using TestParticipantInfo = Wabbit.Tests.TestInfrastructure.Models.ParticipantInfo;
using ModelsTournament = Wabbit.Models.Tournament;

namespace Wabbit.Tests.TestInfrastructure.Extensions
{
    /// <summary>
    /// Extension methods for working with model objects in tests
    /// </summary>
    public static class ModelExtensions
    {
        /// <summary>
        /// Sets points for a tournament participant via its Wins and Draws values
        /// </summary>
        public static void SetPoints(this TestTournament.GroupParticipant participant, int points)
        {
            // Points = (Wins * 3) + Draws
            // Set Wins and Draws to achieve the desired number of points
            participant.Draws = points % 3;
            participant.Wins = (points - participant.Draws) / 3;
        }

        /// <summary>
        /// Converts test ParticipantInfo to Tournament.GroupParticipant
        /// </summary>
        public static TestTournament.GroupParticipant ToTournamentParticipant(this TestParticipantInfo info)
        {
            // Create a mock DiscordMember for the player
            var mockMember = new Moq.Mock<DSharpPlus.Entities.DiscordMember>();
            mockMember.Setup(m => m.Id).Returns(info.Id);
            mockMember.Setup(m => m.Username).Returns(info.Username);
            mockMember.Setup(m => m.ToString()).Returns(info.Username);

            return new TestTournament.GroupParticipant
            {
                Player = mockMember.Object,
                Seed = info.Seed.GetValueOrDefault()
            };
        }

        /// <summary>
        /// Converts a list of test ParticipantInfo to Tournament.GroupParticipant list
        /// </summary>
        public static List<TestTournament.GroupParticipant> ToTournamentParticipants(this IEnumerable<TestParticipantInfo> infos)
        {
            var result = new List<TestTournament.GroupParticipant>();

            foreach (var info in infos)
            {
                result.Add(info.ToTournamentParticipant());
            }

            return result;
        }

        /// <summary>
        /// Simulates a match result between two teams
        /// </summary>
        public static void SimulateMatchResult(this Round round, int team1Score, int team2Score)
        {
            if (round.Teams.Count < 2)
                return;

            round.Teams[0].Wins = team1Score;
            round.Teams[1].Wins = team2Score;

            // Set match result text
            if (team1Score > team2Score)
            {
                round.WinMsg = $"{round.Teams[0].Name} won the match ({team1Score}-{team2Score})";
                round.PointsAwarded = 3;
            }
            else if (team2Score > team1Score)
            {
                round.WinMsg = $"{round.Teams[1].Name} won the match ({team2Score}-{team1Score})";
                round.PointsAwarded = 0;
            }
            else
            {
                round.WinMsg = $"The match ended in a draw ({team1Score}-{team2Score})";
                round.PointsAwarded = 1;
            }

            // Set IsCompleted based on win condition
            int requiredWins = round.Length == 5 ? 3 : (round.Length == 3 ? 2 : 1);
            round.IsCompleted = team1Score >= requiredWins || team2Score >= requiredWins;

            // Set match result
            round.MatchResult = $"{round.Teams[0].Name} {team1Score} - {team2Score} {round.Teams[1].Name}";
        }
    }
}