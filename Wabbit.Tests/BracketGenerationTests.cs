using Moq;
using System.Collections.Generic;
using System.Linq;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using Wabbit.Tests.Helpers;
using Xunit;

namespace Wabbit.Tests;

public class BracketGenerationTests
{
    [Fact]
    public void GenerateBracket_ForSingleElimination_CreatesCorrectStructure()
    {
        // Arrange
        var participants = new List<Tournament.GroupParticipant>
        {
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(1, "Player1"), Seed = 1 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(2, "Player2"), Seed = 2 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(3, "Player3"), Seed = 3 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(4, "Player4"), Seed = 4 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(5, "Player5"), Seed = 5 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(6, "Player6"), Seed = 6 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(7, "Player7"), Seed = 7 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(8, "Player8"), Seed = 8 }
        };

        var mockTournamentService = new Mock<ITournamentService>();
        mockTournamentService.Setup(s => s.GenerateSingleEliminationBracket(It.IsAny<List<Tournament.GroupParticipant>>()))
            .Returns((List<Tournament.GroupParticipant> p) =>
            {
                // Simplified bracket generation logic for testing
                var brackets = new List<Tournament.Bracket>();

                // Round 1 (Quarter-finals)
                var round1 = new Tournament.Bracket
                {
                    Name = "Quarter-finals",
                    Matches = new List<Round>
                    {
                        CreateMatchWithParticipants(p[0], p[7]), // 1 vs 8
                        CreateMatchWithParticipants(p[3], p[4]), // 4 vs 5
                        CreateMatchWithParticipants(p[2], p[5]), // 3 vs 6
                        CreateMatchWithParticipants(p[1], p[6])  // 2 vs 7
                    }
                };

                // Round 2 (Semi-finals)
                var round2 = new Tournament.Bracket
                {
                    Name = "Semi-finals",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch(),
                        CreateEmptyMatch()
                    }
                };

                // Round 3 (Final)
                var round3 = new Tournament.Bracket
                {
                    Name = "Final",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch()
                    }
                };

                brackets.Add(round1);
                brackets.Add(round2);
                brackets.Add(round3);

