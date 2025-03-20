using System.Collections.Generic;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Test-specific adapter for TournamentGroupService that adds methods needed for testing
    /// </summary>
    public class TestTournamentGroupService : ITournamentGroupService
    {
        private readonly TournamentGroupService _groupService;
        private readonly ILogger<TournamentGroupService> _logger;

        public TestTournamentGroupService(
            IRandomProvider randomProvider,
            ILogger<TournamentGroupService> logger,
            ITournamentMatchOperationsService matchOperations,
            ITournamentScoreManager scoreManager,
            ITournamentStateValidator stateValidator)
        {
            _logger = logger;
            _groupService = new TournamentGroupService(
                randomProvider,
                logger,
                matchOperations,
                scoreManager,
                stateValidator);
        }

        /// <summary>
        /// Creates groups for a tournament
        /// </summary>
        public void CreateGroups(Tournament tournament, List<DiscordMember> players, Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            _groupService.CreateGroups(tournament, players, playerSeeds);
        }

        /// <summary>
        /// Checks if a group is complete
        /// </summary>
        public void CheckGroupCompletion(Tournament.Group group)
        {
            _groupService.CheckGroupCompletion(group);
        }

        /// <summary>
        /// Determines the appropriate group count based on player count and format
        /// </summary>
        public int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            return _groupService.DetermineGroupCount(playerCount, format);
        }

        /// <summary>
        /// Gets optimal group sizes for distribution of players
        /// </summary>
        public List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
        {
            return _groupService.GetOptimalGroupSizes(playerCount, groupCount);
        }

        /// <summary>
        /// Gets player display name
        /// </summary>
        public string GetPlayerDisplayName(object? player)
        {
            return _groupService.GetPlayerDisplayName(player);
        }

        /// <summary>
        /// Gets player ID
        /// </summary>
        public ulong? GetPlayerId(object? player)
        {
            return _groupService.GetPlayerId(player);
        }

        /// <summary>
        /// Gets player mention
        /// </summary>
        public string GetPlayerMention(object? player)
        {
            return _groupService.GetPlayerMention(player);
        }

        /// <summary>
        /// Compares player IDs
        /// </summary>
        public bool ComparePlayerIds(object? player1, object? player2)
        {
            return _groupService.ComparePlayerIds(player1, player2);
        }

        /// <summary>
        /// Converts to DiscordMember
        /// </summary>
        public DiscordMember? ConvertToDiscordMember(object? player)
        {
            return _groupService.ConvertToDiscordMember(player);
        }

        /// <summary>
        /// Ensures all participants in the tournament are properly converted to DiscordMember objects
        /// </summary>
        public void EnsureParticipantsAreDiscordMembers(Tournament tournament, DiscordClient client)
        {
            _groupService.EnsureParticipantsAreDiscordMembers(tournament, client);
        }

        /// <summary>
        /// Gets a list of participants who should advance to playoffs based on group standings
        /// </summary>
        public List<Tournament.GroupParticipant> GetAdvancingParticipants(Tournament.Group group, int count)
        {
            // Get participants sorted by points
            var sortedParticipants = new List<Tournament.GroupParticipant>(group.Participants);
            sortedParticipants.Sort((a, b) => b.Points.CompareTo(a.Points));

            // Take the top 'count' participants
            return sortedParticipants.Take(count).ToList();
        }

        /// <summary>
        /// Checks if all matches in a group are completed
        /// </summary>
        public bool IsGroupStageComplete(Tournament.Group group)
        {
            if (group?.Matches == null || group.Matches.Count == 0)
                return false;

            return group.Matches.All(m => m.IsComplete);
        }

        /// <summary>
        /// Creates groups based on the provided list of participants
        /// This is the adapter method that maps to what the tests expect
        /// </summary>
        public List<Tournament.Group> CreateGroups(List<ParticipantInfo> participants)
        {
            // Create a new tournament for these participants
            var tournament = new Tournament
            {
                Name = "TestTournament",
                Format = TournamentFormat.GroupStageWithPlayoffs
            };

            // Convert ParticipantInfo to DiscordMember
            var players = new List<DiscordMember>();
            var playerSeeds = new Dictionary<DiscordMember, int>();

            foreach (var participant in participants)
            {
                var mockMember = new Moq.Mock<DiscordMember>();
                mockMember.Setup(m => m.Id).Returns(participant.Id);
                mockMember.Setup(m => m.Username).Returns(participant.Username);
                var member = mockMember.Object;

                players.Add(member);

                if (participant.Seed.HasValue && participant.Seed.Value > 0)
                {
                    playerSeeds[member] = participant.Seed.Value;
                }
            }

            // Create the groups
            _groupService.CreateGroups(tournament, players, playerSeeds.Count > 0 ? playerSeeds : null);

            return tournament.Groups?.ToList() ?? new List<Tournament.Group>();
        }
    }
}