using System;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a single entry in a leaderboard
    /// </summary>
    public class LeaderboardEntry
    {
        /// <summary>
        /// Rank position in the leaderboard
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Whether this entry is for a team (true) or player (false)
        /// </summary>
        public bool IsTeam { get; set; }

        /// <summary>
        /// Player Discord ID (if IsTeam is false)
        /// </summary>
        public ulong? PlayerId { get; set; }

        /// <summary>
        /// Player Discord username (if IsTeam is false)
        /// </summary>
        public string? PlayerUsername { get; set; }

        /// <summary>
        /// Team ID (if IsTeam is true)
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// Team name (if IsTeam is true)
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Current rating 
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
        /// Win rate percentage (calculated)
        /// </summary>
        public double WinRate => (Wins + Losses) > 0 ? Math.Round((double)Wins / (Wins + Losses) * 100, 1) : 0;

        /// <summary>
        /// Rating change from previous time period (e.g., week)
        /// </summary>
        public int RatingChange { get; set; }

        /// <summary>
        /// Display name based on whether this is a team or player
        /// </summary>
        public string DisplayName => IsTeam ? TeamName ?? "Unknown Team" : PlayerUsername ?? "Unknown Player";
    }
}