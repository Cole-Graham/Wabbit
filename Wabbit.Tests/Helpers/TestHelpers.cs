using DSharpPlus.Entities;
using Moq;
using Wabbit.Models;

namespace Wabbit.Tests.Helpers;

public static class TestHelpers
{
    /// <summary>
    /// Creates a mock DiscordMember for testing
    /// </summary>
    public static DiscordMember CreateMockMember(ulong id, string username = null)
    {
        var mockMember = new Mock<DiscordMember>();
        mockMember.Setup(m => m.Id).Returns(id);
        mockMember.Setup(m => m.Username).Returns(username ?? $"User{id}");
        mockMember.Setup(m => m.DisplayName).Returns(username ?? $"User{id}");
        return mockMember.Object;
    }

    /// <summary>
    /// Creates a list of mock Round.Team objects
    /// </summary>
    public static List<Round.Team> CreateMockTeams(int id1, int id2)
    {
        return new List<Round.Team>
        {
            new Round.Team {
                Name = $"Player{id1}",
                Participants = new List<Round.Participant> {
                    new Round.Participant { Player = CreateMockMember((ulong)id1, $"Player{id1}") }
                }
            },
            new Round.Team {
                Name = $"Player{id2}",
                Participants = new List<Round.Participant> {
                    new Round.Participant { Player = CreateMockMember((ulong)id2, $"Player{id2}") }
                }
            }
        };
    }

    /// <summary>
    /// Creates a list of mock ParticipantInfo objects
    /// </summary>
    public static List<ParticipantInfo> CreateMockParticipants(int count)
    {
        var participants = new List<ParticipantInfo>();
        for (int i = 1; i <= count; i++)
        {
            participants.Add(new ParticipantInfo { Id = (ulong)i, Username = $"Player{i}" });
        }
        return participants;
    }

    /// <summary>
    /// Creates a list of mock ParticipantInfo objects with specified seeds
    /// </summary>
    public static List<ParticipantInfo> CreateMockParticipantsWithSeeds(int count)
    {
        var participants = new List<ParticipantInfo>();
        for (int i = 1; i <= count; i++)
        {
            participants.Add(new ParticipantInfo { Id = (ulong)i, Username = $"Player{i}", Seed = i });
        }
        return participants;
    }

    /// <summary>
    /// Creates a mock tournament group with participants and matches
    /// </summary>
    public static Tournament.Group CreateMockGroup(string name, int participantCount, bool createMatches = false)
    {
        var participants = new List<Tournament.GroupParticipant>();
        for (int i = 1; i <= participantCount; i++)
        {
            participants.Add(new Tournament.GroupParticipant
            {
                Player = CreateMockMember((ulong)i, $"Player{i}"),
                Points = (participantCount - i) * 3 // Simulate descending points
            });
        }

        var group = new Tournament.Group
        {
            Name = name,
            Participants = participants
        };

        if (createMatches)
        {
            // Create a full round-robin of matches
            var matches = new List<Round>();

            for (int i = 0; i < participantCount; i++)
            {
                for (int j = i + 1; j < participantCount; j++)
                {
                    var match = new Round
                    {
                        Id = $"match_{i + 1}vs{j + 1}",
                        IsCompleted = true,
                        Teams = CreateMockTeams(i + 1, j + 1)
                    };
                    matches.Add(match);
                }
            }

            group.Matches = matches;
        }

        return group;
    }
}