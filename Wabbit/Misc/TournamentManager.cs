using DSharpPlus.Entities;
using Wabbit.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MatchType = Wabbit.Models.MatchType;

namespace Wabbit.Misc
{
    public class TournamentManager
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly string _dataDirectory;
        private readonly string _tournamentsFilePath;
        private readonly string _signupsFilePath;

        // Constructor
        public TournamentManager(OngoingRounds ongoingRounds)
        {
            _ongoingRounds = ongoingRounds;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _tournamentsFilePath = Path.Combine(_dataDirectory, "tournaments.json");
            _signupsFilePath = Path.Combine(_dataDirectory, "signups.json");

            // Initialize collections if needed
            if (_ongoingRounds.Tournaments == null)
            {
                _ongoingRounds.Tournaments = new List<Tournament>();
            }

            if (_ongoingRounds.TournamentSignups == null)
            {
                _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
            }

            // Load data from files
            LoadTournamentsFromFile();
            LoadSignupsFromFile();
        }

        // Load tournaments from file
        private void LoadTournamentsFromFile()
        {
            try
            {
                if (File.Exists(_tournamentsFilePath))
                {
                    string json = File.ReadAllText(_tournamentsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        ReferenceHandler = ReferenceHandler.Preserve
                    };
                    var tournaments = JsonSerializer.Deserialize<List<Tournament>>(json, options);

                    if (tournaments != null)
                    {
                        Console.WriteLine($"Loaded {tournaments.Count} tournaments from file");
                        _ongoingRounds.Tournaments = tournaments;
                    }
                }
                else
                {
                    Console.WriteLine("Tournaments file does not exist, creating a new one");
                    SaveTournamentsToFile();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading tournaments from file: {ex.Message}");
                // If we can't load, create a new empty collection
                _ongoingRounds.Tournaments = new List<Tournament>();
                SaveTournamentsToFile();
            }
        }

        // Load signups from file
        private void LoadSignupsFromFile()
        {
            try
            {
                if (File.Exists(_signupsFilePath))
                {
                    string json = File.ReadAllText(_signupsFilePath);

                    // Try to load signups, handling possibility of different formats
                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            ReferenceHandler = ReferenceHandler.Preserve
                        };

                        // Try to deserialize as anonymous type first (new format)
                        using JsonDocument doc = JsonDocument.Parse(json);

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            List<TournamentSignup> signups = new();

                            foreach (JsonElement element in doc.RootElement.EnumerateArray())
                            {
                                var signup = new TournamentSignup
                                {
                                    Name = element.GetProperty("Name").GetString() ?? "",
                                    IsOpen = element.TryGetProperty("IsOpen", out var isOpen) && isOpen.GetBoolean(),
                                    Format = element.TryGetProperty("Format", out var format) ?
                                        Enum.Parse<TournamentFormat>(format.GetString() ?? "GroupStageWithPlayoffs") :
                                        TournamentFormat.GroupStageWithPlayoffs
                                };

                                if (element.TryGetProperty("CreatedAt", out var createdAt))
                                {
                                    signup.CreatedAt = createdAt.GetDateTime();
                                }

                                if (element.TryGetProperty("ScheduledStartTime", out var startTime) && startTime.ValueKind != JsonValueKind.Null)
                                {
                                    signup.ScheduledStartTime = startTime.GetDateTime();
                                }

                                if (element.TryGetProperty("SignupChannelId", out var channelId))
                                {
                                    signup.SignupChannelId = channelId.GetUInt64();
                                }

                                if (element.TryGetProperty("MessageId", out var messageId))
                                {
                                    signup.MessageId = messageId.GetUInt64();
                                }

                                // Participants will need to be reloaded when accessed

                                signups.Add(signup);
                            }

                            Console.WriteLine($"Loaded {signups.Count} signups from file (simplified format)");
                            _ongoingRounds.TournamentSignups = signups;
                        }
                        else
                        {
                            // Try original format as fallback
                            var signups = JsonSerializer.Deserialize<List<TournamentSignup>>(json, options);
                            if (signups != null)
                            {
                                Console.WriteLine($"Loaded {signups.Count} signups from file (original format)");
                                _ongoingRounds.TournamentSignups = signups;
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Console.WriteLine($"Error parsing signups file: {parseEx.Message}");
                        throw; // Rethrow to outer handler
                    }
                }
                else
                {
                    Console.WriteLine("Signups file does not exist, creating a new one");
                    SaveSignupsToFile();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading signups from file: {ex.Message}");
                // If we can't load, create a new empty collection
                _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
                SaveSignupsToFile();
            }
        }

        // Save tournaments to file
        private void SaveTournamentsToFile()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve
                };

