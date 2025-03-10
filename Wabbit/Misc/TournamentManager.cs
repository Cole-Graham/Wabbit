using DSharpPlus.Entities;
using Wabbit.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MatchType = Wabbit.Models.MatchType;

namespace Wabbit.Misc
{
    public class TournamentManager
    {
        private readonly List<Tournament> _activeTournaments = [];
        private readonly OngoingRounds _ongoingRounds;

        // Helper methods for handling object/DiscordMember properties
        private string GetPlayerDisplayName(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.DisplayName;
            return player?.ToString() ?? "Unknown";
        }

        private ulong? GetPlayerId(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.Id;
            return null;
        }

        private string GetPlayerMention(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.Mention;
            return GetPlayerDisplayName(player);
        }

        private bool ComparePlayerIds(object? player1, object? player2)
        {
            // If both are DiscordMember, compare IDs
            if (player1 is DSharpPlus.Entities.DiscordMember member1 &&
                player2 is DSharpPlus.Entities.DiscordMember member2)
                return member1.Id == member2.Id;

            // Otherwise compare by reference or ToString
            if (ReferenceEquals(player1, player2))
                return true;

            return player1?.ToString() == player2?.ToString();
        }

        private DSharpPlus.Entities.DiscordMember? ConvertToDiscordMember(object? player)
        {
            return player as DSharpPlus.Entities.DiscordMember;
        }

        public TournamentManager(OngoingRounds ongoingRounds)
        {
            _ongoingRounds = ongoingRounds;
        }

        public Tournament CreateTournament(string name, List<DiscordMember> players, TournamentFormat format, DiscordChannel announcementChannel)
        {
            var tournament = new Tournament
            {
                Name = name,
                Format = format,
                AnnouncementChannel = announcementChannel
            };

            // Create groups based on the number of players
            switch (players.Count)
            {
                case <= 8:
                    // Create 2 groups of 4 or less
                    CreateGroups(tournament, players, 2);
                    break;
                case 9:
                    // Create 3 groups of 3
                    CreateGroups(tournament, players, 3);
                    break;
                case <= 10:
                    // Create 2 groups of 5
                    CreateGroups(tournament, players, 2);
                    break;
                default:
                    // For 11+ players, adjust as needed
                    int groupCount = (players.Count / 4) + (players.Count % 4 > 0 ? 1 : 0);
                    CreateGroups(tournament, players, groupCount);
                    break;
            }

            _activeTournaments.Add(tournament);
            return tournament;
        }

