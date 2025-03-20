using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Wabbit.Services;
using Wabbit.Services.Interfaces;
using ModelsTournament = Wabbit.Models.Tournament;
using ModelsParticipantInfo = Wabbit.Models.ParticipantInfo;
using TestTournament = Wabbit.Tests.TestInfrastructure.Models.Tournament;
using TestParticipantInfo = Wabbit.Tests.TestInfrastructure.Models.ParticipantInfo;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Test-specific adapter for TournamentService that adds methods needed for testing
    /// </summary>
    public class TestTournamentService
    {
        private readonly TournamentService _tournamentService;
        private readonly TestTournamentGroupService _groupService;
        private readonly ILogger<TournamentService> _logger;

        public TestTournamentService(
            ILogger<TournamentService> logger,
            ITournamentManagerService tournamentManagerService,
            TestTournamentGroupService groupService,
            ITournamentStateService stateService,
            ITournamentPlayoffService playoffService,
            ITournamentStateValidator stateValidator)
        {
            _logger = logger;
            _groupService = groupService;
            _tournamentService = new TournamentService(
                logger,
                tournamentManagerService,
                groupService,
                stateService,
                playoffService,
                stateValidator);
        }

        /// <summary>
        /// Creates a new tournament
        /// </summary>
        public async Task<ModelsTournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            Wabbit.Models.TournamentFormat format,
            DiscordChannel announcementChannel,
            Wabbit.Models.TournamentGameType gameType = Wabbit.Models.TournamentGameType.OneVOne,
            Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            return await _tournamentService.CreateTournamentAsync(
                name,
                players,
                format,
                announcementChannel,
                gameType,
                playerSeeds);
        }

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        public ModelsTournament? GetTournament(string name)
        {
            return _tournamentService.GetTournament(name);
        }

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        public List<ModelsTournament> GetAllTournaments()
        {
            return _tournamentService.GetAllTournaments();
        }

        /// <summary>
        /// Starts a tournament
        /// </summary>
        public Task StartTournamentAsync(ModelsTournament tournament, DiscordClient client)
        {
            return _tournamentService.StartTournamentAsync(tournament, client);
        }

        /// <summary>
        /// Create groups based on the provided list of participants
        /// This is an adapter for the test-expected method
        /// </summary>
        public List<ModelsTournament.Group> CreateGroups(List<TestParticipantInfo> participants)
        {
            // Convert test participants to model participants
            var modelParticipants = ModelConverters.ToProductionModels(participants);

            return _groupService.CreateGroups(modelParticipants);
        }

        /// <summary>
        /// Creates match threads for a list of matches
        /// </summary>
        public async Task CreateMatchThreads(List<Wabbit.Models.Round> matches, DiscordChannel channel, DiscordClient client)
        {
            foreach (var match in matches)
            {
                foreach (var team in match.Teams)
                {
                    var mockThread = new Mock<DiscordThreadChannel>();
                    mockThread.Setup(t => t.Id).Returns((ulong)(4000 + System.Random.Shared.Next(1000)));
                    mockThread.Setup(t => t.Name).Returns($"{match.Name} - {team.Name}");
                    team.Thread = mockThread.Object;
                }
            }

            // This is simpler than the actual implementation, but sufficient for testing
            await Task.CompletedTask;
        }

        /// <summary>
        /// Creates group matches
        /// </summary>
        public List<Wabbit.Models.Round> CreateGroupMatches(ModelsTournament.Group group)
        {
            var matches = new List<Wabbit.Models.Round>();

            // Create a match for each pair of participants
            for (int i = 0; i < group.Participants.Count; i++)
            {
                for (int j = i + 1; j < group.Participants.Count; j++)
                {
                    var match = new Wabbit.Models.Round
                    {
                        Name = $"{group.Name} - Match {matches.Count + 1}",
                        Teams = new List<Wabbit.Models.Round.Team>
                        {
                            new Wabbit.Models.Round.Team
                            {
                                Name = group.Participants[i].Player.ToString(),
                                Participants = new List<Wabbit.Models.Round.Participant>
                                {
                                    new Wabbit.Models.Round.Participant { Player = group.Participants[i].Player as DiscordMember }
                                }
                            },
                            new Wabbit.Models.Round.Team
                            {
                                Name = group.Participants[j].Player.ToString(),
                                Participants = new List<Wabbit.Models.Round.Participant>
                                {
                                    new Wabbit.Models.Round.Participant { Player = group.Participants[j].Player as DiscordMember }
                                }
                            }
                        },
                        Length = 3,
                        OneVOne = true
                    };

                    matches.Add(match);
                }
            }

            return matches;
        }

        /// <summary>
        /// Determines if a match is complete
        /// </summary>
        public bool IsMatchComplete(Wabbit.Models.Round round)
        {
            if (round.Teams.Count != 2)
                return false;

            var team1 = round.Teams[0];
            var team2 = round.Teams[1];

            int requiredWins = round.Length == 5 ? 3 : (round.Length == 3 ? 2 : 1);

            return team1.Wins >= requiredWins || team2.Wins >= requiredWins;
        }

        /// <summary>
        /// Gets participants from a group who should advance to playoffs
        /// </summary>
        public List<ModelsTournament.GroupParticipant> GetAdvancingParticipants(ModelsTournament.Group group, int count)
        {
            return _groupService.GetAdvancingParticipants(group, count);
        }

        /// <summary>
        /// Checks if a group stage is complete
        /// </summary>
        public bool IsGroupStageComplete(ModelsTournament.Group group)
        {
            return _groupService.IsGroupStageComplete(group);
        }

        /// <summary>
        /// Generates a single elimination bracket
        /// </summary>
        public List<TestTournament.Bracket> GenerateSingleEliminationBracket(List<ModelsTournament.GroupParticipant> participants)
        {
            // Note: This is a simplified implementation for testing
            var brackets = new List<TestTournament.Bracket>();

            // Sort participants by seed
            var seededParticipants = new List<ModelsTournament.GroupParticipant>(participants);
            seededParticipants.Sort((a, b) => a.Seed.CompareTo(b.Seed));

            // Create a simple single-elimination bracket
            int participantCount = seededParticipants.Count;

            // Determine number of rounds needed
            int roundCount = 1;
            int matchCount = 1;
            while (matchCount < participantCount)
            {
                roundCount++;
                matchCount *= 2;
            }

            // Create bracket rounds
            for (int i = 0; i < roundCount; i++)
            {
                var roundName = i switch
                {
                    0 => participantCount >= 8 ? "Quarter-finals" : (participantCount >= 4 ? "Semi-finals" : "Final"),
                    1 => participantCount >= 8 ? "Semi-finals" : "Final",
                    2 => "Final",
                    _ => $"Round {i + 1}"
                };

                var bracket = new TestTournament.Bracket
                {
                    Name = roundName,
                    Matches = new List<TestTournament.Match>()
                };

                int matchesInRound = i == 0 ? matchCount / 2 : brackets[i - 1].Matches.Count / 2;

                for (int j = 0; j < matchesInRound; j++)
                {
                    var match = new TestTournament.Match
                    {
                        Name = $"{roundName} Match {j + 1}",
                        Participants = new List<TestTournament.MatchParticipant>()
                    };

                    // For the first round, add participants
                    if (i == 0)
                    {
                        // Add participants in a seeded bracket order (1 vs 8, 4 vs 5, 2 vs 7, 3 vs 6)
                        if (j < seededParticipants.Count / 2)
                        {
                            int seed1 = 0;
                            int seed2 = 0;

                            // Determine seed positions using standard bracket seeding
                            if (j == 0) { seed1 = 0; seed2 = seededParticipants.Count - 1; }
                            else if (j == 1) { seed1 = 3; seed2 = 4; }
                            else if (j == 2) { seed1 = 2; seed2 = seededParticipants.Count - 3; }
                            else if (j == 3) { seed1 = 1; seed2 = seededParticipants.Count - 2; }

                            // Only add participants if the indices are valid
                            if (seed1 < seededParticipants.Count)
                            {
                                match.Participants.Add(new TestTournament.MatchParticipant
                                {
                                    Player = seededParticipants[seed1].Player,
                                    Score = 0
                                });
                            }

                            if (seed2 < seededParticipants.Count)
                            {
                                match.Participants.Add(new TestTournament.MatchParticipant
                                {
                                    Player = seededParticipants[seed2].Player,
                                    Score = 0
                                });
                            }
                        }
                    }

                    bracket.Matches.Add(match);
                }

                brackets.Add(bracket);
            }

            return brackets;
        }

        /// <summary>
        /// Generates a double elimination bracket
        /// </summary>
        public List<TestTournament.Bracket> GenerateDoubleEliminationBracket(List<ModelsTournament.GroupParticipant> participants)
        {
            var brackets = new List<TestTournament.Bracket>();

            // Winners bracket
            var winnersBracket = new TestTournament.Bracket
            {
                Name = "Winners Bracket",
                Matches = new List<TestTournament.Match>()
            };

            // Create matches with participants for the first round
            var seededParticipants = new List<ModelsTournament.GroupParticipant>(participants);
            seededParticipants.Sort((a, b) => a.Seed.CompareTo(b.Seed));

            // Create first round matches (1 vs 4, 2 vs 3)
            var match1 = new TestTournament.Match
            {
                Name = "Winners Round 1 Match 1",
                Participants = new List<TestTournament.MatchParticipant>()
            };

            if (seededParticipants.Count > 0)
            {
                match1.Participants.Add(new TestTournament.MatchParticipant
                {
                    Player = seededParticipants[0].Player,
                    Score = 0
                });
            }

            if (seededParticipants.Count > 3)
            {
                match1.Participants.Add(new TestTournament.MatchParticipant
                {
                    Player = seededParticipants[3].Player,
                    Score = 0
                });
            }

            var match2 = new TestTournament.Match
            {
                Name = "Winners Round 1 Match 2",
                Participants = new List<TestTournament.MatchParticipant>()
            };

            if (seededParticipants.Count > 1)
            {
                match2.Participants.Add(new TestTournament.MatchParticipant
                {
                    Player = seededParticipants[1].Player,
                    Score = 0
                });
            }

            if (seededParticipants.Count > 2)
            {
                match2.Participants.Add(new TestTournament.MatchParticipant
                {
                    Player = seededParticipants[2].Player,
                    Score = 0
                });
            }

            // Add winners final
            var winnersFinal = new TestTournament.Match
            {
                Name = "Winners Final",
                Participants = new List<TestTournament.MatchParticipant>()
            };

            winnersBracket.Matches.Add(match1);
            winnersBracket.Matches.Add(match2);
            winnersBracket.Matches.Add(winnersFinal);

            // Losers bracket
            var losersBracket = new TestTournament.Bracket
            {
                Name = "Losers Bracket",
                Matches = new List<TestTournament.Match>
                {
                    new TestTournament.Match { Name = "Losers Round 1 Match 1", Participants = new List<TestTournament.MatchParticipant>() },
                    new TestTournament.Match { Name = "Losers Round 1 Match 2", Participants = new List<TestTournament.MatchParticipant>() },
                    new TestTournament.Match { Name = "Losers Final", Participants = new List<TestTournament.MatchParticipant>() }
                }
            };

            // Grand finals
            var grandFinals = new TestTournament.Bracket
            {
                Name = "Grand Finals",
                Matches = new List<TestTournament.Match>
                {
                    new TestTournament.Match { Name = "Grand Finals", Participants = new List<TestTournament.MatchParticipant>() }
                }
            };

            brackets.Add(winnersBracket);
            brackets.Add(losersBracket);
            brackets.Add(grandFinals);

            return brackets;
        }
    }
}