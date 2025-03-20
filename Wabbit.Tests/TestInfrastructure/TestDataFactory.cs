using DSharpPlus.Entities;
using Moq;
using System.Collections.Generic;
using Wabbit.Models;
using TestParticipantInfo = Wabbit.Tests.TestInfrastructure.Models.ParticipantInfo;
using TestTournament = Wabbit.Tests.TestInfrastructure.Models.Tournament;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Factory for creating test data objects
    /// </summary>
    public static class TestDataFactory
    {
        /// <summary>
        /// Creates a list of test participants with specified count
        /// </summary>
        public static List<TestParticipantInfo> CreateTestParticipants(int count, bool withSeeding = false)
        {
            var participants = new List<TestParticipantInfo>();

            for (int i = 0; i < count; i++)
            {
                var participant = new TestParticipantInfo
                {
                    Id = (ulong)(1000 + i),
                    Username = $"TestPlayer{i + 1}",
                    Seed = withSeeding ? i + 1 : null
                };

                // Create a mock DiscordMember
                var mockMember = new Mock<DiscordMember>();
                mockMember.Setup(m => m.Id).Returns(participant.Id);
                mockMember.Setup(m => m.Username).Returns(participant.Username);
                mockMember.Setup(m => m.ToString()).Returns(participant.Username);
                participant.Player = mockMember.Object;

                participants.Add(participant);
            }

            return participants;
        }

        /// <summary>
        /// Creates a sample tournament group with participants
        /// </summary>
        public static Tournament.Group CreateTestGroup(int participantCount, string groupName = "Group A")
        {
            var group = new Tournament.Group
            {
                Name = groupName,
                Participants = new List<Tournament.GroupParticipant>(),
                Matches = new List<Tournament.Match>()
            };

            // Add participants
            for (int i = 0; i < participantCount; i++)
            {
                var mockMember = new Mock<DiscordMember>();
                mockMember.Setup(m => m.Id).Returns((ulong)(1000 + i));
                mockMember.Setup(m => m.Username).Returns($"Player{i + 1}");
                mockMember.Setup(m => m.ToString()).Returns($"Player{i + 1}");

                group.Participants.Add(new Tournament.GroupParticipant
                {
                    Player = mockMember.Object,
                    Seed = i + 1
                });
            }

            return group;
        }

        /// <summary>
        /// Creates a sample tournament with groups and participants
        /// </summary>
        public static Tournament CreateTestTournament(string name, int groupCount, int participantsPerGroup, TournamentFormat format = TournamentFormat.GroupStageWithPlayoffs)
        {
            var tournament = new Tournament
            {
                Name = name,
                Format = format,
                Groups = new List<Tournament.Group>()
            };

            // Create groups
            for (int i = 0; i < groupCount; i++)
            {
                var groupName = $"Group {(char)('A' + i)}";
                tournament.Groups.Add(CreateTestGroup(participantsPerGroup, groupName));
            }

            return tournament;
        }

        /// <summary>
        /// Creates a sample Round object for testing
        /// </summary>
        public static Round CreateTestRound(string name, int length = 3, bool oneVOne = true)
        {
            var round = new Round
            {
                Name = name,
                Length = length,
                OneVOne = oneVOne,
                Teams = new List<Round.Team>(),
                IsCompleted = false
            };

            // Add two teams
            var team1 = new Round.Team
            {
                Name = "Team A",
                Participants = new List<Round.Participant>(),
                Wins = 0
            };

            var team2 = new Round.Team
            {
                Name = "Team B",
                Participants = new List<Round.Participant>(),
                Wins = 0
            };

            // Add participants to teams
            var mockMember1 = new Mock<DiscordMember>();
            mockMember1.Setup(m => m.Id).Returns(1001UL);
            mockMember1.Setup(m => m.Username).Returns("Player1");
            mockMember1.Setup(m => m.ToString()).Returns("Player1");

            var mockMember2 = new Mock<DiscordMember>();
            mockMember2.Setup(m => m.Id).Returns(1002UL);
            mockMember2.Setup(m => m.Username).Returns("Player2");
            mockMember2.Setup(m => m.ToString()).Returns("Player2");

            team1.Participants.Add(new Round.Participant { Player = mockMember1.Object });
            team2.Participants.Add(new Round.Participant { Player = mockMember2.Object });

            round.Teams.Add(team1);
            round.Teams.Add(team2);

            return round;
        }

        /// <summary>
        /// Sets points/wins/draws for a tournament participant
        /// </summary>
        public static void SetPoints(TestTournament.GroupParticipant participant, int points)
        {
            participant.Points = points;
        }
    }
}