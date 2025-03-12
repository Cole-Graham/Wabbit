using DSharpPlus.Entities;
using System;
using System.Collections.Generic;

namespace Wabbit.Models
{
    public class TournamentSignup
    {
        public string Name { get; set; } = string.Empty;
        public bool IsOpen { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<DiscordMember> Participants { get; set; } = [];
        public DiscordMessage? SignupListMessage { get; set; }
        public TournamentFormat Format { get; set; } = TournamentFormat.GroupStageWithPlayoffs;
        public DateTime? ScheduledStartTime { get; set; }
        public DiscordUser CreatedBy { get; set; } = null!;

        // Store plain properties for serialization
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public ulong CreatorId { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public string CreatorUsername { get; set; } = string.Empty;

        public ulong SignupChannelId { get; set; }
        public ulong MessageId { get; set; }

        // List of related message IDs (e.g., standings visualizations, announcement messages)
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<RelatedMessage> RelatedMessages { get; set; } = [];

        // Used to store participant info from JSON until we can convert to DiscordMembers
        [System.Text.Json.Serialization.JsonIgnore]
        public List<(ulong Id, string Username)> ParticipantInfo { get; set; } = [];
    }

    public class RelatedMessage
    {
        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }
        public string Type { get; set; } = "Announcement"; // e.g., "Announcement", "Standings", etc.
    }
}