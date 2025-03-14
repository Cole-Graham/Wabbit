using DSharpPlus.Entities;
using System;
using System.Collections.Generic;

namespace Wabbit.Models
{
    public enum GameType
    {
        OneVsOne,
        TwoVsTwo
    }

    public class TournamentSignup
    {
        public string Name { get; set; } = string.Empty;
        public bool IsOpen { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Discord objects that shouldn't be serialized to JSON
        [System.Text.Json.Serialization.JsonIgnore]
        public List<DiscordMember> Participants { get; set; } = [];

        [System.Text.Json.Serialization.JsonIgnore]
        public List<ParticipantSeed> Seeds { get; set; } = [];

        [System.Text.Json.Serialization.JsonIgnore]
        public DiscordMessage? SignupListMessage { get; set; }

        public TournamentFormat Format { get; set; } = TournamentFormat.GroupStageWithPlayoffs;
        public GameType Type { get; set; } = GameType.OneVsOne;
        public DateTime? ScheduledStartTime { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<ParticipantInfo> ParticipantInfo { get; set; } = [];

        // Used to store seeding info from JSON until we can convert to DiscordMembers
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<SeedInfo> SeedInfo { get; set; } = [];
    }

    public class ParticipantSeed
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public DiscordMember Player { get; set; } = null!;

        public int Seed { get; set; } = 0; // 0 = unseeded, 1 = first seed, 2 = second seed, etc.

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public ulong PlayerId { get; set; }

        // This method ensures PlayerId is always populated with Player.Id when Player is set
        public void SetPlayer(DiscordMember player)
        {
            Player = player;
            PlayerId = player?.Id ?? 0;
        }
    }

    public class RelatedMessage
    {
        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }
        public string Type { get; set; } = "Announcement"; // e.g., "Announcement", "Standings", etc.
    }

    public class ParticipantInfo
    {
        public ulong Id { get; set; }
        public string Username { get; set; } = string.Empty;
    }

    public class SeedInfo
    {
        public ulong Id { get; set; }
        public int Seed { get; set; }
    }
}