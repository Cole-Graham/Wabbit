using System;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;
using Wabbit.Models;

namespace Wabbit.BotClient.Extensions
{
    /// <summary>
    /// Extension methods for CommandContext to standardize common operations
    /// </summary>
    public static class CommandContextExtensions
    {
        /// <summary>
        /// Responds with a standardized error message
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="message">The error message</param>
        public static Task RespondErrorAsync(this CommandContext context, string message)
        {
            return context.RespondAsync(new DiscordMessageBuilder()
                .WithContent($"❌ {message}")).AsTask();
        }

        /// <summary>
        /// Responds with a standardized success message
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="message">The success message</param>
        public static Task RespondSuccessAsync(this CommandContext context, string message)
        {
            return context.RespondAsync(new DiscordMessageBuilder()
                .WithContent($"✅ {message}")).AsTask();
        }

        /// <summary>
        /// Updates a deferred response with a standardized error message
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="message">The error message</param>
        public static async Task<DiscordMessage> EditResponseErrorAsync(this CommandContext context, string message)
        {
            var response = await context.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"❌ {message}"));
            return response;
        }

        /// <summary>
        /// Updates a deferred response with a standardized success message
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="message">The success message</param>
        public static async Task<DiscordMessage> EditResponseSuccessAsync(this CommandContext context, string message)
        {
            var response = await context.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"✅ {message}"));
            return response;
        }

        /// <summary>
        /// Gets a team by name using the team service
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="teamName">The team name to lookup</param>
        /// <returns>The team or null if not found</returns>
        public static async Task<Team?> GetTeamByNameAsync(this CommandContext context, string teamName)
        {
            var teamService = context.ServiceProvider.GetRequiredService<ITeamService>();
            return await teamService.GetTeamByNameAsync(teamName);
        }

        /// <summary>
        /// Gets a team by ID using the team service
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="teamId">The team ID to lookup</param>
        /// <returns>The team or null if not found</returns>
        public static async Task<Team?> GetTeamByIdAsync(this CommandContext context, string teamId)
        {
            var teamService = context.ServiceProvider.GetRequiredService<ITeamService>();
            return await teamService.GetTeamByIdAsync(teamId);
        }

        /// <summary>
        /// Creates a standard team info embed
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="team">The team to display information for</param>
        /// <returns>A configured embed builder</returns>
        public static DiscordEmbedBuilder CreateTeamInfoEmbed(this CommandContext context, Team team)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"Team: {team.TeamName}")
                .WithColor(DiscordColor.Blue)
                .AddField("Type", GetGameTypeDisplayName((GameType)team.GameType), true)
                .AddField("Rating", team.Rating.ToString(), true)
                .AddField("Created", team.CreatedAt.ToString("MMM d, yyyy"), true)
                .AddField("Record", $"{team.Wins}-{team.Losses} ({GetWinRate(team.Wins, team.Losses)}%)", true);

            return embed;
        }

        private static string GetGameTypeDisplayName(GameType gameType)
        {
            return GameTypeHelpers.GetDisplayName(gameType);
        }

        private static int GetWinRate(int wins, int losses)
        {
            if (wins + losses == 0) return 0;
            return (int)Math.Round((double)wins / (wins + losses) * 100);
        }
    }
}