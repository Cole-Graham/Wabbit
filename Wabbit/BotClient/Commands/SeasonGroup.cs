using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Commands
{
    [SlashCommandGroup("season", "Commands to manage competitive seasons")]
    public class SeasonGroup : BaseCommandGroup
    {
        private readonly ILogger<SeasonGroup> _logger;
        private readonly ISeasonStateService _seasonStateService;
        private readonly ILeaderboardService _leaderboardService;

        public SeasonGroup(
            ILogger<SeasonGroup> logger,
            ISeasonStateService seasonStateService,
            ILeaderboardService leaderboardService)
        {
            _logger = logger;
            _seasonStateService = seasonStateService;
            _leaderboardService = leaderboardService;
        }

        [SlashCommand("current", "Shows information about the current season")]
        public async Task CurrentSeasonAsync(CommandContext ctx)
        {
            await ctx.DeferResponseAsync();

            try
            {
                var season = await _seasonStateService.GetCurrentSeasonAsync();

                if (season == null)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("There is no active season currently. Use `/season start` to start a new season."));
                    return;
                }

                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Season: {season.SeasonName}")
                    .WithDescription(season.Description)
                    .WithColor(DiscordColor.Green)
                    .AddField("Started", $"{season.StartDate:MMM d, yyyy}", true)
                    .AddField("End Date", season.EndDate.HasValue ? $"{season.EndDate:MMM d, yyyy}" : "No end date set", true)
                    .AddField("Duration", season.GetFormattedDuration(), true)
                    .AddField("Created By", season.CreatorUsername, true)
                    .WithFooter($"Season ID: {season.SeasonId}")
                    .WithTimestamp(DateTime.UtcNow);

                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current season");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while getting the current season information."));
            }
        }

        [SlashCommand("start", "Start a new competitive season")]
        public async Task StartSeasonAsync(
            CommandContext ctx,
            [Option("name", "Name for the new season")] string name,
            [Option("description", "Description of the season")] string description,
            [Option("end_date", "When the season should end (format: MM/DD/YYYY)")] string endDateStr)
        {
            await ctx.DeferResponseAsync();

            try
            {
                // Check if user has admin privileges
                if (!await _seasonStateService.HasSeasonAdminPrivilegesAsync(ctx.User.Id))
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ You don't have permission to manage seasons."));
                    return;
                }

                // Parse end date
                if (!DateTime.TryParse(endDateStr, out DateTime endDate))
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ Invalid date format. Please use MM/DD/YYYY format."));
                    return;
                }

                // Check if date is in the future
                if (endDate <= DateTime.UtcNow)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ End date must be in the future."));
                    return;
                }

                // Check if there's already an active season
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                if (currentSeason != null)
                {
                    var confirmMessage = await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"⚠️ There is already an active season: **{currentSeason.SeasonName}**. Do you want to end it and start a new one?")
                        .AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, "end_current_season", "End Current Season"),
                            new DiscordButtonComponent(ButtonStyle.Secondary, "cancel_season_start", "Cancel")));

                    // We'll handle this in the component handler
                    return;
                }

                // Start a new season
                var season = await _seasonStateService.StartNewSeasonAsync(name, description, endDate, ctx.User);

                // Create embed for response
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 New Season Started: {season.SeasonName}")
                    .WithDescription(season.Description)
                    .WithColor(DiscordColor.Green)
                    .AddField("Started", $"{season.StartDate:MMM d, yyyy}", true)
                    .AddField("Ends", $"{season.EndDate:MMM d, yyyy}", true)
                    .AddField("Created By", ctx.User.Username, true)
                    .WithFooter($"Season ID: {season.SeasonId}")
                    .WithTimestamp(DateTime.UtcNow);

                var message = await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().AddEmbed(embed));

                // Record the announcement message
                await _seasonStateService.AddSeasonMessageAsync(season.SeasonId, ctx.Channel, message, SeasonMessageType.SeasonStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting new season");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while starting the new season."));
            }
        }

        [SlashCommand("end", "End the current competitive season")]
        public async Task EndSeasonAsync(CommandContext ctx)
        {
            await ctx.DeferResponseAsync();

            try
            {
                // Check if user has admin privileges
                if (!await _seasonStateService.HasSeasonAdminPrivilegesAsync(ctx.User.Id))
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ You don't have permission to manage seasons."));
                    return;
                }

                // Check if there is an active season
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                if (currentSeason == null)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ There is no active season to end."));
                    return;
                }

                // Show confirmation with buttons
                var confirmMessage = await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent($"⚠️ Are you sure you want to end the current season: **{currentSeason.SeasonName}**? This will finalize all rankings and cannot be undone.")
                    .AddComponents(new DiscordButtonComponent(ButtonStyle.Danger, "confirm_end_season", "End Season"),
                        new DiscordButtonComponent(ButtonStyle.Secondary, "cancel_end_season", "Cancel")));

                // Component handler will handle the button interactions
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending season");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while trying to end the season."));
            }
        }

        [SlashCommand("list", "List past seasons")]
        public async Task ListSeasonsAsync(
            CommandContext ctx,
            [Option("count", "Number of past seasons to show")] long count = 5)
        {
            await ctx.DeferResponseAsync();

            try
            {
                // Get past seasons
                var pastSeasons = await _seasonStateService.GetPastSeasonsAsync((int)count);

                if (pastSeasons.Count == 0)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("There are no past seasons to display."));
                    return;
                }

                // Create embed for past seasons
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Past Competitive Seasons")
                    .WithColor(DiscordColor.Blue)
                    .WithTimestamp(DateTime.UtcNow);

                foreach (var season in pastSeasons)
                {
                    embed.AddField($"{season.SeasonName}",
                        $"**Duration:** {season.GetFormattedDuration()}\n" +
                        $"**Start:** {season.StartDate:MMM d, yyyy}\n" +
                        $"**End:** {season.EndDate:MMM d, yyyy}\n" +
                        $"**ID:** {season.SeasonId}");
                }

                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing past seasons");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while listing past seasons."));
            }
        }

        [SlashCommand("leaderboard", "View the current season leaderboard")]
        public async Task LeaderboardAsync(
            CommandContext ctx,
            [Option("game_type", "Game type to show leaderboard for")] GameType gameType = GameType.OneVOne,
            [Option("count", "Number of top players to show")] long count = 10)
        {
            await ctx.DeferResponseAsync();

            try
            {
                // Check if there is an active season
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                if (currentSeason == null)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("There is no active season currently. Use `/season start` to start a new season."));
                    return;
                }

                // Get current top rankings
                var rankings = await _seasonStateService.PreviewSeasonFinalRankingsAsync(gameType, (int)count);

                if (rankings.Count == 0)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"No rankings found for {gameType} games this season."));
                    return;
                }

                // Create embed for leaderboard
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"{currentSeason.SeasonName} - {gameType} Leaderboard")
                    .WithDescription($"Current rankings as of {DateTime.UtcNow:MMM d, yyyy}")
                    .WithColor(DiscordColor.Gold)
                    .WithFooter("These rankings will be finalized when the season ends")
                    .WithTimestamp(DateTime.UtcNow);

                // Add fields for each player/team
                for (int i = 0; i < rankings.Count; i++)
                {
                    var ranking = rankings[i];
                    string name;

                    if (ranking.IsTeam)
                    {
                        name = ranking.TeamName ?? "Unknown Team";
                    }
                    else
                    {
                        name = ranking.PlayerUsername ?? "Unknown Player";
                    }

                    string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : "";
                    string rankText = medal.Length > 0 ? medal : $"#{i + 1}";

                    embed.AddField($"{rankText} {name}",
                        $"**Rating:** {ranking.Rating}\n" +
                        $"**W/L:** {ranking.Wins}-{ranking.Losses} ({ranking.WinRate:F1}%)\n" +
                        $"**Matches:** {ranking.MatchesPlayed}");
                }

                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing season leaderboard");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while generating the leaderboard."));
            }
        }

        [SlashCommand("update", "Update the end date of the current season")]
        public async Task UpdateSeasonEndDateAsync(
            CommandContext ctx,
            [Option("end_date", "New end date for the season (format: MM/DD/YYYY)")] string endDateStr)
        {
            await ctx.DeferResponseAsync();

            try
            {
                // Check if user has admin privileges
                if (!await _seasonStateService.HasSeasonAdminPrivilegesAsync(ctx.User.Id))
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ You don't have permission to manage seasons."));
                    return;
                }

                // Parse end date
                if (!DateTime.TryParse(endDateStr, out DateTime endDate))
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ Invalid date format. Please use MM/DD/YYYY format."));
                    return;
                }

                // Check if date is in the future
                if (endDate <= DateTime.UtcNow)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ End date must be in the future."));
                    return;
                }

                // Check if there is an active season
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                if (currentSeason == null)
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ There is no active season to update."));
                    return;
                }

                // Update the season end date
                var success = await _seasonStateService.UpdateSeasonEndDateAsync(currentSeason.SeasonId, endDate, ctx.User);

                if (success)
                {
                    // Re-fetch the season to get updated info
                    currentSeason = await _seasonStateService.GetCurrentSeasonAsync();

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"Season Updated: {currentSeason?.SeasonName}")
                        .WithColor(DiscordColor.Green)
                        .AddField("New End Date", $"{currentSeason?.EndDate:MMM d, yyyy}", true)
                        .AddField("Modified By", ctx.User.Username, true)
                        .WithTimestamp(DateTime.UtcNow);

                    var message = await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().AddEmbed(embed));

                    // Record the update message
                    if (currentSeason != null)
                    {
                        await _seasonStateService.AddSeasonMessageAsync(currentSeason.SeasonId, ctx.Channel, message, SeasonMessageType.Update);
                    }
                }
                else
                {
                    await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("❌ Failed to update the season end date."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating season end date");
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder()
                    .WithContent("❌ An error occurred while updating the season end date."));
            }
        }
    }
}