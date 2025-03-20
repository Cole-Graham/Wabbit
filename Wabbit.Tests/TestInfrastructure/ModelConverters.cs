using System.Collections.Generic;
using System.Linq;
using DSharpPlus.Entities;
using Moq;
using ModelsParticipantInfo = Wabbit.Models.ParticipantInfo;
using ModelsTournament = Wabbit.Models.Tournament;
using TestParticipantInfo = Wabbit.Tests.TestInfrastructure.Models.ParticipantInfo;
using TestTournament = Wabbit.Tests.TestInfrastructure.Models.Tournament;
using Wabbit.Models;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Utility class for converting between test models and production models
    /// </summary>
    public static class ModelConverters
    {
        /// <summary>
        /// Converts a test ParticipantInfo to a production ParticipantInfo
        /// </summary>
        public static ModelsParticipantInfo ToProductionModel(this TestParticipantInfo testModel)
        {
            return new ModelsParticipantInfo
            {
                Id = testModel.Id,
                Username = testModel.Username
                // Note: Production ParticipantInfo doesn't have Player or Seed properties
            };
        }

        /// <summary>
        /// Converts a list of test ParticipantInfo to a list of production ParticipantInfo
        /// </summary>
        public static List<ModelsParticipantInfo> ToProductionModels(this IEnumerable<TestParticipantInfo> testModels)
        {
            return testModels.Select(m => m.ToProductionModel()).ToList();
        }

        /// <summary>
        /// Creates a mock DiscordMember from a ParticipantInfo
        /// </summary>
        public static DiscordMember CreateMockDiscordMember(this TestParticipantInfo info)
        {
            var mockMember = new Mock<DiscordMember>();
            mockMember.Setup(m => m.Id).Returns(info.Id);
            mockMember.Setup(m => m.Username).Returns(info.Username);
            mockMember.Setup(m => m.ToString()).Returns(info.Username);

            // Set the Player property if it's not already set
            if (info.Player == null)
            {
                info.Player = mockMember.Object;
            }

            return mockMember.Object;
        }

        /// <summary>
        /// Converts a production Round to a test Tournament.Match
        /// </summary>
        public static TestTournament.Match ToTestMatch(this Round round)
        {
            var match = new TestTournament.Match
            {
                Name = round.Name,
                IsComplete = round.IsCompleted,
                Participants = new List<TestTournament.MatchParticipant>()
            };

            // Convert teams to participants
            foreach (var team in round.Teams)
            {
                foreach (var participant in team.Participants)
                {
                    match.Participants.Add(new TestTournament.MatchParticipant
                    {
                        Player = participant.Player,
                        Score = team.Wins
                    });
                }
            }

            return match;
        }

        /// <summary>
        /// Converts a list of Rounds to a list of Tournament.Matches
        /// </summary>
        public static List<TestTournament.Match> ToTestMatches(this IEnumerable<Round> rounds)
        {
            return rounds.Select(r => r.ToTestMatch()).ToList();
        }

        /// <summary>
        /// Converts a production Tournament.GroupParticipant to a test Tournament.GroupParticipant
        /// </summary>
        public static TestTournament.GroupParticipant ToTestGroupParticipant(this ModelsTournament.GroupParticipant productionParticipant)
        {
            return new TestTournament.GroupParticipant
            {
                Player = productionParticipant.Player,
                Seed = productionParticipant.Seed,
                Wins = productionParticipant.Wins,
                Draws = productionParticipant.Draws,
                Losses = productionParticipant.Losses
            };
        }

        /// <summary>
        /// Converts a list of production GroupParticipants to test GroupParticipants
        /// </summary>
        public static List<TestTournament.GroupParticipant> ToTestGroupParticipants(this IEnumerable<ModelsTournament.GroupParticipant> productionParticipants)
        {
            return productionParticipants.Select(p => p.ToTestGroupParticipant()).ToList();
        }
    }
}