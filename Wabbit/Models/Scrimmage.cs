using DSharpPlus.Entities;

namespace Wabbit.Models
{
    /// <summary>
    /// Represents a scrimmage match between two players or teams
    /// </summary>
    public class Scrimmage
    {
        /// <summary>
        /// The Discord thread where this scrimmage is taking place
        /// </summary>
        public required DiscordThreadChannel Thread { get; set; }

        /// <summary>
        /// First player in the scrimmage
        /// </summary>
        public required DiscordUser Player1 { get; set; }

        /// <summary>
        /// Optional deck name for player 1
        /// </summary>
        public string? Deck1 { get; set; }

        /// <summary>
        /// Second player in the scrimmage
        /// </summary>
        public required DiscordUser Player2 { get; set; }

        /// <summary>
        /// Optional deck name for player 2
        /// </summary>
        public string? Deck2 { get; set; }

        /// <summary>
        /// The maps selected for this scrimmage
        /// </summary>
        public List<string> Maps { get; set; } = [];

        /// <summary>
        /// Current status of the scrimmage
        /// </summary>
        public ScrimmageStatus Status { get; set; } = ScrimmageStatus.Created;

        /// <summary>
        /// A list of all messages related to this scrimmage for easy tracking
        /// </summary>
        public List<DiscordMessage> Messages { get; set; } = [];

        /// <summary>
        /// The status message that is constantly updated
        /// </summary>
        public DiscordMessage? StatusMessage { get; set; }

        /// <summary>
        /// The length of the match (best of 1, 3 or 5)
        /// </summary>
        public MatchLength MatchLength { get; set; } = MatchLength.Bo1;

        /// <summary>
        /// Whether this is a rated match that will affect player/team ratings
        /// </summary>
        public bool IsRated { get; set; } = false;

        /// <summary>
        /// Whether to use the tournament map pool instead of the casual map pool
        /// </summary>
        public bool UseTournamentMapPool { get; set; } = false;

        /// <summary>
        /// The current game number in the match sequence
        /// </summary>
        public int CurrentGameNumber { get; set; } = 1;

        /// <summary>
        /// The score for player/team 1
        /// </summary>
        public int Player1Score { get; set; } = 0;

        /// <summary>
        /// The score for player/team 2
        /// </summary>
        public int Player2Score { get; set; } = 0;

        /// <summary>
        /// The timestamp when the scrimmage was created
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The timestamp when the scrimmage was completed or cancelled, if applicable
        /// </summary>
        public DateTimeOffset? CompletedAt { get; set; }
    }

    /// <summary>
    /// Represents the different states a scrimmage can be in
    /// </summary>
    public enum ScrimmageStatus
    {
        /// <summary>
        /// Scrimmage has been created but not yet started
        /// </summary>
        Created,

        /// <summary>
        /// Scrimmage is in progress
        /// </summary>
        InProgress,

        /// <summary>
        /// Scrimmage has been completed
        /// </summary>
        Completed,

        /// <summary>
        /// Scrimmage has been cancelled
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// Represents the type of game being played
    /// </summary>
    public enum ScrimmageGameType
    {
        /// <summary>
        /// 1v1 match
        /// </summary>
        OneVOne,

        /// <summary>
        /// 2v2 match
        /// </summary>
        TwoVTwo,

        /// <summary>
        /// 3v3 match
        /// </summary>
        ThreeVThree,

        /// <summary>
        /// 4v4 match
        /// </summary>
        FourVFour
    }

    /// <summary>
    /// Represents the length of a match
    /// </summary>
    public enum MatchLength
    {
        /// <summary>
        /// Best of 1 (single game)
        /// </summary>
        Bo1,

        /// <summary>
        /// Best of 3 (first to 2 wins)
        /// </summary>
        Bo3,

        /// <summary>
        /// Best of 5 (first to 3 wins)
        /// </summary>
        Bo5
    }
}