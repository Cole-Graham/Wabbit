using DSharpPlus.Entities;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path; // Add alias for System.IO.Path

namespace Wabbit.Models
{
    public class Tournament
    {
        public string Name { get; set; } = "Tournament";
        public List<Group> Groups { get; set; } = [];
        public List<Match> PlayoffMatches { get; set; } = [];
        public TournamentStage CurrentStage { get; set; } = TournamentStage.Groups;
        public TournamentFormat Format { get; set; } = TournamentFormat.GroupStageWithPlayoffs;
        public GameType GameType { get; set; } = GameType.OneVsOne;
        public int MatchesPerPlayer { get; set; } = 0; // Default to roundrobin
        public bool IsComplete { get; set; } = false;
        [System.Text.Json.Serialization.JsonIgnore]
        public DiscordChannel? AnnouncementChannel { get; set; }

        // Tournament settings
        public TournamentSettings? Settings { get; set; }

        // Related message IDs for deletion when tournament is removed
        public List<RelatedMessage> RelatedMessages { get; set; } = [];

        // Custom properties for storing dynamic configuration
        public Dictionary<string, object>? CustomProperties { get; set; }

        public class Group
        {
            public string Name { get; set; } = "";
            public List<GroupParticipant> Participants { get; set; } = [];
            public List<Match> Matches { get; set; } = [];
            public bool IsComplete { get; set; } = false;
        }

        public class GroupParticipant
        {
            public object? Player { get; set; }
            public int Wins { get; set; } = 0;
            public int Draws { get; set; } = 0;
            public int Losses { get; set; } = 0;
            public int Points => (Wins * 3) + Draws;
            public bool AdvancedToPlayoffs { get; set; } = false;

            // Seeding information
            public int Seed { get; set; } = 0; // 0 = unseeded, 1 = first seed, 2 = second seed, etc.

            // For tiebreakers if needed
            public int GamesWon { get; set; } = 0;
            public int GamesLost { get; set; } = 0;

            // Qualification information for visualization
            public string? QualificationInfo { get; set; }

            public Group? SourceGroup { get; set; } // For playoff seeding
            public Match? SourceMatch { get; set; } // For bracket advancement tracking
            public int SourceGroupPosition { get; set; } = 0; // 1 = first place, 2 = second place, etc.
            public int Score { get; set; } = 0;
            public bool IsWinner { get; set; } = false;
            public string Display => Player?.ToString() ?? $"{SourceGroup?.Name ?? "Unknown"} #{SourceGroupPosition}";
        }

        public class Match
        {
            public string Name { get; set; } = "";
            public TournamentMatchType Type { get; set; } = TournamentMatchType.GroupStage;
            public List<MatchParticipant> Participants { get; set; } = [];
            public MatchResult? Result { get; set; }
            public Round? LinkedRound { get; set; } // Reference to the actual round in the system
            public Match? NextMatch { get; set; } // For brackets, the match that follows
            public Match? ThirdPlaceMatch { get; set; } // For semifinals, refers to the third place match
            public bool IsComplete => Result != null;
            public int BestOf { get; set; } = 3; // Default Bo3

            // For playoffs display
            public string DisplayPosition { get; set; } = ""; // e.g., "Semifinal 1", "Final"
        }

        public class MatchParticipant
        {
            public object? Player { get; set; }
            public Group? SourceGroup { get; set; } // For playoff seeding
            public Match? SourceMatch { get; set; } // For bracket advancement tracking
            public int SourceGroupPosition { get; set; } = 0; // 1 = first place, 2 = second place, etc.
            public int Score { get; set; } = 0;
            public bool IsWinner { get; set; } = false;
            public string Display => Player?.ToString() ?? $"{SourceGroup?.Name ?? "Unknown"} #{SourceGroupPosition}";
        }

        public class MatchResult
        {
            public object? Winner { get; set; }
            public List<string> MapResults { get; set; } = [];
            public DateTime CompletedAt { get; set; } = DateTime.Now;
            public MatchStatus Status { get; set; } = MatchStatus.Completed;
            public MatchResultType ResultType { get; set; } = MatchResultType.Normal;
            public object? Forfeiter { get; set; } // For forfeit results

            // Dictionary<PlayerID, Dictionary<MapName, DeckCode>>
            // Stores deck codes by player ID and map name for verification
            public Dictionary<string, Dictionary<string, string>> DeckCodes { get; set; } =
                new Dictionary<string, Dictionary<string, string>>();
        }
    }

    public enum TournamentStage
    {
        Groups,
        Playoffs,
        Complete
    }

    public enum TournamentFormat
    {
        GroupStageWithPlayoffs,
        SingleElimination,
        DoubleElimination,
        RoundRobin
    }

    public enum TournamentMatchType
    {
        GroupStage,
        PlayoffStage,
        RoundOf16,
        Quarterfinal,
        Semifinal,
        Final,
        ThirdPlace,
        ThirdPlaceTiebreaker
    }

    public enum MatchStatus
    {
        Pending,
        InProgress,
        Completed,
        Cancelled
    }

    public enum MatchResultType
    {
        Normal,
        Forfeit,
        Disqualification,
        Tiebreaker,
        Default    // Used for byes and other automatic advancements
    }

    // Add TournamentSettings class
    public class TournamentSettings
    {
        public bool IncludeThirdPlaceMatch { get; set; } = false; // Default to no third place match
        public int BestOfFinals { get; set; } = 3; // Change to best of 3
        public int BestOfSemifinals { get; set; } = 3;
        public int BestOfQuarterfinals { get; set; } = 3;
        public int BestOfGroupStage { get; set; } = 1; // Group stage is best of 1
    }
}