namespace Wabbit.BotClient.Config
{
    public class BotConfig
    {
        public string? Token { get; set; }
        public List<ServerConfig> Servers { get; set; } = [];
        public TournamentConfig Tournament { get; set; } = new();

        public class ServerConfig
        {
            public ulong? ServerId { get; set; }
            public ulong? BotChannelId { get; set; }
            public ulong? ReplayChannelId { get; set; }
            public ulong? DeckChannelId { get; set; }
            public ulong? SignupChannelId { get; set; }
            public ulong? StandingsChannelId { get; set; }
        }

        public class TournamentConfig
        {
            /// <summary>
            /// Default thread archival duration in hours for completed matches
            /// </summary>
            public int ThreadArchivalHours { get; set; } = 24;

            /// <summary>
            /// Whether to automatically archive threads when matches are completed
            /// </summary>
            public bool AutoArchiveThreads { get; set; } = true;
        }
    }
}
