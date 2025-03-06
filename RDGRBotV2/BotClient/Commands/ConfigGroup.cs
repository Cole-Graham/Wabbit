using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using RDGRBotV2.BotClient.Config;
using System.ComponentModel;

namespace RDGRBotV2.BotClient.Commands
{
    [Command("Config")]
    [RequirePermissions(DiscordPermission.Administrator)]
    public class ConfigGroup
    {
        [Command("setup")]
        [Description("Initial bot set-up")]
        public static async Task Setup(CommandContext context, [Description("Bot channel")] DiscordChannel botChannel, [Description("Deck channel")] DiscordChannel deckChannel)
        {
            if (context.Guild is null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("This command must be used in a server"));
                return;
            }

            await context.DeferResponseAsync();

            // Get or create server config
            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild.Id);
            if (server == null)
            {
                server = new BotConfig.ServerConfig { ServerId = context.Guild.Id };
                ConfigManager.Config.Servers.Add(server);
            }

            if (botChannel.Type != DiscordChannelType.Voice && deckChannel.Type != DiscordChannelType.Voice)
            {
                if (server.BotChannelId != botChannel.Id)
                    server.BotChannelId = botChannel.Id;

                if (server.DeckChannelId != deckChannel.Id)
                    server.DeckChannelId = deckChannel.Id;

                await ConfigManager.SaveConfig();

                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Config has been saved"));
            }
            else
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Selected invalid channel type"));
        }

        [Command("test")]
        [Description("Bot status test")]
        public static async Task Test(CommandContext context) =>
            await context.RespondAsync("Bot is online");
    }
}