                string json = JsonSerializer.Serialize(_ongoingRounds.Tournaments, options);
                File.WriteAllText(_tournamentsFilePath, json);
                Console.WriteLine($"Saved {_ongoingRounds.Tournaments.Count} tournaments to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tournaments to file: {ex.Message}");
            }
        }

        // Save signups to file
        private void SaveSignupsToFile()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                // Create a copy with simplified data to avoid serialization issues
                var signupsToSave = _ongoingRounds.TournamentSignups.Select(signup => new
                {
                    signup.Name,
                    signup.IsOpen,
                    signup.CreatedAt,
                    signup.Format,
                    signup.ScheduledStartTime,
                    signup.SignupChannelId,
                    signup.MessageId,
                    CreatedById = signup.CreatedBy?.Id ?? 0,
                    CreatedByUsername = signup.CreatedBy?.Username ?? "Unknown",
                    Participants = signup.Participants.Select(p => new
                    {
                        Id = p.Id,
                        Username = p.Username
                    }).ToList()
                }).ToList();

                string json = JsonSerializer.Serialize(signupsToSave, options);
                File.WriteAllText(_signupsFilePath, json);
                Console.WriteLine($"Saved {_ongoingRounds.TournamentSignups.Count} signups to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving signups to file: {ex.Message}");
            }
        }

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

        public Tournament CreateTournament(string name, List<DiscordMember> players, TournamentFormat format, DiscordChannel announcementChannel)
        {
            // Create the tournament
            var tournament = new Tournament
            {
                Name = name,
                Format = format,
                AnnouncementChannel = announcementChannel
            };

            // Setup groups and players
            CreateGroups(tournament, players, DetermineGroupCount(players.Count, format));

            // Add to ongoingRounds collection
            _ongoingRounds.Tournaments.Add(tournament);

            // Save to file
            SaveTournamentsToFile();

            Console.WriteLine($"Created tournament '{name}' with {players.Count} players");

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
            return _ongoingRounds.Tournaments.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public List<Tournament> GetAllTournaments()
        {
            return _ongoingRounds.Tournaments;
        }

        public void DeleteTournament(string name)
        {
            Console.WriteLine($"Deleting tournament '{name}'");
            int countBefore = _ongoingRounds.Tournaments.Count;

            // Remove from collection
            _ongoingRounds.Tournaments.RemoveAll(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            int countAfter = _ongoingRounds.Tournaments.Count;
            Console.WriteLine($"Removed {countBefore - countAfter} tournaments");

            // Save changes to file
            SaveTournamentsToFile();
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

            // Save changes to file after updating match result
            SaveTournamentsToFile();
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

        private int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // Determine appropriate group count based on player count and format
            if (format != TournamentFormat.GroupStageWithPlayoffs)
            {
                return 1; // Single group for non-group formats
            }

            switch (playerCount)
            {
                case <= 8:
                    // Create 2 groups of 4 or less
                    return 2;
                case 9:
                    // Create 3 groups of 3
                    return 3;
                case <= 10:
                    // Create 2 groups of 5
                    return 2;
                default:
                    // For 11+ players, adjust as needed
                    return (playerCount / 4) + (playerCount % 4 > 0 ? 1 : 0);
            }
        }

        // Add a tournament signup management method
        public TournamentSignup CreateSignup(string name, TournamentFormat format, DiscordUser creator, ulong signupChannelId, DateTime? scheduledStartTime = null)
        {
            var signup = new TournamentSignup
            {
                Name = name,
                Format = format,
                CreatedAt = DateTime.Now,
                CreatedBy = creator,
                SignupChannelId = signupChannelId,
                ScheduledStartTime = scheduledStartTime,
                IsOpen = true
            };

            _ongoingRounds.TournamentSignups.Add(signup);
            SaveSignupsToFile();

            Console.WriteLine($"Created tournament signup '{name}'");
            return signup;
        }

        // Methods for managing signups
        public TournamentSignup? GetSignup(string name)
        {
            return _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public List<TournamentSignup> GetAllSignups()
        {
            return _ongoingRounds.TournamentSignups;
        }

        public void DeleteSignup(string name)
        {
            Console.WriteLine($"Deleting signup '{name}'");
            int countBefore = _ongoingRounds.TournamentSignups.Count;

            _ongoingRounds.TournamentSignups.RemoveAll(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            int countAfter = _ongoingRounds.TournamentSignups.Count;
            Console.WriteLine($"Removed {countBefore - countAfter} signups");

            SaveSignupsToFile();
        }

        public void UpdateSignup(TournamentSignup signup)
        {
            // Just save the current state of signups
            SaveSignupsToFile();
        }

        // Save both data files whenever there's a significant change
        public void SaveAllData()
        {
            SaveTournamentsToFile();
            SaveSignupsToFile();
        }
    }
}