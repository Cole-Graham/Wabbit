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
    }
}