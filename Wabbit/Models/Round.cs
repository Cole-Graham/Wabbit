using DSharpPlus.Entities;

namespace Wabbit.Models
{
    public class Round
    {
        public string? Name { get; set; }
        public int Length { get; set; }
        public List<Team>? Teams { get; set; }
        public bool OneVOne { get; set; }
        public int Cycle { get; set; } = 0;
        public bool InGame { get; set; } = false;
        public List<string> Maps { get; set; } = [];
        public string? Pings { get; set; } // To not use LINQ each time
        public List<DiscordMessage> MsgToDel { get; set; } = [];

        public class Participant
        {
            public DiscordMember? Player { get; set; }
            public string? Deck { get; set; }

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
        }
    }
}
