using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using Wabbit.BotClient.Config;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Wabbit.BotClient.Commands
{
    [Command("Config")]
    [RequirePermissions(DiscordPermission.Administrator)]
    public class ConfigGroup
    {
        private readonly ILogger<ConfigGroup> _logger;

        public ConfigGroup(ILogger<ConfigGroup> logger)
        {
            _logger = logger;
        }

        [Command("setup")]
        [Description("Initial bot set-up")]
        public async Task Setup(CommandContext context, [Description("Bot channel")] DiscordChannel botChannel, [Description("Deck channel")] DiscordChannel deckChannel)
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
        public async Task Test(CommandContext context) =>
            await context.RespondAsync("Bot is online");

        [Command("set_bot_channel")]
        [Description("Set the bot channel")]
        public async Task SetBotChannel(
            CommandContext context,
            [Description("Text channel to set as bot channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

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
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

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
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

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
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageGuild))
            {
                await context.EditResponseAsync("You don't have permission to configure the bot.");
                return;
            }

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

            // Update the signup channel
            serverConfig.SignupChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"{channel.Mention} has been set as the tournament signup channel.");
        }

        [Command("set_standings_channel")]
        [Description("Set the tournament standings channel")]
        public async Task SetStandingsChannel(
            CommandContext context,
            [Description("Text channel to set as tournament standings channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            // Check permissions
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageGuild))
            {
                await context.EditResponseAsync("You don't have permission to configure the bot.");
                return;
            }

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

            // Update the standings channel
            serverConfig.StandingsChannelId = channel.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            // Post a confirmation in the standings channel
            var embed = new DiscordEmbedBuilder()
                .WithTitle("📊 Tournament Standings Channel")
                .WithDescription("This channel has been designated for tournament standings visualizations.")
                .WithColor(DiscordColor.Green)
                .WithFooter("Standings will be posted and updated here automatically");

            await channel.SendMessageAsync(embed);

            await context.EditResponseAsync($"{channel.Mention} has been set as the tournament standings channel.");
        }

        [Command("set_thread_archival_hours")]
        [Description("Set the number of hours before tournament threads are auto-archived")]
        public async Task SetThreadArchivalHours(
            CommandContext context,
            [Description("Number of hours (1-168)")] long hours)
        {
            await context.DeferResponseAsync();

            // Check permissions
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageGuild))
            {
                await context.EditResponseAsync("You don't have permission to configure the bot.");
                return;
            }

            // Validate the hours (Discord limits: 1, 24, 72, 168)
            if (hours < 1 || hours > 168)
            {
                await context.EditResponseAsync("The number of hours must be between 1 and 168 (7 days).");
                return;
            }

            // Convert to closest Discord auto-archive duration
            string durationText;
            if (hours < 24)
            {
                durationText = "1 hour";
            }
            else if (hours < 72)
            {
                durationText = "24 hours";
                hours = 24;
            }
            else if (hours < 168)
            {
                durationText = "3 days";
                hours = 72;
            }
            else
            {
                durationText = "7 days";
                hours = 168;
            }

            // Update the config
            ConfigManager.Config.Tournament.ThreadArchivalHours = (int)hours;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync($"Thread archival duration has been set to {durationText}. " +
                $"Tournament threads will be auto-archived after this period of inactivity when matches are completed.");
        }

        [Command("toggle_auto_archive")]
        [Description("Toggle automatic thread archiving for tournaments")]
        public async Task ToggleAutoArchive(
            CommandContext context,
            [Description("Whether to automatically archive threads (true/false)")] bool enabled)
        {
            await context.DeferResponseAsync();

            // Check permissions
            if (context.Member is null || !context.Member.Permissions.HasPermission(DiscordPermission.ManageGuild))
            {
                await context.EditResponseAsync("You don't have permission to configure the bot.");
                return;
            }

            // Update the config
            ConfigManager.Config.Tournament.AutoArchiveThreads = enabled;

            // Save the config
            await ConfigManager.SaveConfig();

            string status = enabled ? "enabled" : "disabled";
            await context.EditResponseAsync($"Automatic thread archiving has been {status} for tournaments.");
        }

        [Command("set_whitelisted_role")]
        [Description("Set the role that gives users access to basic bot commands")]
        public async Task SetWhitelistedRole(
            CommandContext context,
            [Description("Discord role that should have access to use bot commands")] DiscordRole role)
        {
            await context.DeferResponseAsync();

            // Get the server config, or create if none exists
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

            // Update the whitelisted role ID
            serverConfig.WhitelistedRoleId = role.Id;

            // Save the config
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"The {role.Mention} role has been set as the whitelisted role. " +
                             $"Users with this role can now access basic bot commands."));
        }

        [Command("view_whitelisted_role")]
        [Description("View which role is currently set as the whitelisted role")]
        public async Task ViewWhitelistedRole(CommandContext context)
        {
            await context.DeferResponseAsync();

            // Get the server config
            var serverConfig = GetOrCreateServerConfig(context.Guild?.Id ?? 0);

            if (serverConfig.WhitelistedRoleId == null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("No whitelisted role has been configured yet. " +
                                "Use `/config set_whitelisted_role` to set one."));
                return;
            }

            // Try to get the role from the guild
            DiscordRole? role = null;
            try
            {
                if (context.Guild is not null && context.Guild.Roles.TryGetValue(serverConfig.WhitelistedRoleId.Value, out var guildRole))
                {
                    role = guildRole;
                }
            }
            catch (Exception ex)
            {
                // Role not found
                _logger.LogError(ex, $"Error finding role with ID {serverConfig.WhitelistedRoleId}");
            }

            if (role is null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"The configured whitelisted role ID ({serverConfig.WhitelistedRoleId}) " +
                                 $"could not be found in this server. The role may have been deleted."));
                return;
            }

            await context.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"The current whitelisted role is: {role.Mention} (ID: {role.Id})"));
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
