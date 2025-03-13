using System.Collections.Generic;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament group management
    /// </summary>
    public interface ITournamentGroupService
    {
        /// <summary>
        /// Creates groups for a tournament
        /// </summary>
        void CreateGroups(
            Tournament tournament,
            List<DiscordMember> players,
            Dictionary<DiscordMember, int>? playerSeeds = null);

        /// <summary>
        /// Checks if a group is complete
        /// </summary>
        void CheckGroupCompletion(Tournament.Group group);

        /// <summary>
        /// Determines the appropriate group count based on player count and format
        /// </summary>
        int DetermineGroupCount(int playerCount, TournamentFormat format);

        /// <summary>
        /// Gets optimal group sizes for distribution of players
        /// </summary>
        List<int> GetOptimalGroupSizes(int playerCount, int groupCount);

        /// <summary>
        /// Gets player display name
        /// </summary>
        string GetPlayerDisplayName(object? player);

        /// <summary>
        /// Gets player ID
        /// </summary>
        ulong? GetPlayerId(object? player);

        /// <summary>
        /// Gets player mention
        /// </summary>
        string GetPlayerMention(object? player);

        /// <summary>
        /// Compares player IDs
        /// </summary>
        bool ComparePlayerIds(object? player1, object? player2);

        /// <summary>
        /// Converts to DiscordMember
        /// </summary>
        DiscordMember? ConvertToDiscordMember(object? player);
    }
}