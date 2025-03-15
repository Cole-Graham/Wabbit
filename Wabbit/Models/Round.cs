using DSharpPlus.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Wabbit.Models
{
    public class Round
    {
        public string? Name { get; set; }
        public int Length { get; set; } = 3;
        public List<Team>? Teams { get; set; }
        public bool OneVOne { get; set; }
        public int Cycle { get; set; } = 0;
        public bool InGame { get; set; } = false;
        public List<string> Maps { get; set; } = [];
        public string? Pings { get; set; } // To not use LINQ each time
        public List<DiscordMessage> MsgToDel { get; set; } = [];
        public string? TournamentId { get; set; } // Add this property to link rounds to tournaments
        public Dictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();
        public MatchStage CurrentStage { get; set; } = MatchStage.Created;
        public string? WinMsg { get; set; }
        public bool TournamentRound { get; set; }

        // Group stage tracking properties
        public int GroupStageMatchNumber { get; set; } = 0;
        public int TotalGroupStageMatches { get; set; } = 0;
        public bool IsCompleted { get; set; } = false;
        public string? MatchResult { get; set; } // String representation of match result (e.g. "2-1", "Draw")
        public int PointsAwarded { get; set; } = 0; // Points awarded for this match (3 for win, 1 for draw, 0 for loss)

        // For better tracking of match history in thread
        public ulong? StatusMessageId { get; set; } // ID of the current status message

        public class Participant
        {
            public DiscordMember? Player { get; set; }
            public string? Deck { get; set; }
            public string? TempDeckCode { get; set; }

            // Dictionary to store deck codes by map name
            // Key: Map name, Value: Deck code used for that map
            public Dictionary<string, string> DeckHistory { get; set; } = new Dictionary<string, string>();
        }

        public class Team
        {
            public string? Name { get; set; }
            public DiscordThreadChannel? Thread { get; set; } // Use ID?
            public List<Participant> Participants { get; set; } = [];
            public int Wins { get; set; } = 0;
            public List<string> MapBans { get; set; } = []; // Init not needed
            public bool HasSubmittedDeck => Participants.All(p => !string.IsNullOrEmpty(p.Deck));
        }
    }
}
