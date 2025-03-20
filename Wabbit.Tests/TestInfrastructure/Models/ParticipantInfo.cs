namespace Wabbit.Tests.TestInfrastructure.Models
{
    /// <summary>
    /// Test-specific model for tournament participants with seeding information
    /// </summary>
    public class ParticipantInfo
    {
        /// <summary>
        /// The participant's Discord ID
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The participant's username
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The participant's seed value (0 = unseeded, 1 = first seed, etc.)
        /// </summary>
        public int? Seed { get; set; }
    }
}