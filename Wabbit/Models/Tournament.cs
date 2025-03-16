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
        public TournamentStage CurrentStage { get; set; } = TournamentStage.SignupOpen;
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

        // List of signed up players
        public List<object> SignedUpPlayers { get; set; } = [];

        // Tournament winner
        public object? Winner { get; set; }

        public Tournament()
        {
            Groups = new List<Group>();
            PlayoffMatches = new List<Match>();
            RelatedMessages = new List<RelatedMessage>();
            CustomProperties = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a deep copy of the tournament
        /// </summary>
        public Tournament DeepClone()
        {
            var clone = new Tournament
            {
                Name = Name,
                Format = Format,
                CurrentStage = CurrentStage,
                GameType = GameType,
                MatchesPerPlayer = MatchesPerPlayer,
                IsComplete = IsComplete,
                AnnouncementChannel = AnnouncementChannel,
                Settings = Settings,
                CustomProperties = CustomProperties?.ToDictionary(entry => entry.Key, entry => entry.Value),
                RelatedMessages = RelatedMessages?.ToList() ?? new List<RelatedMessage>(),
                SignedUpPlayers = SignedUpPlayers?.ToList() ?? new List<object>(),
                Winner = Winner
            };

            // Clone groups
            clone.Groups = Groups.Select(g => new Group
            {
                Name = g.Name,
                IsComplete = g.IsComplete,
                Tournament = clone,
                Participants = g.Participants.Select(p => new GroupParticipant
                {
                    Player = p.Player,
                    Wins = p.Wins,
                    Draws = p.Draws,
                    Losses = p.Losses,
                    Seed = p.Seed,
                    GamesWon = p.GamesWon,
                    GamesLost = p.GamesLost,
                    AdvancedToPlayoffs = p.AdvancedToPlayoffs,
                    QualificationInfo = p.QualificationInfo,
                    Position = p.Position
                }).ToList()
            }).ToList();

            // Clone matches within groups
            foreach (var originalGroup in Groups)
            {
                var clonedGroup = clone.Groups.First(g => g.Name == originalGroup.Name);
                clonedGroup.Matches = originalGroup.Matches.Select(m => CloneMatch(m, clone)).ToList();
            }

            // Clone playoff matches
            clone.PlayoffMatches = PlayoffMatches.Select(m => CloneMatch(m, clone)).ToList();

            // Fix match references (NextMatch and ThirdPlaceMatch)
            foreach (var originalMatch in PlayoffMatches)
            {
                var clonedMatch = clone.PlayoffMatches.First(m => m.Name == originalMatch.Name);
                if (originalMatch.NextMatch != null)
                {
                    clonedMatch.NextMatch = clone.PlayoffMatches.First(m => m.Name == originalMatch.NextMatch.Name);
                }
                if (originalMatch.ThirdPlaceMatch != null)
                {
                    clonedMatch.ThirdPlaceMatch = clone.PlayoffMatches.First(m => m.Name == originalMatch.ThirdPlaceMatch.Name);
                }
            }

            return clone;
        }

        /// <summary>
        /// Helper method to clone a match
        /// </summary>
        private Match CloneMatch(Match original, Tournament clonedTournament)
        {
            var clone = new Match
            {
                Id = Guid.NewGuid().ToString(),
                TournamentId = clonedTournament.Name,
                Name = original.Name,
                Type = original.Type,
                BestOf = original.BestOf,
                DisplayPosition = original.DisplayPosition,
                IsTiebreakerMatch = original.IsTiebreakerMatch,
                LinkedRound = original.LinkedRound,
                Participants = original.Participants.Select(p => new MatchParticipant
                {
                    Player = p.Player,
                    Score = p.Score,
                    SourceGroupPosition = p.SourceGroupPosition,
                    SourceGroup = p.SourceGroup != null ? clonedTournament.Groups.First(g => g.Name == p.SourceGroup.Name) : null
                }).ToList()
            };

            if (original.Result != null)
            {
                clone.Result = new MatchResult
                {
                    Winner = original.Result.Winner,
                    WinnerScore = original.Result.WinnerScore,
                    LoserScore = original.Result.LoserScore,
                    MapResults = original.Result.MapResults.ToList(),
                    CompletedAt = original.Result.CompletedAt,
                    Status = original.Result.Status,
                    ResultType = original.Result.ResultType,
                    Forfeiter = original.Result.Forfeiter,
                    DeckCodes = original.Result.DeckCodes.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value.ToDictionary(x => x.Key, x => x.Value)
                    )
                };
            }

            return clone;
        }

        public class Group
        {
            public string Name { get; set; } = "";
            public List<GroupParticipant> Participants { get; set; } = [];
            public List<Match> Matches { get; set; } = [];
            public bool IsComplete { get; set; } = false;
            public Tournament? Tournament { get; set; }
        }

        public class GroupParticipant
        {
            public object? Player { get; set; }
            public int Wins { get; set; } = 0;
            public int Draws { get; set; } = 0;
            public int Losses { get; set; } = 0;
            public int Points => (Wins * 3) + Draws;
            public bool AdvancedToPlayoffs { get; set; } = false;
            public int Position { get; set; } = 0; // Current position in group standings

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
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string TournamentId { get; set; } = "";
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

            /// <summary>
            /// Indicates if this is a tiebreaker match
            /// </summary>
            public bool IsTiebreakerMatch { get; set; }
        }

        public class MatchParticipant
        {
            public object? Player { get; set; }
            public Group? SourceGroup { get; set; } // For playoff seeding
            public Match? SourceMatch { get; set; } // For bracket advancement tracking
            public int SourceGroupPosition { get; set; } = 0; // 1 = first place, 2 = second place, etc.
            public int Score { get; set; } = 0;
            public string Display => Player?.ToString() ?? $"{SourceGroup?.Name ?? "Unknown"} #{SourceGroupPosition}";
        }

        public class MatchResult
        {
            public object? Winner { get; set; }
            public int WinnerScore { get; set; } = 0;
            public int LoserScore { get; set; } = 0;
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
        SignupOpen,
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
        GroupStageTiebreaker,  // For resolving perfect ties in group stage
        RoundOf16,
        Quarterfinal,
        Semifinal,
        Final,
        PlayoffThirdPlace,     // The optional third place match between semifinal losers
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
        // Player limits
        public int MinPlayers { get; set; } = 7;  // Minimum players required for tournament
        public int MaxPlayers { get; set; } = 32; // Maximum players allowed in tournament

        // Match format settings
        public bool IncludeThirdPlaceMatch { get; set; } = false;  // Default to no third place match
        public int BestOfFinals { get; set; } = 3;                 // Finals are Best-of-3
        public int BestOfSemifinals { get; set; } = 3;            // Semifinals are Best-of-3
        public int BestOfQuarterfinals { get; set; } = 3;         // Quarterfinals are Best-of-3
        public int BestOfGroupStage { get; set; } = 1;            // Group stage is Best-of-1
    }
}