        private void CreateGroups(Tournament tournament, List<DiscordMember> players, int groupCount)
        {
            // Create the groups
            for (int i = 0; i < groupCount; i++)
            {
                var group = new Tournament.Group { Name = $"Group {(char)('A' + i)}" };
                tournament.Groups.Add(group);
            }

            // Distribute players evenly across groups
            var shuffledPlayers = players.OrderBy(_ => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffledPlayers.Count; i++)
            {
                var groupIndex = i % groupCount;
                var group = tournament.Groups[groupIndex];

                var participant = new Tournament.GroupParticipant { Player = shuffledPlayers[i] };
                group.Participants.Add(participant);
            }

            // Create the matches within each group
            foreach (var group in tournament.Groups)
            {
                // Create round-robin matches
                for (int i = 0; i < group.Participants.Count; i++)
                {
                    for (int j = i + 1; j < group.Participants.Count; j++)
                    {
                        var match = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(group.Participants[i].Player)} vs {GetPlayerDisplayName(group.Participants[j].Player)}",
                            Type = MatchType.GroupStage,
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new() { Player = group.Participants[i].Player },
                                new() { Player = group.Participants[j].Player }
                            }
                        };
                        group.Matches.Add(match);
                    }
                }
            }
        }

        public Tournament? GetTournament(string name)
        {
            return _activeTournaments.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public List<Tournament> GetAllTournaments()
        {
            return _activeTournaments;
        }

        public void UpdateMatchResult(Tournament tournament, Tournament.Match match, DiscordMember winner, int winnerScore, int loserScore)
        {
            // Early return if any parameter is null
            if (tournament is null || match is null || winner is null || match.Participants is null)
                return;

            // Use simple reference equality on IDs
            ulong mainWinnerId = winner.Id;  // Store ID once

            Tournament.MatchParticipant? winnerParticipant = null;
            Tournament.MatchParticipant? loserParticipant = null;

            // Find the winner first
            foreach (var p in match.Participants)
            {
                if (p is null || p.Player is null)
                    continue;

                var playerId = GetPlayerId(p.Player);
                if (playerId.HasValue && playerId.Value == mainWinnerId)
                {
                    winnerParticipant = p;
                    break;
                }
            }

            // Then find the non-winner
            if (winnerParticipant != null)
            {
                foreach (var p in match.Participants)
                {
                    if (p is null || p.Player is null || ReferenceEquals(p, winnerParticipant))
                        continue;

                    loserParticipant = p;
                    break;
                }
            }

            if (winnerParticipant == null || loserParticipant == null)
                return;

            winnerParticipant.Score = winnerScore;
            winnerParticipant.IsWinner = true;
            loserParticipant.Score = loserScore;

            match.Result = new Tournament.MatchResult
            {
                Winner = winner,
                CompletedAt = DateTime.Now
            };

            // Update group standings if it's a group stage match
            if (match.Type == MatchType.GroupStage && tournament.Groups != null)
            {
                // Store ID once
                ulong? groupWinnerIdNullable = GetPlayerId(winner);
                if (!groupWinnerIdNullable.HasValue)
                {
                    Console.WriteLine("Error: Winner has no valid ID");
                    return;
                }
                ulong groupWinnerId = groupWinnerIdNullable.Value;
                ulong? groupLoserId = loserParticipant?.Player != null ? GetPlayerId(loserParticipant.Player) : null;

                foreach (var group in tournament.Groups)
                {
                    if (group?.Matches == null)
                        continue;

                    bool containsMatch = false;
                    foreach (var m in group.Matches)
                    {
                        if (m == match)
                        {
                            containsMatch = true;
                            break;
                        }
                    }

                    if (containsMatch && group.Participants != null)
                    {
                        // Update participant stats without using DiscordMember comparisons
                        Tournament.GroupParticipant? groupWinner = null;
                        Tournament.GroupParticipant? groupLoser = null;

                        // Find the winner
                        foreach (var p in group.Participants)
                        {
                            if (p is null || p.Player is null)
                                continue;

                            if (p.Player is null)
                                continue;

                            var pPlayerId = GetPlayerId(p.Player);
                            if (pPlayerId.HasValue && pPlayerId.Value == groupWinnerId)
                            {
                                groupWinner = p;
                                break;
                            }
                        }

                        // Find the loser
                        if (groupLoserId.HasValue)
                        {
                            foreach (var p in group.Participants)
                            {
                                if (p is null || p.Player is null)
                                    continue;

                                if (p.Player is null)
                                    continue;

                                var pPlayerId = GetPlayerId(p.Player);
                                if (pPlayerId.HasValue && pPlayerId.Value == groupLoserId.Value)
                                {
                                    groupLoser = p;
                                    break;
                                }
                            }
                        }

                        if (groupWinner != null)
                        {
                            groupWinner.Wins++;
                            groupWinner.GamesWon += winnerScore;
                            groupWinner.GamesLost += loserScore;
                        }

                        if (groupLoser != null)
                        {
                            groupLoser.Losses++;
                            groupLoser.GamesWon += loserScore;
                            groupLoser.GamesLost += winnerScore;
                        }

                        // Check if group is complete
                        CheckGroupCompletion(group);
                        break;
                    }
                }
            }

            // Check if all groups are complete to begin playoffs
            bool allGroupsComplete = true;
            if (tournament.Groups != null)
            {
                foreach (var g in tournament.Groups)
                {
                    if (g != null && !g.IsComplete)
                    {
                        allGroupsComplete = false;
                        break;
                    }
                }

                if (tournament.CurrentStage == TournamentStage.Groups && allGroupsComplete)
                {
                    SetupPlayoffs(tournament);
                    tournament.CurrentStage = TournamentStage.Playoffs;
                }
            }

            // Check if tournament is complete
            bool allPlayoffsComplete = true;
            if (tournament.PlayoffMatches != null)
            {
                foreach (var m in tournament.PlayoffMatches)
                {
                    if (m != null && !m.IsComplete)
                    {
                        allPlayoffsComplete = false;
                        break;
                    }
                }

                if (tournament.CurrentStage == TournamentStage.Playoffs && allPlayoffsComplete)
                {
                    tournament.CurrentStage = TournamentStage.Complete;
                    tournament.IsComplete = true;
                }
            }
        }

        private void CheckGroupCompletion(Tournament.Group group)
        {
            if (group.Matches?.All(m => m?.IsComplete == true) == true)
            {
                group.IsComplete = true;
            }
        }

        private void SetupPlayoffs(Tournament tournament)
        {
            // Sort participants in each group by points (and tiebreakers if needed)
            foreach (var group in tournament.Groups)
            {
                var sortedParticipants = group.Participants?
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GamesWon)
                    .ThenBy(p => p.GamesLost)
                    .ToList() ?? [];

                // Mark top 2 as advancing to playoffs
                for (int i = 0; i < Math.Min(2, sortedParticipants.Count); i++)
                {
                    sortedParticipants[i].AdvancedToPlayoffs = true;
                }
            }

            // Create semifinal matches
            if (tournament.Groups.Count >= 2)
            {
                var groupA = tournament.Groups[0];
                var groupB = tournament.Groups[1];

                // Special case for 9 players (3 groups of 3) - only best second-place advances
                Tournament.GroupParticipant? bestSecondPlace = null;
                if (tournament.Groups.Count == 3)
                {
                    var secondPlaceFinishers = tournament.Groups
                        .Select(g => g.Participants?.OrderByDescending(p => p.Points)
                                                 .ThenByDescending(p => p.GamesWon)
                                                 .ThenBy(p => p.GamesLost)
                                                 .Skip(1)
                                                 .FirstOrDefault())
                        .Where(p => p != null)
                        .ToList();

                    bestSecondPlace = secondPlaceFinishers
                        .OrderByDescending(p => p!.Points)
                        .ThenByDescending(p => p!.GamesWon)
                        .ThenBy(p => p!.GamesLost)
                        .FirstOrDefault();
                }

                // Create semifinal 1: Group A #1 vs Group B #2 (or best second place)
                var semi1 = new Tournament.Match
                {
                    Name = "Semifinal 1",
                    Type = MatchType.Semifinal,
                    DisplayPosition = "Semifinal 1",
                    BestOf = 3,
                    Participants = new List<Tournament.MatchParticipant>()
                };

                // Group A #1
                var groupA1 = groupA.Participants?.OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GamesWon)
                    .FirstOrDefault();

                if (groupA1 != null)
                {
                    semi1.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = groupA1.Player,
                        SourceGroup = groupA,
                        SourceGroupPosition = 1
                    });
                }

                // Group B #2 or best second place
                if (tournament.Groups.Count == 2)
                {
                    var groupB2 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .Skip(1)
                        .FirstOrDefault();

                    if (groupB2 != null)
                    {
                        semi1.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB2.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 2
                        });
                    }
                }
                else if (bestSecondPlace != null)
                {
                    // Find the group this participant belongs to
                    var sourceGroup = tournament.Groups.FirstOrDefault(g => g.Participants?.Contains(bestSecondPlace) == true);

                    semi1.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = bestSecondPlace.Player,
                        SourceGroup = sourceGroup,
                        SourceGroupPosition = 2
                    });
                }

                tournament.PlayoffMatches.Add(semi1);

                // Create semifinal 2: Group B #1 vs Group A #2 (for 2 groups)
                if (tournament.Groups.Count == 2)
                {
                    var semi2 = new Tournament.Match
                    {
                        Name = "Semifinal 2",
                        Type = MatchType.Semifinal,
                        DisplayPosition = "Semifinal 2",
                        BestOf = 3,
                        Participants = new List<Tournament.MatchParticipant>()
                    };

                    // Group B #1
                    var groupB1 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupB1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB1.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 1
                        });
                    }

                    // Group A #2
                    var groupA2 = groupA.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .Skip(1)
                        .FirstOrDefault();

                    if (groupA2 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupA2.Player,
                            SourceGroup = groupA,
                            SourceGroupPosition = 2
                        });
                    }

                    tournament.PlayoffMatches.Add(semi2);
                }
                else if (tournament.Groups.Count >= 3)
                {
                    var groupC = tournament.Groups[2];

                    // In 3 groups case, top from Group B vs top from Group C
                    var semi2 = new Tournament.Match
                    {
                        Name = "Semifinal 2",
                        Type = MatchType.Semifinal,
                        DisplayPosition = "Semifinal 2",
                        BestOf = 3,
                        Participants = new List<Tournament.MatchParticipant>()
                    };

                    // Group B #1
                    var groupB1 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupB1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB1.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 1
                        });
                    }

                    // Group C #1
                    var groupC1 = groupC.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupC1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupC1.Player,
                            SourceGroup = groupC,
                            SourceGroupPosition = 1
                        });
                    }

                    tournament.PlayoffMatches.Add(semi2);
                }

                // Create final match
                var final = new Tournament.Match
                {
                    Name = "Final",
                    Type = MatchType.Final,
                    DisplayPosition = "Final",
                    BestOf = 3,
                    Participants = new List<Tournament.MatchParticipant>()
                };

                // Link semifinals to finals
                if (tournament.PlayoffMatches.Count >= 2)
                {
                    tournament.PlayoffMatches[0].NextMatch = final;
                    tournament.PlayoffMatches[1].NextMatch = final;
                }

                tournament.PlayoffMatches.Add(final);
            }
        }

        public Task StartMatchRound(Tournament tournament, Tournament.Match match, DiscordChannel channel)
        {
            // Early return if any parameter is null
            if (tournament is null || match is null || channel is null)
                return Task.CompletedTask;

            // Check that we have exactly 2 participants with valid players
            if (match.Participants?.Count != 2)
                return Task.CompletedTask;

            // Safe access to player objects
            var player1 = match.Participants[0]?.Player;
            var player2 = match.Participants[1]?.Player;

            if (player1 is null || player2 is null)
                return Task.CompletedTask;

            var round = new Round
            {
                Name = match.Name,
                Length = match.BestOf,
                OneVOne = true,
                Teams = new List<Round.Team>(),
                Pings = $"{GetPlayerMention(player1)} {GetPlayerMention(player2)}"
            };

            // Create teams (in this case individual players)
            foreach (var participant in match.Participants)
            {
                if (participant is not null && participant.Player is not null)
                {
                    var team = new Round.Team { Name = GetPlayerDisplayName(participant.Player) };
                    var discordMember = ConvertToDiscordMember(participant.Player);
                    if (discordMember is not null)
                    {
                        var roundParticipant = new Round.Participant { Player = discordMember };
                        team.Participants.Add(roundParticipant);
                    }
                    else
                    {
                        // Fallback for non-DiscordMember players in test scenarios
                        Console.WriteLine("[DEBUG] Skipping non-DiscordMember player in round creation");
                    }
                    round.Teams.Add(team);
                }
            }

            match.LinkedRound = round;
            _ongoingRounds.TourneyRounds.Add(round);

            // Return completed task since no async operations are performed
            return Task.CompletedTask;
        }

        public void UpdateTournamentFromRound(Tournament tournament)
        {
            // Look for completed rounds that are linked to tournament matches
            if (tournament.PlayoffMatches == null)
                return;

            foreach (var playoffMatch in tournament.PlayoffMatches)
            {
                if (playoffMatch?.LinkedRound == null || playoffMatch.IsComplete)
                    continue;

                var round = playoffMatch.LinkedRound;

                if (round.Teams == null || round.Teams.Count < 2)
                    continue;

                // Check if we have a winner
                var winningTeam = round.Teams.FirstOrDefault(t => t.Wins > round.Length / 2);
                if (winningTeam is null)
                    continue;

                var losingTeam = round.Teams.FirstOrDefault(t => !ReferenceEquals(t, winningTeam));

                // Get the winner member
                var winner = winningTeam.Participants?.FirstOrDefault()?.Player;
                if (winner is null)
                    continue;

                // Store ID once
                ulong playoffWinnerId = winner.Id;

                // Update match result    
                UpdateMatchResult(
                    tournament,
                    playoffMatch,
                    winner,
                    winningTeam.Wins,
                    losingTeam?.Wins ?? 0);

                // If this match has a next match (e.g., semifinal -> final)
                if (playoffMatch.NextMatch != null && playoffMatch.Participants != null)
                {
                    var nextMatch = playoffMatch.NextMatch;

                    // Find the winner's participant without using complex comparisons
                    Tournament.MatchParticipant? winnerParticipant = null;
                    foreach (var p in playoffMatch.Participants)
                    {
                        if (p is null || p.Player is null)
                            continue;

                        var playerIdValue = GetPlayerId(p.Player);
                        if (playerIdValue.HasValue && playerIdValue.Value == playoffWinnerId)
                        {
                            winnerParticipant = p;
                            break;
                        }
                    }

                    if (winnerParticipant != null && nextMatch.Participants != null)
                    {
                        nextMatch.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = winner,
                            SourceGroup = winnerParticipant.SourceGroup,
                            SourceGroupPosition = winnerParticipant.SourceGroupPosition
                        });
                    }
                }
            }
        }
    }
}