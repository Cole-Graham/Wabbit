using DSharpPlus.Entities;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Wabbit.Models
{
    public class Round
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public int Length { get; set; } = 3;
        public List<Team> Teams { get; set; } = [];
        public bool OneVOne { get; set; }
        public int Cycle { get; set; } = 0;
        public bool InGame { get; set; } = false;
        public List<string> Maps { get; set; } = [];
        public string? Pings { get; set; }
        public List<DiscordMessage> MsgToDel { get; set; } = [];
        public string? TournamentId { get; set; }
        public Dictionary<string, object> CustomProperties { get; set; } = [];
        public MatchStage CurrentStage { get; set; } = MatchStage.Created;
        public string? WinMsg { get; set; }
        public bool TournamentRound { get; set; }

        // Properties for tracking conditional map bans
        public bool CoinflipPerformed { get; set; } = false;
        public string? CoinflipWinnerTeamName { get; set; }
        public string? CoinflipHeadsTeamName { get; set; }
        public string? CoinflipTailsTeamName { get; set; }

        // Group stage tracking properties
        public int GroupStageMatchNumber { get; set; } = 0;
        public int TotalGroupStageMatches { get; set; } = 0;
        public bool IsCompleted { get; set; } = false;
        public string? MatchResult { get; set; }
        public int PointsAwarded { get; set; } = 0;

        public ulong? StatusMessageId { get; set; }

        public class Participant
        {
            public DiscordMember? Player { get; set; }
            public string? Deck { get; set; }
            public string? TempDeckCode { get; set; }
            public Dictionary<string, string> DeckHistory { get; set; } = [];
        }

        public class Team
        {
            public string? Name { get; set; }
            public DiscordThreadChannel? Thread { get; set; }
            public List<Participant> Participants { get; set; } = [];
            public int Wins { get; set; } = 0;
            public List<string> MapBans { get; set; } = [];
            public List<string> UnconfirmedMapBans { get; set; } = [];
            public bool HasSubmittedDeck => Participants.All(p => !string.IsNullOrEmpty(p.Deck));
        }
    }
}
