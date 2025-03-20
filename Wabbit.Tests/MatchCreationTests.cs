using DSharpPlus;
using DSharpPlus.Entities;
using Moq;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.Helpers;
using Xunit;

namespace Wabbit.Tests;

public class MatchCreationTests
{
    [Fact]
    public void CreateMatches_ForGroupStage_GeneratesCorrectNumberOfMatches()
    {
        // Arrange
        var group = TestHelpers.CreateMockGroup("Group A", 4, false);

        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var matches = tournamentService.CreateGroupMatches(group);

        // Assert
        // In a group of 4, each player plays against the other 3 = total of (4*3)/2 = 6 matches
        Assert.Equal(6, matches.Count);

        // Check that each player has 3 matches
        var playerMatches = new Dictionary<ulong, int>();

        foreach (var match in matches)
        {
            foreach (var team in match.Teams)
            {
                foreach (var participant in team.Participants)
                {
                    var playerId = participant.Player.Id;
                    if (!playerMatches.ContainsKey(playerId))
                    {
                        playerMatches[playerId] = 0;
                    }
                    playerMatches[playerId]++;
                }
            }
        }

        // Each player should be in exactly 3 matches
        Assert.Equal(4, playerMatches.Count); // 4 players
        Assert.All(playerMatches.Values, count => Assert.Equal(3, count));
    }

    [Fact]
    public void CreateMatches_WithOddNumberOfPlayers_GeneratesCorrectNumberOfMatches()
    {
        // Arrange
        var group = TestHelpers.CreateMockGroup("Group A", 3, false);

        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var matches = tournamentService.CreateGroupMatches(group);

        // Assert
        // In a group of 3, each player plays against the other 2 = total of (3*2)/2 = 3 matches
        Assert.Equal(3, matches.Count);

        // Check that each player has 2 matches
        var playerMatches = new Dictionary<ulong, int>();

        foreach (var match in matches)
        {
            foreach (var team in match.Teams)
            {
                foreach (var participant in team.Participants)
                {
                    var playerId = participant.Player.Id;
                    if (!playerMatches.ContainsKey(playerId))
                    {
                        playerMatches[playerId] = 0;
                    }
                    playerMatches[playerId]++;
                }
            }
        }

        // Each player should be in exactly 2 matches
        Assert.Equal(3, playerMatches.Count); // 3 players
        Assert.All(playerMatches.Values, count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task CreateMatchThreads_CreatesThreadPerTeam()
    {
        // Arrange
        var matches = new List<Round>
        {
            new Round {
                Id = "match1",
                Teams = new List<Round.Team> {
                    new Round.Team { Name = "Team1" },
                    new Round.Team { Name = "Team2" }
                }
            }
        };

        // Mock Discord client and channel
        var mockDiscordClient = new Mock<DiscordClient>();
        var mockChannel = new Mock<DiscordChannel>();

        // Mock the thread channels that would be created
        var mockThread1 = new Mock<DiscordChannel>();
        var mockThread2 = new Mock<DiscordChannel>();

        // Set up the thread IDs
        mockThread1.Setup(t => t.Id).Returns(1001UL);
        mockThread2.Setup(t => t.Id).Returns(1002UL);

        // Set up the channel CreateThreadAsync method to return our mock threads
        int createThreadCallCount = 0;
        mockChannel
            .Setup(c => c.CreateThreadAsync(
                It.IsAny<string>(),
                It.IsAny<ChannelType>(),
                It.IsAny<AutoArchiveDuration>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync((string name, ChannelType type, AutoArchiveDuration duration, string reason, bool invitable) =>
            {
                createThreadCallCount++;
                return createThreadCallCount == 1 ? mockThread1.Object : mockThread2.Object;
            });

        // Create tournament service with mocked dependencies
        var mockStateService = new Mock<IStateService>();
        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            mockStateService.Object,
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        await tournamentService.CreateMatchThreads(matches, mockChannel.Object, mockDiscordClient.Object);

        // Assert
        // Verify CreateThreadAsync was called twice (once per team)
        mockChannel.Verify(c => c.CreateThreadAsync(
            It.IsAny<string>(),
            It.IsAny<ChannelType>(),
            It.IsAny<AutoArchiveDuration>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Exactly(2));

        // Verify threads were assigned to teams
        Assert.Equal(1001UL, matches[0].Teams[0].Thread?.Id);
        Assert.Equal(1002UL, matches[0].Teams[1].Thread?.Id);

        // Verify tournament state was saved
        mockStateService.Verify(s => s.SaveTournamentStateAsync(It.IsAny<DiscordClient>()), Times.Once);
    }
}