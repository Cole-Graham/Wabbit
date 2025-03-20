using Moq;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.Helpers;
using Xunit;

namespace Wabbit.Tests;

public class SeedDistributionTests
{
    [Fact]
    public void DistributeParticipants_WithSeeding_PlacesInCorrectGroups()
    {
        // Arrange
        var participants = new List<ParticipantInfo>
        {
            new ParticipantInfo { Id = 1, Username = "Player1", Seed = 1 },
            new ParticipantInfo { Id = 2, Username = "Player2", Seed = 2 },
            new ParticipantInfo { Id = 3, Username = "Player3", Seed = 3 },
            new ParticipantInfo { Id = 4, Username = "Player4", Seed = 4 },
            new ParticipantInfo { Id = 5, Username = "Player5", Seed = 5 },
            new ParticipantInfo { Id = 6, Username = "Player6", Seed = 6 },
            new ParticipantInfo { Id = 7, Username = "Player7", Seed = 7 },
            new ParticipantInfo { Id = 8, Username = "Player8", Seed = 8 }
        };

        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        // Should create 2 groups of 4 players
        Assert.Equal(2, groups.Count);
        Assert.Equal(4, groups[0].Participants.Count);
        Assert.Equal(4, groups[1].Participants.Count);

        // Top seeds should be distributed across groups (groups ordered by name)
        var group0Participants = groups[0].Participants.OrderBy(p => p.Seed).ToList();
        var group1Participants = groups[1].Participants.OrderBy(p => p.Seed).ToList();

        // Check for S-curve seeding: 
        // Group 1: 1, 4, 5, 8
        // Group 2: 2, 3, 6, 7

        // First group should have seeds 1, 4, 5, 8
        Assert.Equal(1, group0Participants[0].Seed);
        Assert.Equal(4, group0Participants[1].Seed);
        Assert.Equal(5, group0Participants[2].Seed);
        Assert.Equal(8, group0Participants[3].Seed);

        // Second group should have seeds 2, 3, 6, 7
        Assert.Equal(2, group1Participants[0].Seed);
        Assert.Equal(3, group1Participants[1].Seed);
        Assert.Equal(6, group1Participants[2].Seed);
        Assert.Equal(7, group1Participants[3].Seed);
    }

    [Fact]
    public void DistributeParticipants_WithMixedSeeding_PrioritizesSeededPlayers()
    {
        // Arrange
        var participants = new List<ParticipantInfo>
        {
            new ParticipantInfo { Id = 1, Username = "Player1", Seed = 1 },
            new ParticipantInfo { Id = 2, Username = "Player2", Seed = 2 },
            new ParticipantInfo { Id = 3, Username = "Player3" }, // No seed
            new ParticipantInfo { Id = 4, Username = "Player4" }, // No seed
            new ParticipantInfo { Id = 5, Username = "Player5", Seed = 3 },
            new ParticipantInfo { Id = 6, Username = "Player6" }  // No seed
        };

        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        // Should create 2 groups of 3 players
        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups[0].Participants.Count);
        Assert.Equal(3, groups[1].Participants.Count);

        // Check that seeds 1-3 are distributed across different groups
        var group0Seeds = groups[0].Participants
            .Where(p => p.Seed.HasValue)
            .Select(p => p.Seed!.Value)
            .ToList();

        var group1Seeds = groups[1].Participants
            .Where(p => p.Seed.HasValue)
            .Select(p => p.Seed!.Value)
            .ToList();

        // Each group should have at least one seeded player
        Assert.NotEmpty(group0Seeds);
        Assert.NotEmpty(group1Seeds);

        // No group should have all 3 seeded players
        Assert.False(group0Seeds.Count == 3 || group1Seeds.Count == 3);

        // Combined, all 3 seeded players should be present
        var allSeeds = group0Seeds.Concat(group1Seeds).OrderBy(s => s).ToList();
        Assert.Equal(new List<int> { 1, 2, 3 }, allSeeds);
    }

    [Fact]
    public void DistributeParticipants_WithoutSeeding_DistributesRandomly()
    {
        // Arrange
        var participants = TestHelpers.CreateMockParticipants(8);

        var tournamentService = new TournamentService(
            Mock.Of<ILogger<TournamentService>>(),
            Mock.Of<IStateService>(),
            Mock.Of<ITournamentMapService>(),
            Mock.Of<IMatchStatusService>());

        // Act
        var groups = tournamentService.CreateGroups(participants);

        // Assert
        // Should create 2 groups of 4 players
        Assert.Equal(2, groups.Count);
        Assert.Equal(4, groups[0].Participants.Count);
        Assert.Equal(4, groups[1].Participants.Count);

        // All participants should be distributed (no duplicates, no missing)
        var allParticipantIds = groups
            .SelectMany(g => g.Participants.Select(p => p.PlayerId))
            .OrderBy(id => id)
            .ToList();

        var expectedIds = participants
            .Select(p => p.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(expectedIds, allParticipantIds);
    }
}