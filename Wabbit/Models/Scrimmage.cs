using DSharpPlus.Entities;
using Wabbit.Models.Rating;

namespace Wabbit.Models
{
    /// <summary>
    /// Represents a scrimmage match between two sides (either individual players or teams)
    /// </summary>
    public class Scrimmage
    {
        /// <summary>
        /// The Discord thread where this scrimmage is taking place
        /// </summary>
        public required DiscordThreadChannel Thread { get; set; }

        /// <summary>
        /// The type of game being played (1v1, 2v2, etc.)
        /// </summary>
        public ScrimmageGameType GameType { get; set; } = ScrimmageGameType.OneVOne;

        /// <summary>
        /// Team A - can be an individual player (1v1) or a proper team (2v2, 3v3, 4v4)
        /// </summary>
        public ScrimmageTeam TeamA { get; set; } = null!;

        /// <summary>
        /// Team B - can be an individual player (1v1) or a proper team (2v2, 3v3, 4v4)
        /// </summary>
        public ScrimmageTeam TeamB { get; set; } = null!;

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
        /// The score for Team A
        /// </summary>
        public int TeamAScore { get; set; } = 0;

        /// <summary>
        /// The score for Team B
        /// </summary>
        public int TeamBScore { get; set; } = 0;

        /// <summary>
        /// The timestamp when the scrimmage was created
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The timestamp when the scrimmage was completed or cancelled, if applicable
        /// </summary>
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>
        /// The results of individual games: 1 for Team A winning, 2 for Team B winning
        /// </summary>
        public List<int> Results { get; set; } = [];

        /// <summary>
        /// Maps banned by Team A
        /// </summary>
        public List<string> TeamAMapBans { get; set; } = [];

        /// <summary>
        /// Maps banned by Team B
        /// </summary>
        public List<string> TeamBMapBans { get; set; } = [];

        /// <summary>
        /// Unconfirmed map bans for Team A
        /// </summary>
        public List<string> TeamAUnconfirmedMapBans { get; set; } = [];

        /// <summary>
        /// Unconfirmed map bans for Team B
        /// </summary>
        public List<string> TeamBUnconfirmedMapBans { get; set; } = [];

        /// <summary>
        /// Creates a new scrimmage between two players (1v1)
        /// </summary>
        /// <param name="thread">Discord thread for the scrimmage</param>
        /// <param name="player1">First player</param>
        /// <param name="deck1">First player's deck (optional)</param>
        /// <param name="player2">Second player</param>
        /// <param name="deck2">Second player's deck (optional)</param>
        public Scrimmage(DiscordThreadChannel thread, DiscordUser player1, string? deck1, DiscordUser player2, string? deck2)
        {
            Thread = thread;
            GameType = ScrimmageGameType.OneVOne;

            // Create teams with explicit Captain initialization to satisfy the required property
            TeamA = new ScrimmageTeam { Captain = player1 };
            if (deck1 != null)
                TeamA.SetDeckCode(player1, deck1);

            TeamB = new ScrimmageTeam { Captain = player2 };
            if (deck2 != null)
                TeamB.SetDeckCode(player2, deck2);
        }

        /// <summary>
        /// Creates a new scrimmage between two teams
        /// </summary>
        /// <param name="thread">Discord thread for the scrimmage</param>
        /// <param name="teamA">First team</param>
        /// <param name="teamB">Second team</param>
        /// <param name="gameType">Game type (2v2, 3v3, etc.)</param>
        public Scrimmage(DiscordThreadChannel thread, ScrimmageTeam teamA, ScrimmageTeam teamB, ScrimmageGameType gameType)
        {
            Thread = thread;
            GameType = gameType;
            TeamA = teamA;
            TeamB = teamB;
        }

        /// <summary>
        /// Default constructor for serialization
        /// </summary>
        public Scrimmage()
        {
        }
    }

