using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using Wabbit.BotClient.Config;
using System.ComponentModel;

namespace Wabbit.BotClient.Commands
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

        [Command("set_bot_channel")]
        [Description("Set the bot channel")]
        public async Task SetBotChannel(
            CommandContext context,
            [Description("Text channel to set as bot channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild.Id);

            // Update the bot channel
            serverConfig.BotChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"{channel.Mention} has been set as the bot channel.");
        }

        [Command("set_replay_channel")]
        [Description("Set the replay channel")]
        public async Task SetReplayChannel(
            CommandContext context,
            [Description("Text channel to set as replay channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild.Id);

            // Update the replay channel
            serverConfig.ReplayChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"{channel.Mention} has been set as the replay channel.");
        }

        [Command("set_deck_channel")]
        [Description("Set the deck channel")]
        public async Task SetDeckChannel(
            CommandContext context,
            [Description("Text channel to set as deck channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild.Id);

            // Update the deck channel
            serverConfig.DeckChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"{channel.Mention} has been set as the deck channel.");
        }

        [Command("set_signup_channel")]
        [Description("Set the tournament signup channel")]
        public async Task SetSignupChannel(
            CommandContext context,
            [Description("Text channel to set as tournament signup channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Check permissions
            if (!context.Member.Permissions.HasPermission(DSharpPlus.Permissions.ManageGuild))
            {
                await context.EditResponseAsync("You don't have permission to configure the bot.");
                return;
            }

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild.Id);

            // Update the signup channel
            serverConfig.SignupChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"{channel.Mention} has been set as the tournament signup channel.");
        }

        private BotConfig.ServerConfig GetOrCreateServerConfig(ulong guildId)
        {
            var serverConfig = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == guildId);

            if (serverConfig == null)
            {
                serverConfig = new BotConfig.ServerConfig { ServerId = guildId };
                ConfigManager.Config.Servers.Add(serverConfig);
            }

            return serverConfig;
        }
    }
}
