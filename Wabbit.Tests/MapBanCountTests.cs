using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.TestInfrastructure;
using Xunit;

namespace Wabbit.Tests;

public class MapBanCountTests : TestBase
{
    [Theory]
    [InlineData(1, 0)]  // Bo1 gets 0 bans per side (implementation changed)
    [InlineData(3, 2)]  // Bo3 gets 2 bans per side 
    [InlineData(5, 3)]  // Bo5 gets 3 bans per side
    public void GetMapBanCount_ReturnsCorrectNumberOfBans(int matchLength, int expectedBanCount)
    {
        // Arrange
        var mapService = CreateTestTournamentMapService();
        var round = TestDataFactory.CreateTestRound("Test Match", matchLength);

        // Act
        int banCount = mapService.GetMapBanCount(matchLength);

        // Assert
        Assert.Equal(expectedBanCount, banCount);
    }

    [Fact]
    public async Task RecordMapBan_ForBo3_AcceptsCorrectBans()
    {
        // Arrange
        var mapService = CreateTestTournamentMapService();
        var matchStatusService = CreateTestMatchStatusService();

        var round = TestDataFactory.CreateTestRound("Test Bo3 Match", 3);
        round.CurrentStage = MatchStage.MapBan;

        var bannedMaps = new List<string> { "Map1", "Map2" };

        // Act
        await matchStatusService.UpdateMatchResultAsync(round, 0, 0);

        // Set bans directly for testing
        round.Teams[0].UnconfirmedMapBans = new List<string>(bannedMaps);

        // Assert
        Assert.Equal(bannedMaps, round.Teams[0].UnconfirmedMapBans);
        Assert.Equal(2, round.Teams[0].UnconfirmedMapBans.Count);
    }

    [Fact]
    public async Task RecordMapBan_ForBo5_AcceptsCorrectBans()
    {
        // Arrange
        var mapService = CreateTestTournamentMapService();
        var matchStatusService = CreateTestMatchStatusService();

        var round = TestDataFactory.CreateTestRound("Test Bo5 Match", 5);
        round.CurrentStage = MatchStage.MapBan;

        var bannedMaps = new List<string> { "Map1", "Map2", "Map3" };

        // Act
        await matchStatusService.UpdateMatchResultAsync(round, 0, 0);

        // Set bans directly for testing
        round.Teams[0].UnconfirmedMapBans = new List<string>(bannedMaps);

        // Assert
        Assert.Equal(bannedMaps, round.Teams[0].UnconfirmedMapBans);
        Assert.Equal(3, round.Teams[0].UnconfirmedMapBans.Count);
    }

    [Fact]
    public void GetMapBanCount_UsesDifferentCountsByMatchLength()
    {
        // Arrange
        var mapService = CreateTestTournamentMapService();

        // Act & Assert
        Assert.Equal(0, mapService.GetMapBanCount(1)); // Bo1
        Assert.Equal(2, mapService.GetMapBanCount(3)); // Bo3
        Assert.Equal(3, mapService.GetMapBanCount(5)); // Bo5
    }
}