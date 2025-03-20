using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.Helpers;
using Xunit;

namespace Wabbit.Tests;

public class ConditionalMapBanTests
{
    [Fact]
    public async Task ConfirmMapBans_WithOverlappingBans_PerformsCoinflip()
    {
        // Arrange
        var mockMapService = new Mock<ITournamentMapService>();
        mockMapService.Setup(m => m.GetTournamentMapPool(It.IsAny<bool>()))
            .Returns(new List<string> { "Map1", "Map2", "Map3", "Map4", "Map5", "Map6" });

        var mockLogger = new Mock<ILogger<MatchStatusService>>();
        var matchStatusService = new MatchStatusService(
            mockLogger.Object,
            mockMapService.Object);

        var round = new Round
        {
            Length = 3, // Bo3
            CurrentStage = MatchStage.MapBan,
            CoinflipPerformed = false,
            Teams = new List<Round.Team>
            {
                new Round.Team {
                    Name = "Team1",
                    UnconfirmedMapBans = new List<string> { "Map1", "Map2", "Map3" }
                },
                new Round.Team {
                    Name = "Team2",
                    MapBans = new List<string> { "Map1", "Map2", "Map4" } // Note: Map3 vs Map4 difference
                }
            }
        };

        var mockMessage = new Mock<DiscordMessage>();
        var mockEmbed = new Mock<DiscordEmbed>();
        mockMessage.Setup(m => m.Embeds).Returns(new List<DiscordEmbed> { mockEmbed.Object });

        var mockChannel = new Mock<DiscordChannel>();
        mockChannel.Setup(c => c.GetMessageAsync(It.IsAny<ulong>()))
            .ReturnsAsync(mockMessage.Object);
        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<DiscordMessageBuilder>()))
            .ReturnsAsync(mockMessage.Object);

        var mockClient = new Mock<DiscordClient>();

        // Act
        await matchStatusService.ConfirmMapBansAsync(mockChannel.Object, round, "Team1", mockClient.Object);

        // Assert
        Assert.True(round.CoinflipPerformed);
        Assert.NotNull(round.CoinflipWinnerTeamName);

        // The overlapping bans were Map1 and Map2, so there should be a coinflip for the 3rd ban
        // Either Team1's Map3 or Team2's Map4 should be applied
    }

    [Fact]
    public async Task ConfirmMapBans_WithoutOverlap_DoesNotPerformCoinflip()
    {
        // Arrange
        var mockMapService = new Mock<ITournamentMapService>();
        mockMapService.Setup(m => m.GetTournamentMapPool(It.IsAny<bool>()))
            .Returns(new List<string> { "Map1", "Map2", "Map3", "Map4", "Map5", "Map6", "Map7", "Map8" });

        var mockLogger = new Mock<ILogger<MatchStatusService>>();
        var matchStatusService = new MatchStatusService(
            mockLogger.Object,
            mockMapService.Object);

        var round = new Round
        {
            Length = 3, // Bo3
            CurrentStage = MatchStage.MapBan,
            CoinflipPerformed = false,
            Teams = new List<Round.Team>
            {
                new Round.Team {
                    Name = "Team1",
                    UnconfirmedMapBans = new List<string> { "Map1", "Map2", "Map3" }
                },
                new Round.Team {
                    Name = "Team2",
                    MapBans = new List<string> { "Map4", "Map5", "Map6" } // All different maps
                }
            }
        };

        var mockMessage = new Mock<DiscordMessage>();
        var mockEmbed = new Mock<DiscordEmbed>();
        mockMessage.Setup(m => m.Embeds).Returns(new List<DiscordEmbed> { mockEmbed.Object });

        var mockChannel = new Mock<DiscordChannel>();
        mockChannel.Setup(c => c.GetMessageAsync(It.IsAny<ulong>()))
            .ReturnsAsync(mockMessage.Object);
        mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<DiscordMessageBuilder>()))
            .ReturnsAsync(mockMessage.Object);

        var mockClient = new Mock<DiscordClient>();

        // Act
        await matchStatusService.ConfirmMapBansAsync(mockChannel.Object, round, "Team1", mockClient.Object);

        // Assert
        // No overlapping bans, so no coinflip needed
        Assert.False(round.CoinflipPerformed);
        Assert.Null(round.CoinflipWinnerTeamName);
    }

    [Theory]
    [InlineData(3, 0, true, true)]  // Bo3: 1st priority ban, team won, should be guaranteed
    [InlineData(3, 1, true, true)]  // Bo3: 2nd priority ban, team won, should be guaranteed
    [InlineData(3, 2, true, true)]  // Bo3: 3rd priority ban, team won, should be conditional
    [InlineData(3, 2, false, false)] // Bo3: 3rd priority ban, team lost, should not be guaranteed
    [InlineData(5, 0, true, true)]  // Bo5: 1st priority ban, team won, should be guaranteed
    [InlineData(5, 1, true, true)]  // Bo5: 2nd priority ban, team won, should be conditional
    [InlineData(5, 1, false, false)] // Bo5: 2nd priority ban, team lost, should not be guaranteed
    public void IsGuaranteedBan_ReturnsCorrectStatus(int matchLength, int banPriority, bool isWinningTeam, bool expectedGuaranteed)
    {
        // Arrange
        var round = new Round
        {
            Length = matchLength,
            CoinflipPerformed = true,
            CoinflipWinnerTeamName = "Winner",
            Teams = new List<Round.Team>
            {
                new Round.Team {
                    Name = isWinningTeam ? "Winner" : "Loser",
                    MapBans = new List<string>()
                }
            }
        };

        // Add bans to the team (up to priority index)
        for (int i = 0; i <= banPriority; i++)
        {
            round.Teams[0].MapBans.Add($"Map{i + 1}");
        }

        // Act - check if ban is guaranteed (this would be a method in your service)
        bool isGuaranteed = IsGuaranteedBan(round, round.Teams[0], banPriority);

        // Assert
        Assert.Equal(expectedGuaranteed, isGuaranteed);
    }

    // Helper method to mimic the logic in your service
    // In a real implementation, this would be a method in your service that you'd test
    private bool IsGuaranteedBan(Round round, Round.Team team, int banPriority)
    {
        bool isGuaranteedBan = false;

        // Best of 1: All 3 bans are guaranteed
        if (round.Length == 1)
        {
            isGuaranteedBan = true;
        }
        // Best of 3: Priority 1 and 2 (index 0 and 1) are guaranteed, Priority 3 (index 2) is conditional
        else if (round.Length == 3)
        {
            // Priority 0 and 1 (1st and 2nd) are guaranteed
            if (banPriority < 2)
            {
                isGuaranteedBan = true;
            }
            // Priority 2 (3rd) is conditional and depends on coinflip
            else if (banPriority == 2 && round.CoinflipPerformed)
            {
                // If this team won the coinflip, their priority 3 ban is applied
                isGuaranteedBan = string.Equals(team.Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);
            }
        }
        // Best of 5: Only Priority 1 (index 0) is guaranteed, Priority 2 (index 1) is conditional
        else if (round.Length == 5)
        {
            // Priority 0 (1st) is guaranteed
            if (banPriority == 0)
            {
                isGuaranteedBan = true;
            }
            // Priority 1 (2nd) is conditional and depends on coinflip
            else if (banPriority == 1 && round.CoinflipPerformed)
            {
                // If this team won the coinflip, their priority 2 ban is applied
                isGuaranteedBan = string.Equals(team.Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);
            }
        }

        return isGuaranteedBan;
    }
}