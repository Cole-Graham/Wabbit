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
    }
}