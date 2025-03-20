using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using Wabbit.BotClient.Attributes;
using Wabbit.BotClient.Config;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.ComponentModel;

namespace Wabbit.BotClient.Commands
{
    [Command("Scrimmage")]
    [RequireWhitelistedRole]
    public class ScrimmageGroup
    {
        private readonly IScrimmageStatusService _scrimmageService;
        private readonly IMapService _mapService;

        public ScrimmageGroup(
            IScrimmageStatusService scrimmageService,
            IMapService mapService)
        {
            _scrimmageService = scrimmageService;
            _mapService = mapService;
        }

        [Command("scrimmage")]
        [Description("Start a scrimmage match with another player")]
        // TODO: Reinstate the whitelist once we have the attribute working
        //[RequireUser(193778962323456000, 182941761801420800)] // Replace with actual user IDs for the whitelist
        public async Task StartScrimmage(
            CommandContext context,
            [Description("Other player to scrimmage with")] DiscordUser opponent,
            [Description("Game type (0=1v1, 1=2v2, 2=3v3, 3=4v4)")] int gameTypeInt = 0,
            [Description("Match length (0=Bo1, 1=Bo3, 2=Bo5)")] int matchLengthInt = 0,
            [Description("Is this a rated match? (affects ratings)")] bool isRated = false,
            [Description("Use tournament map pool instead of casual")] bool tournamentMaps = false,
            [Description("Your deck name (optional)")] string? deck1 = null,
            [Description("Opponent's deck name (optional)")] string? deck2 = null)
        {
            await context.DeferResponseAsync();

            // Check if we're in the designated scrimmage channel
            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild?.Id);
            if (server?.ScrimmageChannelId == null || server.ScrimmageChannelId != context.Channel.Id)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "This command can only be used in the designated scrimmage channel."));
                return;
            }

            // Can't scrimmage with yourself
            if (opponent.Id == context.User.Id)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "You can't scrimmage with yourself!"));
                return;
            }

            // Can't scrimmage with a bot
            if (opponent.IsBot)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "You can't scrimmage with a bot!"));
                return;
            }

            // Convert parameters to enums
            var gameType = gameTypeInt switch
            {
                1 => ScrimmageGameType.TwoVTwo,
                2 => ScrimmageGameType.ThreeVThree,
                3 => ScrimmageGameType.FourVFour,
                _ => ScrimmageGameType.OneVOne
            };

            var matchLength = matchLengthInt switch
            {
                1 => MatchLength.Bo3,
                2 => MatchLength.Bo5,
                _ => MatchLength.Bo1
            };

            // For rated matches, always use tournament maps
            if (isRated)
                tournamentMaps = true;

            // Create the scrimmage
            var scrimmage = await _scrimmageService.CreateScrimmageAsync(
                context.Channel,
                context.User,
                deck1,
                opponent,
                deck2,
                gameType,
                matchLength,
                isRated,
                tournamentMaps);

            // Respond with confirmation
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"Scrimmage started! Check the thread: {scrimmage.Thread.Mention}"));
        }

        [Command("win")]
        [Description("Record a win for a player in the current game")]
        public async Task RecordWin(
            CommandContext context,
            [Description("The player who won this game (1 or 2)")] int winningPlayer)
        {
            await context.DeferResponseAsync();

            if (winningPlayer != 1 && winningPlayer != 2)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "Invalid player number. Please use 1 for the first player or 2 for the second player."));
                return;
            }

            // Check if this is a scrimmage thread
            var threadId = context.Channel.Id;
            var scrimmage = await _scrimmageService.GetScrimmageByThreadIdAsync(threadId);

            if (scrimmage == null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "This command can only be used in a scrimmage thread."));
                return;
            }

            // Check if the user has permission to manage this scrimmage
            if (!await _scrimmageService.CanManageScrimmageAsync(scrimmage, context.User.Id))
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "Only players participating in this scrimmage or administrators can record results."));
                return;
            }

            // Record the win
            await _scrimmageService.RecordGameResultAsync(scrimmage, winningPlayer);

            // Check if we need to advance to the next game
            bool matchComplete = scrimmage.Status == ScrimmageStatus.Completed;
            if (!matchComplete && (scrimmage.MatchLength == MatchLength.Bo3 || scrimmage.MatchLength == MatchLength.Bo5))
            {
                await _scrimmageService.AdvanceToNextGameAsync(scrimmage);
            }

            if (matchComplete)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "Win recorded. The match is now complete!"));
            }
            else
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"Win recorded for {(winningPlayer == 1 ? scrimmage.TeamA.Captain.Username : scrimmage.TeamB.Captain.Username)}"));
            }
        }

        [Command("complete")]
        [Description("Mark a scrimmage as completed")]
        public async Task CompleteScrimmage(CommandContext context)
        {
            await context.DeferResponseAsync();

            // Check if this is a scrimmage thread
            var threadId = context.Channel.Id;
            var scrimmage = await _scrimmageService.GetScrimmageByThreadIdAsync(threadId);

            if (scrimmage == null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "This command can only be used in a scrimmage thread."));
                return;
            }

            // Check if the user has permission to manage this scrimmage
            if (!await _scrimmageService.CanManageScrimmageAsync(scrimmage, context.User.Id))
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "Only players participating in this scrimmage or administrators can mark it as completed."));
                return;
            }

            // Complete the scrimmage
            await _scrimmageService.CompleteScrimmageAsync(scrimmage);

            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                "This scrimmage has been marked as completed. Thanks for playing!"));
        }

        [Command("cancel")]
        [Description("Cancel a scrimmage")]
        public async Task CancelScrimmage(CommandContext context)
        {
            await context.DeferResponseAsync();

            // Check if this is a scrimmage thread
            var threadId = context.Channel.Id;
            var scrimmage = await _scrimmageService.GetScrimmageByThreadIdAsync(threadId);

            if (scrimmage == null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "This command can only be used in a scrimmage thread."));
                return;
            }

            // Check if the user has permission to manage this scrimmage
            if (!await _scrimmageService.CanManageScrimmageAsync(scrimmage, context.User.Id))
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "Only players participating in this scrimmage or administrators can cancel it."));
                return;
            }

            // Cancel the scrimmage
            await _scrimmageService.CancelScrimmageAsync(scrimmage);

            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                "This scrimmage has been cancelled."));
        }

        [Command("set_scrimmage_channel")]
        [Description("Set the designated scrimmage channel")]
        public async Task SetScrimmageChannel(
            CommandContext context,
            [Description("Text channel to set as scrimmage channel")] DiscordChannel channel)
        {
            await context.DeferResponseAsync();

            if (context.Guild is null)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "This command must be used in a server"));
                return;
            }

            // Check if the user has permission to manage scrimmages
            if (!await _scrimmageService.HasScrimmageAdminPrivilegesAsync(context.User.Id))
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    "You don't have permission to set the scrimmage channel. This requires administrator or moderator privileges."));
                return;
            }

            // Get or create server config
            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild.Id);
            if (server == null)
            {
                server = new BotConfig.ServerConfig { ServerId = context.Guild.Id };
                ConfigManager.Config.Servers.Add(server);
            }

            // Set the channel
            server.ScrimmageChannelId = channel.Id;
            await ConfigManager.SaveConfig();

            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"Scrimmage channel set to {channel.Mention}"));
        }
    }
}