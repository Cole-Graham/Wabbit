namespace Wabbit.BotClient.Config
{
    public class BotConfig
    {
        public string? Token { get; set; }
        public List<ServerConfig> Servers { get; set; } = [];

        public class ServerConfig
        {
            public ulong? ServerId { get; set; }
            public ulong? BotChannelId { get; set; }
            public ulong? ReplayChannelId { get; set; }
            public ulong? DeckChannelId { get; set; }
            public ulong? SignupChannelId { get; set; }
            public ulong? StandingsChannelId { get; set; }
        }
    }
}
