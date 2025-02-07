using DSharpPlus.Entities;

namespace RDGRBotV2.Models
{
    public class Regular1v1
    {
        public required DiscordUser Player1 { get; set; }
        public string? Deck1 { get; set; }
        public required DiscordUser? Player2 { get; set; }
        public string? Deck2 { get; set; }
        public List<DiscordMessage> Messages { get; set; } = [];
    }
}
