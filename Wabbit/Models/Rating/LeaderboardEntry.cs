using System;
using Wabbit.Models;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a leaderboard entry for either a player (from PlayerRating for 1v1) or a team (from Team for any game type)
    /// </summary>
    public class LeaderboardEntry
    {
        /// <summary>
        /// The displayed rank on the leaderboard
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Whether this entry represents a team (true) or individual player (false)
        /// </summary>
        public bool IsTeam { get; set; }

        /// <summary>
        /// Discord user ID if this is a player entry, null if team entry
        /// </summary>
        public ulong? PlayerId { get; set; }

        /// <summary>
        /// Player's username if this is a player entry, null if team entry
        /// </summary>
        public string? PlayerUsername { get; set; }

        /// <summary>
        /// Team ID if this is a team entry, null if player entry
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// Team name if this is a team entry, null if player entry
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Current rating for the entry
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// Number of wins
        /// </summary>
        public int Wins { get; set; }

        /// <summary>
        /// Number of losses
        /// </summary>
        public int Losses { get; set; }

        /// <summary>
        /// Display name to show on the leaderboard (either player username or team name)
        /// </summary>
        public string DisplayName => IsTeam ? TeamName ?? "Unknown Team" : PlayerUsername ?? "Unknown Player";

        /// <summary>
        /// Win rate as a percentage
        /// </summary>
        public double WinRate => (Wins + Losses) > 0 ? Math.Round((double)Wins / (Wins + Losses) * 100, 1) : 0;

        /// <summary>
        /// The total number of matches played
        /// </summary>
        public int MatchesPlayed => Wins + Losses;

        /// <summary>
        /// Gets a text representation of the leaderboard entry for display purposes
        /// </summary>
        public string ToString(bool includeRank = true)
        {
            string rankDisplay = includeRank ? $"#{Rank}: " : "";
            string typeDisplay = IsTeam ? "Team" : "Player";

            return $"{rankDisplay}{DisplayName} ({typeDisplay}) - " +
                   $"Rating: {Rating}, " +
                   $"W/L: {Wins}-{Losses} ({WinRate:F1}%)";
        }
    }
}