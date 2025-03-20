using DSharpPlus.Entities;

namespace Wabbit.Tests.TestInfrastructure.Models
{
    /// <summary>
    /// Test-specific model for tournaments
    /// </summary>
    public partial class Tournament
    {
        /// <summary>
        /// Represents a participant in a tournament group
        /// </summary>
        public class GroupParticipant
        {
            /// <summary>
            /// The player (usually a DiscordMember)
            /// </summary>
            public object? Player { get; set; }

            /// <summary>
            /// The participant's seed
            /// </summary>
            public int Seed { get; set; }

            /// <summary>
            /// Number of wins
            /// </summary>
            public int Wins { get; set; }

            /// <summary>
            /// Number of draws
            /// </summary>
            public int Draws { get; set; }

            /// <summary>
            /// Number of losses
            /// </summary>
            public int Losses { get; set; }

            /// <summary>
            /// Total points (Wins * 3 + Draws)
            /// </summary>
            public int Points
            {
                get => (Wins * 3) + Draws;
                set
                {
                    // When points are set, calculate wins and draws
                    Draws = value % 3;
                    Wins = (value - Draws) / 3;
                }
            }
        }
    }
}