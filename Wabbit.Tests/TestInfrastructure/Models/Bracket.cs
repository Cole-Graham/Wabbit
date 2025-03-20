using System.Collections.Generic;
using Wabbit.Models;

namespace Wabbit.Tests.TestInfrastructure.Models
{
    /// <summary>
    /// Extension class for Tournament to add Bracket functionality for testing
    /// </summary>
    public partial class Tournament
    {
        /// <summary>
        /// Represents a bracket in a tournament
        /// </summary>
        public class Bracket
        {
            /// <summary>
            /// The bracket name (e.g., "Quarter-finals", "Winners Bracket")
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// The matches in this bracket
            /// </summary>
            public List<Match> Matches { get; set; } = new List<Match>();
        }

        /// <summary>
        /// Represents a match in a bracket
        /// </summary>
        public class Match
        {
            /// <summary>
            /// The match name
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// The participants in this match
            /// </summary>
            public List<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();

            /// <summary>
            /// Whether this match is completed
            /// </summary>
            public bool IsComplete { get; set; }
        }

        /// <summary>
        /// Represents a participant in a bracket match
        /// </summary>
        public class MatchParticipant
        {
            /// <summary>
            /// The player
            /// </summary>
            public object? Player { get; set; }

            /// <summary>
            /// The player's score in this match
            /// </summary>
            public int Score { get; set; }

            /// <summary>
            /// Source group position (for playoff seeding)
            /// </summary>
            public int SourceGroupPosition { get; set; }

            /// <summary>
            /// Source group (for playoff seeding)
            /// </summary>
            public Wabbit.Models.Tournament.Group? SourceGroup { get; set; }
        }
    }
}