    /// <summary>
    /// Represents a team or player participating in a scrimmage
    /// </summary>
    public class ScrimmageTeam
    {
        /// <summary>
        /// The team ID for rated matches (will be null for 1v1 matches or non-registered teams)
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// The team name for display (derived from players if not specified)
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// The captain/primary player of the team (for 1v1, this is the only player)
        /// </summary>
        public required DiscordUser Captain { get; set; }

        /// <summary>
        /// Additional players for team formats (2v2, 3v3, 4v4)
        /// </summary>
        public List<DiscordUser> Members { get; set; } = [];

        /// <summary>
        /// Deck codes for each player keyed by user ID
        /// </summary>
        public Dictionary<ulong, string> DeckCodes { get; set; } = new Dictionary<ulong, string>();

        /// <summary>
        /// The deck name for the captain (for backward compatibility and convenience in 1v1)
        /// </summary>
        public string? DeckName
        {
            get => DeckCodes.TryGetValue(Captain.Id, out var deck) ? deck : null;
            set
            {
                if (value != null)
                    DeckCodes[Captain.Id] = value;
                else if (DeckCodes.ContainsKey(Captain.Id))
                    DeckCodes.Remove(Captain.Id);
            }
        }

        /// <summary>
        /// Gets a display-friendly name for the team
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(TeamName))
                    return TeamName;

                if (Members.Count == 0)
                    return Captain.Username;

                // Build a name from the captain and members
                return $"{Captain.Username}'s Team";
            }
        }

        /// <summary>
        /// Default constructor for serialization
        /// </summary>
        public ScrimmageTeam()
        {
            // Workaround for required property during serialization
            // This will be properly set during deserialization
            Captain = null!;
        }

        /// <summary>
        /// Constructor for a single player (1v1)
        /// </summary>
        public ScrimmageTeam(DiscordUser player, string? deckName = null)
        {
            Captain = player;
            if (deckName != null)
                DeckCodes[player.Id] = deckName;
        }

        /// <summary>
        /// Constructor for a registered team
        /// </summary>
        public ScrimmageTeam(Team team, DiscordUser captain)
        {
            TeamId = team.TeamId;
            TeamName = team.TeamName;
            Captain = captain;

            // Add core players to members
            foreach (var player in team.CorePlayers)
            {
                if (player.Id != captain.Id)
                    Members.Add(player);
            }

            // Add secondary players to members
            foreach (var player in team.SecondaryPlayers)
            {
                if (player.Id != captain.Id && !Members.Any(m => m.Id == player.Id))
                    Members.Add(player);
            }
        }

        /// <summary>
        /// Sets a deck code for a specific player
        /// </summary>
        /// <param name="player">The player</param>
        /// <param name="deckCode">The deck code</param>
        public void SetDeckCode(DiscordUser player, string deckCode)
        {
            DeckCodes[player.Id] = deckCode;
        }

        /// <summary>
        /// Gets a deck code for a specific player
        /// </summary>
        /// <param name="player">The player</param>
        /// <returns>The deck code or null if not found</returns>
        public string? GetDeckCode(DiscordUser player)
        {
            return DeckCodes.TryGetValue(player.Id, out var deck) ? deck : null;
        }

        /// <summary>
        /// Gets a deck code for a player by their ID
        /// </summary>
        /// <param name="userId">The player's Discord ID</param>
        /// <returns>The deck code or null if not found</returns>
        public string? GetDeckCode(ulong userId)
        {
            return DeckCodes.TryGetValue(userId, out var deck) ? deck : null;
        }

        /// <summary>
        /// Checks if all players have submitted deck codes
        /// </summary>
        public bool AllPlayersHaveSubmittedDeckCodes()
        {
            // Check captain
            if (!DeckCodes.ContainsKey(Captain.Id))
                return false;

            // Check all members
            foreach (var member in Members)
            {
                if (!DeckCodes.ContainsKey(member.Id))
                    return false;
            }

            return true;
        }
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