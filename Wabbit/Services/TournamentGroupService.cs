using System;
using System.Collections.Generic;
using System.Linq;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament group management and player operations
    /// </summary>
    public class TournamentGroupService : ITournamentGroupService
    {
        private readonly ILogger<TournamentGroupService> _logger;
        private readonly IRandomProvider _randomProvider;

        public TournamentGroupService(
            IRandomProvider randomProvider,
            ILogger<TournamentGroupService> logger)
        {
            _randomProvider = randomProvider;
            _logger = logger;
        }

        /// <summary>
        /// Creates groups for a tournament
        /// </summary>
        public void CreateGroups(
            Tournament tournament,
            List<DiscordMember> players,
            Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            if (tournament == null) throw new ArgumentNullException(nameof(tournament));
            if (players == null) throw new ArgumentNullException(nameof(players));

            _logger.LogInformation($"Creating groups for tournament {tournament.Name} with {players.Count} players");

            int playerCount = players.Count;
            int groupCount = DetermineGroupCount(playerCount, tournament.Format);

            if (groupCount == 0)
            {
                _logger.LogWarning($"No groups determined for player count {playerCount} and format {tournament.Format}");
                return;
            }

            // Calculate optimal group sizes
            List<int> groupSizes = GetOptimalGroupSizes(playerCount, groupCount);

            _logger.LogInformation($"Creating {groupCount} groups with sizes: {string.Join(", ", groupSizes)}");

            // Create the groups
            tournament.Groups = new List<Tournament.Group>();
            for (int i = 0; i < groupCount; i++)
            {
                var group = new Tournament.Group
                {
                    Name = $"Group {(char)('A' + i)}",
                    Participants = new List<Tournament.GroupParticipant>(),
                    Matches = new List<Tournament.Match>()
                };
                tournament.Groups.Add(group);
            }

            // Distribute players based on seeding (if available) or randomly
            if (playerSeeds != null && playerSeeds.Count > 0)
            {
                DistributePlayersWithSeeding(tournament, players, playerSeeds, groupSizes);
            }
            else
            {
                DistributePlayersRandomly(tournament, players, groupSizes);
            }

            // Generate matches for each group
            foreach (var group in tournament.Groups)
            {
                GenerateGroupMatches(tournament, group);
            }
        }

        /// <summary>
        /// Distributes players with seeding
        /// </summary>
        private void DistributePlayersWithSeeding(
            Tournament tournament,
            List<DiscordMember> players,
            Dictionary<DiscordMember, int> playerSeeds,
            List<int> groupSizes)
        {
            // Sort players by seed
            var seededPlayers = players.OrderBy(p =>
                playerSeeds.ContainsKey(p) ? playerSeeds[p] : int.MaxValue).ToList();

            // Distribute seeded players in snake draft order to balance groups
            int groupIndex = 0;
            bool increasing = true;

            foreach (var player in seededPlayers)
            {
                if (tournament.Groups[groupIndex].Participants.Count < groupSizes[groupIndex])
                {
                    tournament.Groups[groupIndex].Participants.Add(new Tournament.GroupParticipant
                    {
                        Player = player,
                        Seed = playerSeeds.ContainsKey(player) ? playerSeeds[player] : 0
                    });

                    // Move to next group in snake draft order
                    if (increasing)
                    {
                        groupIndex++;
                        if (groupIndex >= tournament.Groups.Count)
                        {
                            groupIndex = tournament.Groups.Count - 1;
                            increasing = false;
                        }
                    }
                    else
                    {
                        groupIndex--;
                        if (groupIndex < 0)
                        {
                            groupIndex = 0;
                            increasing = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Distributes players randomly
        /// </summary>
        private void DistributePlayersRandomly(
            Tournament tournament,
            List<DiscordMember> players,
            List<int> groupSizes)
        {
            // Shuffle players
            var shuffledPlayers = players.OrderBy(_ => _randomProvider.Instance.Next()).ToList();

            // Distribute to groups
            int playerIndex = 0;
            for (int i = 0; i < tournament.Groups.Count; i++)
            {
                for (int j = 0; j < groupSizes[i]; j++)
                {
                    if (playerIndex < shuffledPlayers.Count)
                    {
                        tournament.Groups[i].Participants.Add(new Tournament.GroupParticipant
                        {
                            Player = shuffledPlayers[playerIndex]
                        });
                        playerIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// Generates matches for a group
        /// </summary>
        private void GenerateGroupMatches(Tournament tournament, Tournament.Group group)
        {
            int matchesPerPlayer = tournament.MatchesPerPlayer;
            List<Tournament.GroupParticipant> participants = group.Participants;

            // Clear existing matches
            group.Matches.Clear();

            if (participants.Count < 2)
            {
                return; // Can't create matches with less than 2 players
            }

            // If Round Robin (matchesPerPlayer = 0), create all possible matches
            if (matchesPerPlayer == 0)
            {
                // Create a match between each pair of participants
                for (int i = 0; i < participants.Count; i++)
                {
                    for (int j = i + 1; j < participants.Count; j++)
                    {
                        var player1 = participants[i];
                        var player2 = participants[j];

                        var match = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(player1.Player)} vs {GetPlayerDisplayName(player2.Player)}",
                            Type = TournamentMatchType.GroupStage,
                            BestOf = 3,
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new Tournament.MatchParticipant { Player = player1.Player, SourceGroup = group },
                                new Tournament.MatchParticipant { Player = player2.Player, SourceGroup = group }
                            }
                        };

                        group.Matches.Add(match);
                    }
                }
            }
            else
            {
                // Create a specific number of matches per player - to be implemented
                // This would be more complex and could involve algorithms like Swiss system
                _logger.LogWarning($"Creating specific number of matches per player ({matchesPerPlayer}) is not implemented yet");
            }
        }

        /// <summary>
        /// Checks if a group is complete
        /// </summary>
        public void CheckGroupCompletion(Tournament.Group group)
        {
            if (group == null)
            {
                _logger.LogWarning("Cannot check completion for null group");
                return;
            }

            // A group is complete if all matches are complete
            bool allMatchesComplete = group.Matches.All(m => m.IsComplete);

            // Set the group as complete
            group.IsComplete = allMatchesComplete;

            if (group.IsComplete)
            {
                _logger.LogInformation($"Group {group.Name} is now complete");
            }
        }

        /// <summary>
        /// Determines the appropriate group count based on player count and format
        /// </summary>
        public int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // Format-specific group count determination
            switch (format)
            {
                case TournamentFormat.GroupStageWithPlayoffs:
                    // Typical group counts based on player count
                    if (playerCount <= 8) return 2;
                    if (playerCount <= 16) return 4;
                    if (playerCount <= 24) return 6;
                    if (playerCount <= 32) return 8;
                    return (int)Math.Ceiling(playerCount / 4.0); // Approximately 4 players per group

                case TournamentFormat.RoundRobin:
                    return 1; // Single group for round robin

                case TournamentFormat.SingleElimination:
                case TournamentFormat.DoubleElimination:
                    return 0; // No groups for elimination formats

                default:
                    return 2; // Default to 2 groups
            }
        }

        /// <summary>
        /// Gets optimal group sizes for distribution of players
        /// </summary>
        public List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
        {
            if (groupCount <= 0) return new List<int>();

            // Calculate base size and remainder
            int baseSize = playerCount / groupCount;
            int remainder = playerCount % groupCount;

            // Create list of group sizes
            var sizes = new List<int>();
            for (int i = 0; i < groupCount; i++)
            {
                // Add one extra player to some groups to distribute the remainder
                sizes.Add(baseSize + (i < remainder ? 1 : 0));
            }

            return sizes;
        }

        /// <summary>
        /// Gets player display name
        /// </summary>
        public string GetPlayerDisplayName(object? player)
        {
            if (player == null)
                return "Unknown";

            if (player is DiscordMember member)
                return member.DisplayName;

            if (player is DiscordUser user)
                return user.Username;

            // Check if it's serialized player data
            var type = player.GetType();
            if (type.GetProperty("Type") != null &&
                type.GetProperty("Username") != null)
            {
                var username = type.GetProperty("Username")?.GetValue(player)?.ToString();
                if (!string.IsNullOrEmpty(username))
                    return username;
            }

            return player.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Gets player ID
        /// </summary>
        public ulong? GetPlayerId(object? player)
        {
            if (player == null)
                return null;

            if (player is DiscordMember member)
                return member.Id;

            if (player is DiscordUser user)
                return user.Id;

            // Check if it's serialized player data
            var type = player.GetType();
            if (type.GetProperty("Type") != null &&
                type.GetProperty("Id") != null)
            {
                var idObj = type.GetProperty("Id")?.GetValue(player);
                if (idObj != null && ulong.TryParse(idObj.ToString(), out ulong id))
                    return id;
            }

            return null;
        }

        /// <summary>
        /// Gets player mention
        /// </summary>
        public string GetPlayerMention(object? player)
        {
            var id = GetPlayerId(player);
            if (id.HasValue)
                return $"<@{id.Value}>";

            return GetPlayerDisplayName(player);
        }

        /// <summary>
        /// Compares player IDs
        /// </summary>
        public bool ComparePlayerIds(object? player1, object? player2)
        {
            var id1 = GetPlayerId(player1);
            var id2 = GetPlayerId(player2);

            if (id1.HasValue && id2.HasValue)
                return id1.Value == id2.Value;

            return false;
        }

        /// <summary>
        /// Converts to DiscordMember
        /// </summary>
        public DiscordMember? ConvertToDiscordMember(object? player)
        {
            if (player == null)
                return null;

            if (player is DiscordMember member)
                return member;

            // Note: Cannot convert serialized player to DiscordMember without client
            // This would need to be done at a higher level with access to the client

            return null;
        }
    }
}