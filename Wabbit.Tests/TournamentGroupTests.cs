using Moq;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.Helpers;
using Xunit;

namespace Wabbit.Tests;

public class TournamentGroupTests
{
    private readonly Mock<ITournamentService> _mockTournamentService;

    public TournamentGroupTests()
    {
        _mockTournamentService = new Mock<ITournamentService>();
    }

    [Theory]
    [InlineData(5, 1, 5)]  // 5 players should create 1 group of 5
    [InlineData(8, 2, 4)]  // 8 players should create 2 groups of 4
    [InlineData(9, 3, 3)]  // 9 players should create 3 groups of 3
    [InlineData(10, 2, 5)] // 10 players should create 2 groups of 5
    [InlineData(16, 4, 4)] // 16 players should create 4 groups of 4
    public void CreateGroups_ReturnsCorrectNumberOfGroups(int playerCount, int expectedGroupCount, int expectedGroupSize)
    {
        // Arrange
        var participants = TestHelpers.CreateMockParticipants(playerCount);
        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        Assert.Equal(expectedGroupCount, groups.Count);
        Assert.All(groups, g => Assert.Equal(expectedGroupSize, g.Participants.Count));
    }

    [Fact]
    public void CreateGroups_WithOddNumbers_DistributesEvenly()
    {
        // Arrange
        var participants = TestHelpers.CreateMockParticipants(11);
        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        Assert.Equal(3, groups.Count);

        // Distribution should be 4, 4, 3 (most likely) - check all groups have at least 3 and at most 4 participants
        var participantCounts = groups.Select(g => g.Participants.Count).ToList();
        Assert.All(participantCounts, count => Assert.InRange(count, 3, 4));

        // Check that the total number of participants is correct
        Assert.Equal(11, groups.Sum(g => g.Participants.Count));
    }

    // Edge cases
    [Theory]
    [InlineData(1)]    // Single player
    [InlineData(2)]    // Two players
    [InlineData(3)]    // Three players
    [InlineData(100)]  // Large tournament
    public void CreateGroups_HandlesEdgeCases(int playerCount)
    {
        // Arrange
        var participants = TestHelpers.CreateMockParticipants(playerCount);
        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        Assert.NotNull(groups);
        Assert.Equal(playerCount, groups.Sum(g => g.Participants.Count));
    }
}