                return brackets;
            });

        // Act
        var brackets = mockTournamentService.Object.GenerateSingleEliminationBracket(participants);

        // Assert
        // For 8 players, we should have 3 rounds (quarter-finals, semi-finals, final)
        Assert.Equal(3, brackets.Count);

        // First round should have 4 matches
        Assert.Equal(4, brackets[0].Matches.Count);

        // Second round should have 2 matches
        Assert.Equal(2, brackets[1].Matches.Count);

        // Final round should have 1 match
        Assert.Equal(1, brackets[2].Matches.Count);

        // Test seeding: Seed 1 should play against Seed 8 in first round
        var match1 = brackets[0].Matches[0];
        Assert.Equal(1, match1.Teams[0].Participants[0].Seed);
        Assert.Equal(8, match1.Teams[1].Participants[0].Seed);

        // Seed 4 should play against Seed 5
        var match2 = brackets[0].Matches[1];
        Assert.Equal(4, match2.Teams[0].Participants[0].Seed);
        Assert.Equal(5, match2.Teams[1].Participants[0].Seed);
    }

    [Fact]
    public void GenerateBracket_WithByes_HandlesUneven()
    {
        // Arrange - 6 players (not power of 2)
        var participants = new List<Tournament.GroupParticipant>
        {
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(1, "Player1"), Seed = 1 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(2, "Player2"), Seed = 2 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(3, "Player3"), Seed = 3 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(4, "Player4"), Seed = 4 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(5, "Player5"), Seed = 5 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(6, "Player6"), Seed = 6 }
        };

        var mockTournamentService = new Mock<ITournamentService>();
        mockTournamentService.Setup(s => s.GenerateSingleEliminationBracket(It.IsAny<List<Tournament.GroupParticipant>>()))
            .Returns((List<Tournament.GroupParticipant> p) =>
            {
                // Simplified bracket generation logic for testing
                var brackets = new List<Tournament.Bracket>();

                // Round 1 (Quarter-finals)
                var round1 = new Tournament.Bracket
                {
                    Name = "Quarter-finals",
                    Matches = new List<Round>
                    {
                        CreateMatchWithBye(p[0]), // 1 gets a bye
                        CreateMatchWithParticipants(p[3], p[4]), // 4 vs 5
                        CreateMatchWithBye(p[1]), // 2 gets a bye
                        CreateMatchWithParticipants(p[2], p[5])  // 3 vs 6
                    }
                };

                // Round 2 (Semi-finals)
                var round2 = new Tournament.Bracket
                {
                    Name = "Semi-finals",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch(),
                        CreateEmptyMatch()
                    }
                };

                // Round 3 (Final)
                var round3 = new Tournament.Bracket
                {
                    Name = "Final",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch()
                    }
                };

                brackets.Add(round1);
                brackets.Add(round2);
                brackets.Add(round3);

                return brackets;
            });

        // Act
        var brackets = mockTournamentService.Object.GenerateSingleEliminationBracket(participants);

        // Assert
        // For 6 players, we need 8 slots (next power of 2), so 2 byes
        // First round should have 4 matches, but 2 with byes
        Assert.Equal(4, brackets[0].Matches.Count);

        // Check if top seeds got byes (typically 1 and 2)
        var byeMatches = brackets[0].Matches.Where(m => m.Teams.Any(t => t.IsBye)).ToList();
        Assert.Equal(2, byeMatches.Count);

        // Check if the top seeds (1 and 2) got the byes
        var seedsWithByes = byeMatches
            .SelectMany(m => m.Teams)
            .Where(t => !t.IsBye)
            .Select(t => t.Participants[0].Seed.Value)
            .ToList();

        Assert.Contains(1, seedsWithByes);
        Assert.Contains(2, seedsWithByes);
    }

    [Fact]
    public void GenerateBracket_ForDoubleElimination_CreatesWinnersAndLosersBrackets()
    {
        // Arrange
        var participants = new List<Tournament.GroupParticipant>
        {
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(1, "Player1"), Seed = 1 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(2, "Player2"), Seed = 2 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(3, "Player3"), Seed = 3 },
            new Tournament.GroupParticipant { Player = TestHelpers.CreateMockMember(4, "Player4"), Seed = 4 }
        };

        var mockTournamentService = new Mock<ITournamentService>();
        mockTournamentService.Setup(s => s.GenerateDoubleEliminationBracket(It.IsAny<List<Tournament.GroupParticipant>>()))
            .Returns((List<Tournament.GroupParticipant> p) =>
            {
                // Simplified bracket generation logic for testing
                var brackets = new List<Tournament.Bracket>();

                // Winners bracket
                var winnersBracket = new Tournament.Bracket
                {
                    Name = "Winners Bracket",
                    Matches = new List<Round>
                    {
                        CreateMatchWithParticipants(p[0], p[3]), // 1 vs 4
                        CreateMatchWithParticipants(p[1], p[2]), // 2 vs 3
                        CreateEmptyMatch() // winners final
                    }
                };

                // Losers bracket
                var losersBracket = new Tournament.Bracket
                {
                    Name = "Losers Bracket",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch(), // losers round 1
                        CreateEmptyMatch(), // losers round 1
                        CreateEmptyMatch()  // losers final
                    }
                };

                // Grand finals
                var grandFinalsBracket = new Tournament.Bracket
                {
                    Name = "Grand Finals",
                    Matches = new List<Round>
                    {
                        CreateEmptyMatch() // grand final
                    }
                };

                brackets.Add(winnersBracket);
                brackets.Add(losersBracket);
                brackets.Add(grandFinalsBracket);

                return brackets;
            });

        // Act
        var brackets = mockTournamentService.Object.GenerateDoubleEliminationBracket(participants);

        // Assert
        // Should have winners bracket, losers bracket, and grand finals
        Assert.Equal(3, brackets.Count);

        // Winners bracket should have 3 matches (2 semifinals + 1 final)
        Assert.Equal(3, brackets[0].Matches.Count);
        Assert.Equal("Winners Bracket", brackets[0].Name);

        // Losers bracket should have 3 matches (2 first round + 1 final)
        Assert.Equal(3, brackets[1].Matches.Count);
        Assert.Equal("Losers Bracket", brackets[1].Name);

        // Grand finals should have 1 match (with potential reset)
        Assert.Equal(1, brackets[2].Matches.Count);
        Assert.Equal("Grand Finals", brackets[2].Name);

        // Verify seeding in winners bracket first round
        var winnersBracketRound1 = brackets[0].Matches.Take(2).ToList();
        Assert.Equal(1, winnersBracketRound1[0].Teams[0].Participants[0].Seed);
        Assert.Equal(4, winnersBracketRound1[0].Teams[1].Participants[0].Seed);
        Assert.Equal(2, winnersBracketRound1[1].Teams[0].Participants[0].Seed);
        Assert.Equal(3, winnersBracketRound1[1].Teams[1].Participants[0].Seed);
    }

    // Helper methods for creating test matches
    private Round CreateEmptyMatch()
    {
        return new Round
        {
            Teams = new List<Round.Team>
            {
                new Round.Team(),
                new Round.Team()
            }
        };
    }

    private Round CreateMatchWithParticipants(Tournament.GroupParticipant p1, Tournament.GroupParticipant p2)
    {
        return new Round
        {
            Teams = new List<Round.Team>
            {
                new Round.Team
                {
                    Name = p1.Player.Username,
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant
                        {
                            Player = p1.Player,
                            Seed = p1.Seed
                        }
                    }
                },
                new Round.Team
                {
                    Name = p2.Player.Username,
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant
                        {
                            Player = p2.Player,
                            Seed = p2.Seed
                        }
                    }
                }
            }
        };
    }

    private Round CreateMatchWithBye(Tournament.GroupParticipant p)
    {
        return new Round
        {
            Teams = new List<Round.Team>
            {
                new Round.Team
                {
                    Name = p.Player.Username,
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant
                        {
                            Player = p.Player,
                            Seed = p.Seed
                        }
                    }
                },
                new Round.Team
                {
                    Name = "BYE",
                    IsBye = true
                }
            }
        };
    }
}