using System;
using System.Collections.Generic;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a competitive season for player and team ratings
    /// </summary>
    public class Season
    {
        /// <summary>
        /// Unique identifier for the season
        /// </summary>
        public string SeasonId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name for the season
        /// </summary>
        public string SeasonName { get; set; } = string.Empty;

        /// <summary>
        /// When the season started
        /// </summary>
        public DateTimeOffset StartDate { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// When the season ended (null if still active)
        /// </summary>
        public DateTimeOffset? EndDate { get; set; }

        /// <summary>
        /// Whether this is the currently active season
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// Description of the season (optional)
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Leaderboard snapshots at the end of the season
        /// </summary>
        public Dictionary<TeamGameType, List<SeasonRanking>> FinalRankings { get; set; } = new Dictionary<TeamGameType, List<SeasonRanking>>();

        /// <summary>
        /// Discord user ID of the creator
        /// </summary>
        public ulong CreatorId { get; set; }

        /// <summary>
        /// Username of the creator
        /// </summary>
        public string CreatorUsername { get; set; } = string.Empty;

        /// <summary>
        /// Related messaging for the season
        /// </summary>
        public List<SeasonMessage> RelatedMessages { get; set; } = [];

        /// <summary>
        /// Creates a new season with the given name
        /// </summary>
        public Season(string name)
        {
            SeasonName = name;
            StartDate = DateTimeOffset.UtcNow;
            IsActive = true;

            // Initialize the final rankings dictionary
            FinalRankings = new Dictionary<TeamGameType, List<SeasonRanking>>
            {
                { TeamGameType.OneVOne, new List<SeasonRanking>() },
                { TeamGameType.TwoVTwo, new List<SeasonRanking>() },
                { TeamGameType.ThreeVThree, new List<SeasonRanking>() },
                { TeamGameType.FourVFour, new List<SeasonRanking>() }
            };
        }

        /// <summary>
        /// Default constructor for deserialization
        /// </summary>
        public Season()
        {
            // Initialize the final rankings dictionary
            FinalRankings = new Dictionary<TeamGameType, List<SeasonRanking>>
            {
                { TeamGameType.OneVOne, new List<SeasonRanking>() },
                { TeamGameType.TwoVTwo, new List<SeasonRanking>() },
                { TeamGameType.ThreeVThree, new List<SeasonRanking>() },
                { TeamGameType.FourVFour, new List<SeasonRanking>() }
            };
        }

        /// <summary>
        /// Ends the season and captures final rankings
        /// </summary>
        public void EndSeason(Dictionary<TeamGameType, List<SeasonRanking>> rankings)
        {
            EndDate = DateTimeOffset.UtcNow;
            IsActive = false;
            FinalRankings = rankings;
        }

        /// <summary>
        /// Adds a related message (announcement, leaderboard, etc.)
        /// </summary>
        public void AddRelatedMessage(ulong channelId, ulong messageId, string type)
        {
            RelatedMessages.Add(new SeasonMessage
            {
                ChannelId = channelId,
                MessageId = messageId,
                Type = type
            });
        }

        /// <summary>
        /// Gets the duration of the season
        /// </summary>
        public TimeSpan GetDuration()
        {
            return (EndDate ?? DateTimeOffset.UtcNow) - StartDate;
        }

        /// <summary>
        /// Gets a formatted string representing the season's duration
        /// </summary>
        public string GetFormattedDuration()
        {
            var duration = GetDuration();

            if (duration.TotalDays >= 30)
            {
                int months = (int)(duration.TotalDays / 30);
                int days = (int)(duration.TotalDays % 30);
                return $"{months} month{(months != 1 ? "s" : "")} and {days} day{(days != 1 ? "s" : "")}";
            }
            else
            {
                return $"{(int)duration.TotalDays} day{(duration.TotalDays != 1 ? "s" : "")}";
            }
        }
    }

    /// <summary>
    /// Represents a player or team's ranking at the end of a season
    /// </summary>
    public class SeasonRanking
    {
        /// <summary>
        /// Final position in the rankings
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Whether this is a team (true) or individual player (false)
        /// </summary>
        public bool IsTeam { get; set; }

        /// <summary>
        /// Team ID if this is a team ranking
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// Team name if this is a team ranking
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Player ID if this is an individual player ranking
        /// </summary>
        public ulong? PlayerId { get; set; }

        /// <summary>
        /// Player username if this is an individual player ranking
        /// </summary>
        public string? PlayerUsername { get; set; }

        /// <summary>
        /// Final rating at the end of the season
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// Total wins during the season
        /// </summary>
        public int Wins { get; set; }

        /// <summary>
        /// Total losses during the season
        /// </summary>
        public int Losses { get; set; }

        /// <summary>
        /// Calculated win rate
        /// </summary>
        public double WinRate => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) * 100 : 0;

        /// <summary>
        /// Total matches played
        /// </summary>
        public int MatchesPlayed => Wins + Losses;
    }

    /// <summary>
    /// Represents a message related to a season
    /// </summary>
    public class SeasonMessage
    {
        /// <summary>
        /// Discord channel ID where the message was sent
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Discord message ID
        /// </summary>
        public ulong MessageId { get; set; }

        /// <summary>
        /// Type of message (e.g. "Announcement", "Leaderboard")
        /// </summary>
        public string Type { get; set; } = "Announcement";
    }
}