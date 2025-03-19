using System;
using System.Collections.Generic;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a competitive season
    /// </summary>
    public class Season
    {
        /// <summary>
        /// Unique identifier for the season
        /// </summary>
        public string SeasonId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The name of the season (e.g., "Winter 2023")
        /// </summary>
        public string SeasonName { get; set; } = string.Empty;

        /// <summary>
        /// Optional description for the season
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// When the season started
        /// </summary>
        public DateTimeOffset StartDate { get; set; }

        /// <summary>
        /// When the season is scheduled to end (if known)
        /// </summary>
        public DateTimeOffset? EndDate { get; set; }

        /// <summary>
        /// Whether this is the currently active season
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Discord user ID of the creator
        /// </summary>
        public ulong CreatorId { get; set; }

        /// <summary>
        /// Creator's username (for display)
        /// </summary>
        public string CreatorUsername { get; set; } = string.Empty;

        /// <summary>
        /// Final rankings for the season across different team types.
        /// For TeamGameType.OneVOne, these will be individual player rankings.
        /// For other TeamGameType values, these will be team rankings.
        /// </summary>
        public Dictionary<TeamGameType, List<SeasonRanking>> FinalRankings { get; set; } = new Dictionary<TeamGameType, List<SeasonRanking>>();

        /// <summary>
        /// Messages related to this season (announcements, updates, etc.)
        /// </summary>
        public List<SeasonMessage> Messages { get; set; } = new List<SeasonMessage>();

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
        /// Gets a formatted string representing the duration of this season
        /// </summary>
        public string GetFormattedDuration()
        {
            if (!EndDate.HasValue)
            {
                var duration = DateTimeOffset.UtcNow - StartDate;
                return $"{duration.Days} days (ongoing)";
            }

            var totalDuration = EndDate.Value - StartDate;
            return $"{totalDuration.Days} days";
        }

        /// <summary>
        /// Adds a new message to the season
        /// </summary>
        public void AddMessage(SeasonMessage message)
        {
            Messages.Add(message);
        }

        /// <summary>
        /// Adds a related message to the season
        /// </summary>
        /// <param name="channelId">Channel ID where the message was sent</param>
        /// <param name="messageId">Message ID</param>
        /// <param name="messageType">Type of message</param>
        public void AddRelatedMessage(ulong channelId, ulong messageId, string messageType)
        {
            SeasonMessageType type = Enum.TryParse<SeasonMessageType>(messageType, out var parsedType)
                ? parsedType
                : SeasonMessageType.Announcement;

            var message = new SeasonMessage
            {
                ChannelId = channelId,
                MessageId = messageId,
                Type = type,
                Timestamp = DateTimeOffset.UtcNow
            };

            Messages.Add(message);
        }
    }

    /// <summary>
    /// Represents a ranking position in the season for a player or team
    /// </summary>
    public class SeasonRanking
    {
        /// <summary>
        /// Position in the rankings (1 = first place)
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Whether this ranking is for a team or an individual player.
        /// For 1v1 game type, this will typically be false (ranking individual players).
        /// For team-based game types, this will typically be true (ranking teams).
        /// </summary>
        public bool IsTeam { get; set; }

        /// <summary>
        /// The Discord user ID of the player (if IsTeam is false)
        /// </summary>
        public ulong PlayerId { get; set; }

        /// <summary>
        /// The player's username (if IsTeam is false)
        /// </summary>
        public string? PlayerUsername { get; set; }

        /// <summary>
        /// The team ID (if IsTeam is true)
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// The team name (if IsTeam is true)
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Final rating at the end of the season
        /// </summary>
        public int Rating { get; set; }

        /// <summary>
        /// Number of wins during the season
        /// </summary>
        public int Wins { get; set; }

        /// <summary>
        /// Number of losses during the season
        /// </summary>
        public int Losses { get; set; }

        /// <summary>
        /// Win rate percentage
        /// </summary>
        public double WinRate => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) * 100 : 0;

        /// <summary>
        /// Total matches played during the season
        /// </summary>
        public int MatchesPlayed => Wins + Losses;

        /// <summary>
        /// The formatted name for display in rankings
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (IsTeam)
                    return TeamName ?? "Unknown Team";
                else
                    return PlayerUsername ?? "Unknown Player";
            }
        }
    }

    /// <summary>
    /// Represents a message related to a season
    /// </summary>
    public class SeasonMessage
    {
        /// <summary>
        /// Discord channel ID where the message was posted
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Discord message ID
        /// </summary>
        public ulong MessageId { get; set; }

        /// <summary>
        /// Type of message (announcement, update, etc.)
        /// </summary>
        public SeasonMessageType Type { get; set; }

        /// <summary>
        /// When the message was posted
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}