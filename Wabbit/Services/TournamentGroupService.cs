using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
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
        private readonly ITournamentMatchOperationsService _matchOperations;
        private readonly ITournamentScoreManager _scoreManager;
        private readonly ITournamentStateValidator _stateValidator;

        public TournamentGroupService(
            IRandomProvider randomProvider,
            ILogger<TournamentGroupService> logger,
            ITournamentMatchOperationsService matchOperations,
            ITournamentScoreManager scoreManager,
            ITournamentStateValidator stateValidator)
        {
            _randomProvider = randomProvider;
            _logger = logger;
            _matchOperations = matchOperations;
            _scoreManager = scoreManager;
            _stateValidator = stateValidator;
        }

        /// <summary>
        /// Determines the number of groups based on player count and tournament format
        /// </summary>
        public int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // Format-specific group count determination
            switch (format)
            {
                case TournamentFormat.GroupStageWithPlayoffs:
                    return playerCount switch
                    {
                        <= 7 => 1,  // 1 group of 7
                        8 => 2,     // 2 groups of 4
                        9 => 3,     // 3 groups of 3
                        10 => 2,    // 2 groups of 5
                        11 => 3,    // 3 groups (4,4,3)
                        12 => 3,    // 3 groups of 4
                        13 => 3,    // 3 groups (4,4,5)
                        14 => 2,    // 2 groups of 7
                        15 => 3,    // 3 groups of 5
                        16 => 4,    // 4 groups of 4
                        17 => 3,    // 3 groups (6,6,5)
                        18 => 3,    // 3 groups of 6
                        19 => 4,    // 4 groups (5,5,5,4)
                        20 => 4,    // 4 groups of 5
                        21 or 22 or 23 => 6,  // 6 groups (4,4,4,3,3,3 for 21)
                        24 => 6,    // 6 groups of 4
                        25 or 26 or 27 => 6,  // 6 groups (5,5,5,4,3,3 for 25)
                        28 or 29 or 30 => 6,  // 6 groups of 5
                        31 or 32 => 8,        // 8 groups of 4
                        _ => (int)Math.Ceiling(playerCount / 4.0) // Default to ~4 players per group
                    };

                case TournamentFormat.RoundRobin:
                    return 1; // Single group for round robin

                case TournamentFormat.SingleElimination:
                case TournamentFormat.DoubleElimination:
                    return 0; // No groups for elimination formats

                default:
                    return 0;
            }
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
            tournament.Groups ??= new List<Tournament.Group>();
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
                // Ensure groups are initialized
                tournament.Groups ??= new List<Tournament.Group>();

                // Add participant to group
                tournament.Groups[groupIndex] ??= new Tournament.Group();
                tournament.Groups[groupIndex].Participants ??= new List<Tournament.GroupParticipant>();
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
            tournament.Groups ??= new List<Tournament.Group>();
            int playerIndex = 0;
            for (int i = 0; i < tournament.Groups.Count; i++)
            {
                tournament.Groups[i] ??= new Tournament.Group();
                tournament.Groups[i].Participants ??= new List<Tournament.GroupParticipant>();
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
            List<Tournament.GroupParticipant> participants = group.Participants;

            // Clear existing matches
            group.Matches.Clear();

            if (participants.Count < 2)
            {
                return; // Can't create matches with less than 2 players
            }

            // Create a match between each pair of participants
            for (int i = 0; i < participants.Count; i++)
            {
                for (int j = i + 1; j < participants.Count; j++)
                {
                    var player1 = participants[i];
                    var player2 = participants[j];

                    // Determine match format based on tournament stage and format
                    int bestOf = tournament.CurrentStage switch
                    {
                        TournamentStage.Groups => 1, // Group stage is Bo1
                        TournamentStage.Playoffs => 3, // Playoff matches are Bo3
                        _ => 1
                    };

                    var match = _matchOperations.CreateMatch(
                        $"{GetPlayerDisplayName(player1.Player)} vs {GetPlayerDisplayName(player2.Player)}",
                        TournamentMatchType.GroupStage,
                        bestOf,
                        ConvertToDiscordMember(player1.Player)!,
                        ConvertToDiscordMember(player2.Player)!,
                        group);

                    group.Matches.Add(match);
                }
            }
        }

        /// <summary>
        /// Checks if a group is complete and updates its status
        /// </summary>
        public void CheckGroupCompletion(Tournament.Group group)
        {
            if (group == null)
            {
                _logger.LogWarning("Cannot check completion of null group");
                return;
            }

            // Use ScoreManager to check if group is complete
            group.IsComplete = _scoreManager.IsGroupComplete(group);

            if (group.IsComplete)
            {
                _logger.LogInformation($"Group {group.Name} is now complete");
            }
        }

        /// <summary>
        /// Creates tiebreaker matches for a group if needed
        /// </summary>
        private Tournament.Match CreateTiebreakerMatch(
            Tournament tournament,
            Tournament.Group group,
            Tournament.GroupParticipant participant1,
            Tournament.GroupParticipant participant2)
        {
            var player1 = ConvertToDiscordMember(participant1.Player);
            var player2 = ConvertToDiscordMember(participant2.Player);

            if (player1 is null || player2 is null)
            {
                throw new ArgumentException("Invalid participants for tiebreaker match");
            }

            var match = new Tournament.Match
            {
                Name = $"Tiebreaker: {GetPlayerDisplayName(player1)} vs {GetPlayerDisplayName(player2)}",
                Type = TournamentMatchType.GroupStageTiebreaker,
                BestOf = 3, // Tiebreakers are best of 3
                Participants = new List<Tournament.MatchParticipant>
                {
                    new Tournament.MatchParticipant { Player = player1, SourceGroup = group },
                    new Tournament.MatchParticipant { Player = player2, SourceGroup = group }
                }
            };

            return match;
        }

        /// <summary>
        /// Creates tiebreaker matches for tied participants
        /// </summary>
        private List<Tournament.Match> CreateTiebreakerMatches(
            Tournament tournament,
            Tournament.Group group,
            List<Tournament.GroupParticipant> tiedParticipants)
        {
            var tiebreakerMatches = new List<Tournament.Match>();

            // Get head-to-head records for all tied participants
            for (int i = 0; i < tiedParticipants.Count; i++)
            {
                for (int j = i + 1; j < tiedParticipants.Count; j++)
                {
                    var (wins, losses) = _scoreManager.GetHeadToHeadRecord(group, tiedParticipants[i], tiedParticipants[j]);

                    // If no clear head-to-head winner, create a tiebreaker match
                    if (wins == losses)
                    {
                        var match = CreateTiebreakerMatch(tournament, group, tiedParticipants[i], tiedParticipants[j]);
                        tiebreakerMatches.Add(match);
                    }
                }
            }

            return tiebreakerMatches;
        }

        /// <summary>
        /// Checks if a group needs tiebreaker matches
        /// </summary>
        private bool CheckForTiebreaker(List<Tournament.GroupParticipant> standings, Tournament.Group group)
        {
            // Get qualifying positions based on tournament settings
            int qualifyingPositions = 2; // Default to top 2

            // Check for ties in qualifying positions
            for (int position = 0; position < qualifyingPositions; position++)
            {
                var tiedParticipants = _scoreManager.CheckForTie(group, position);
                if (tiedParticipants.Count > 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the standings for a group
        /// </summary>
        public List<Tournament.GroupParticipant> GetGroupStandings(Tournament.Group group)
        {
            return _scoreManager.GetGroupStandings(group);
        }

        /// <summary>
        /// Gets optimal group sizes for distribution of players
        /// </summary>
        public List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
        {
            if (groupCount == 0) return new List<int>();
            if (groupCount == 1) return new List<int> { playerCount };

            var sizes = new List<int>();

            // Special cases based on the format document
            (int[] distribution, bool found) = playerCount switch
            {
                8 when groupCount == 2 => (new[] { 4, 4 }, true),
                9 when groupCount == 3 => (new[] { 3, 3, 3 }, true),
                10 when groupCount == 2 => (new[] { 5, 5 }, true),
                11 when groupCount == 3 => (new[] { 4, 4, 3 }, true),
                12 when groupCount == 3 => (new[] { 4, 4, 4 }, true),
                13 when groupCount == 3 => (new[] { 4, 4, 5 }, true),
                14 when groupCount == 2 => (new[] { 7, 7 }, true),
                15 when groupCount == 3 => (new[] { 5, 5, 5 }, true),
                16 when groupCount == 4 => (new[] { 4, 4, 4, 4 }, true),
                17 when groupCount == 3 => (new[] { 6, 6, 5 }, true),
                18 when groupCount == 3 => (new[] { 6, 6, 6 }, true),
                19 when groupCount == 4 => (new[] { 5, 5, 5, 4 }, true),
                20 when groupCount == 4 => (new[] { 5, 5, 5, 5 }, true),
                21 when groupCount == 6 => (new[] { 4, 4, 4, 3, 3, 3 }, true),
                24 when groupCount == 6 => (new[] { 4, 4, 4, 4, 4, 4 }, true),
                25 when groupCount == 6 => (new[] { 5, 5, 5, 4, 3, 3 }, true),
                28 when groupCount == 6 => (new[] { 5, 5, 5, 5, 4, 4 }, true),
                30 when groupCount == 6 => (new[] { 5, 5, 5, 5, 5, 5 }, true),
                32 when groupCount == 8 => (new[] { 4, 4, 4, 4, 4, 4, 4, 4 }, true),
                _ => (Array.Empty<int>(), false)
            };

            if (found)
            {
                sizes.AddRange(distribution);
                return sizes;
            }

            // For cases not explicitly defined, distribute players as evenly as possible
            int baseSize = playerCount / groupCount;
            int remainder = playerCount % groupCount;

            // Add base size to all groups
            for (int i = 0; i < groupCount; i++)
            {
                sizes.Add(baseSize);
            }

            // Distribute remainder, prioritizing earlier groups
            for (int i = 0; i < remainder; i++)
            {
                sizes[i]++;
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

        /// <summary>
        /// Gets advancement criteria for playoff stage based on player and group count
        /// </summary>
        private (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
        {
            return (playerCount, groupCount) switch
            {
                // Single group formats
                (7, 1) => (4, 0),  // Top 4 advance to playoffs

                // Two group formats
                (8, 2) => (2, 0),  // Top 2 from each group
                (10, 2) => (2, 0), // Top 2 from each group
                (14, 2) => (4, 0), // Top 4 from each group

                // Three group formats
                (9, 3) => (2, 2),   // Top 2 + best 2 third-place
                (11, 3) => (2, 2),  // Top 2 + best 2 third-place
                (12, 3) => (2, 2),  // Top 2 + best 2 third-place
                (13, 3) => (2, 2),  // Top 2 + best 2 third-place
                (15, 3) => (2, 2),  // Top 2 + best 2 third-place
                (17, 3) => (2, 2),  // Top 2 + best 2 third-place
                (18, 3) => (2, 2),  // Top 2 + best 2 third-place

                // Four group formats
                (16, 4) => (2, 0),  // Top 2 from each group
                (19, 4) => (2, 0),  // Top 2 from each group
                (20, 4) => (2, 0),  // Top 2 from each group

                // Six group formats (21-30 players)
                ( >= 21 and <= 30, 6) => (2, 4),  // Top 2 + best 4 third-place

                // Eight group formats (31-32 players)
                ( >= 31 and <= 32, 8) => (2, 0),  // Top 2 from each group

                // Default case - use standard criteria
                _ => (2, 0)  // Default to top 2 from each group
            };
        }

        public async Task CreateGroupMatchesAsync(Tournament tournament, Tournament.Group group)
        {
            try
            {
                if (!_stateValidator.ValidateGroupMatchCreation(tournament, group))
                {
                    _logger.LogError($"Cannot create matches for group {group.Name}: validation failed");
                    return;
                }

                if (group.Participants == null || group.Participants.Count < 2)
                {
                    _logger.LogError($"Cannot create matches for group {group.Name}: insufficient participants");
                    return;
                }

                group.Matches ??= new List<Tournament.Match>();

                // Create round-robin matches
                for (int i = 0; i < group.Participants.Count; i++)
                {
                    for (int j = i + 1; j < group.Participants.Count; j++)
                    {
                        var player1 = group.Participants[i]?.Player as DiscordMember;
                        var player2 = group.Participants[j]?.Player as DiscordMember;

                        if (player1 is null || player2 is null) continue;

                        var match = _matchOperations.CreateMatch(
                            $"{player1.DisplayName} vs {player2.DisplayName}",
                            TournamentMatchType.GroupStage,
                            tournament.DefaultMatchLength,
                            player1,
                            player2,
                            group);

                        group.Matches.Add(match);
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating matches for group {group.Name}");
                throw;
            }
        }

        public async Task HandleGroupCompletionAsync(Tournament tournament, Tournament.Group group)
        {
            try
            {
                if (!_stateValidator.ValidateGroupCompletion(tournament, group))
                {
                    _logger.LogError($"Cannot handle completion for group {group.Name}: validation failed");
                    return;
                }

                // Sort participants by points and game differential
                SortGroupParticipants(group);

                // Check for ties and create tiebreaker matches if needed
                await CreateTiebreakerMatchesIfNeededAsync(tournament, group);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling completion for group {group.Name}");
                throw;
            }
        }

        private void SortGroupParticipants(Tournament.Group group)
        {
            if (group.Participants == null) return;

            group.Participants = group.Participants
                .OrderByDescending(p => p?.Points)
                .ThenByDescending(p => p?.GamesWon - p?.GamesLost)
                .ThenByDescending(p => p?.GamesWon)
                .ToList();

            // Update positions
            for (int i = 0; i < group.Participants.Count; i++)
            {
                if (group.Participants[i] != null)
                {
                    group.Participants[i].Position = i + 1;
                }
            }
        }

        private async Task CreateTiebreakerMatchesIfNeededAsync(Tournament tournament, Tournament.Group group)
        {
            if (group.Participants == null || group.Participants.Count < 2) return;

            // Find tied participants in qualifying positions (top 2)
            var tiedGroups = group.Participants
                .Where(p => p?.Position <= 2)
                .GroupBy(p => new { p?.Points, GameDiff = p?.GamesWon - p?.GamesLost })
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var tiedGroup in tiedGroups)
            {
                var tiedParticipants = tiedGroup.ToList();
                for (int i = 0; i < tiedParticipants.Count; i++)
                {
                    for (int j = i + 1; j < tiedParticipants.Count; j++)
                    {
                        var player1 = tiedParticipants[i]?.Player as DiscordMember;
                        var player2 = tiedParticipants[j]?.Player as DiscordMember;

                        if (player1 is null || player2 is null) continue;

                        var match = _matchOperations.CreateMatch(
                            $"Tiebreaker: {player1.DisplayName} vs {player2.DisplayName}",
                            TournamentMatchType.GroupStageTiebreaker,
                            tournament.DefaultMatchLength,
                            player1,
                            player2,
                            group);

                        match.IsTiebreakerMatch = true;
                        group.Matches?.Add(match);
                    }
                }
            }

            await Task.CompletedTask;
        }
    }
}