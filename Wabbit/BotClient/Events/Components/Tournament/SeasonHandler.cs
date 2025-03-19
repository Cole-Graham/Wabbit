using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles season-related component interactions
    /// </summary>
    public class SeasonHandler : ComponentHandlerBase
    {
        private readonly ISeasonStateService _seasonStateService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="seasonStateService">Service for managing seasons</param>
        public SeasonHandler(
            ILogger<SeasonHandler> logger,
            ITournamentStateService stateService,
            ISeasonStateService seasonStateService)
            : base(logger, stateService)
        {
            _seasonStateService = seasonStateService;
        }

        /// <inheritdoc />
        public override bool CanHandle(string customId)
        {
            return customId == "confirm_end_season" ||
                   customId == "cancel_end_season" ||
                   customId == "end_current_season" ||
                   customId == "cancel_season_start";
        }

        /// <inheritdoc />
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            var customId = e.Id;
            var user = e.User;

            try
            {
                if (customId == "confirm_end_season")
                {
                    await HandleConfirmEndSeasonAsync(e);
                }
                else if (customId == "cancel_end_season")
                {
                    await HandleCancelEndSeasonAsync(e);
                }
                else if (customId == "end_current_season")
                {
                    await HandleEndCurrentSeasonAsync(e);
                }
                else if (customId == "cancel_season_start")
                {
                    await HandleCancelSeasonStartAsync(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling season component interaction: {CustomId}", customId);

                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder()
                        .WithContent($"❌ An error occurred: {ex.Message}")
                );
            }
        }


        private async Task HandleConfirmEndSeasonAsync(ComponentInteractionCreatedEventArgs e)
        {
            // Check if user has admin privileges
            if (!await _seasonStateService.HasSeasonAdminPrivilegesAsync(e.User.Id))
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ You don't have permission to end seasons.")
                );
                return;
            }

            // End the current season
            var endedSeason = await _seasonStateService.EndCurrentSeasonAsync();
            if (endedSeason == null)
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ Failed to end the season. There might not be an active season.")
                );
                return;
            }

            // Create success message
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"Season Ended: {endedSeason.SeasonName}")
                .WithDescription($"The season has been successfully ended by {e.User.Username}.")
                .WithColor(DiscordColor.Orange)
                .AddField("Started", $"{endedSeason.StartDate:MMM d, yyyy}", true)
                .AddField("Ended", $"{endedSeason.EndDate:MMM d, yyyy}", true)
                .AddField("Duration", endedSeason.GetFormattedDuration(), true)
                .WithFooter($"Season ID: {endedSeason.SeasonId}")
                .WithTimestamp(DateTimeOffset.UtcNow);

            await e.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder().AddEmbed(embed)
            );

            // Announce the season end in the channel
            var announcement = new DiscordMessageBuilder()
                .WithContent($"🏆 Season **{endedSeason.SeasonName}** has ended!")
                .AddEmbed(embed);

            var channel = e.Channel;
            var message = await channel.SendMessageAsync(announcement);

            // Record the announcement message
            await _seasonStateService.AddSeasonMessageAsync(endedSeason.SeasonId, channel, message, SeasonMessageType.SeasonEnd);
        }

        private async Task HandleCancelEndSeasonAsync(ComponentInteractionCreatedEventArgs e)
        {
            await e.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Season end operation canceled.")
            );
        }

        private async Task HandleEndCurrentSeasonAsync(ComponentInteractionCreatedEventArgs e)
        {
            // Check if user has admin privileges
            if (!await _seasonStateService.HasSeasonAdminPrivilegesAsync(e.User.Id))
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ You don't have permission to manage seasons.")
                );
                return;
            }

            // Get current season
            var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
            if (currentSeason == null)
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ No active season found.")
                );
                return;
            }

            // Show confirmation with buttons
            var confirmBuilder = new DiscordInteractionResponseBuilder()
                .WithContent($"⚠️ Are you sure you want to end the current season: **{currentSeason.SeasonName}**? This will finalize all rankings and cannot be undone.")
                .AddComponents(
                    new DiscordButtonComponent(DiscordButtonStyle.Danger, "confirm_end_season", "End Season"),
                    new DiscordButtonComponent(DiscordButtonStyle.Secondary, "cancel_end_season", "Cancel")
                );

            await e.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.UpdateMessage,
                confirmBuilder
            );
        }

        private async Task HandleCancelSeasonStartAsync(ComponentInteractionCreatedEventArgs e)
        {
            await e.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder()
                    .WithContent("Season start operation canceled.")
            );
        }
    }
}