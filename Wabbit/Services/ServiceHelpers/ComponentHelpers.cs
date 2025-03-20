using DSharpPlus.Entities;
using System.Collections.Generic;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Helper methods for creating standardized UI components across tournament and scrimmage systems
    /// </summary>
    public static class ComponentHelpers
    {
        /// <summary>
        /// Creates a standard refresh button
        /// </summary>
        /// <param name="uniqueId">A unique identifier for the component</param>
        /// <returns>A refresh button component</returns>
        public static DiscordButtonComponent CreateRefreshButton(string uniqueId)
        {
            return new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"refresh_status_{uniqueId}",
                "Refresh Status",
                emoji: new DiscordComponentEmoji("🔄"));
        }

        /// <summary>
        /// Creates a standard ready up button
        /// </summary>
        /// <param name="uniqueId">A unique identifier for the component</param>
        /// <returns>A ready up button component</returns>
        public static DiscordButtonComponent CreateReadyButton(string uniqueId)
        {
            return new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                $"ready_up_{uniqueId}",
                "Ready Up",
                emoji: new DiscordComponentEmoji("✅"));
        }

        /// <summary>
        /// Creates a standard submit deck button
        /// </summary>
        /// <param name="uniqueId">A unique identifier for the component</param>
        /// <returns>A submit deck button component</returns>
        public static DiscordButtonComponent CreateSubmitDeckButton(string uniqueId)
        {
            return new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                $"submit_deck_{uniqueId}",
                "Submit Deck");
        }

        /// <summary>
        /// Creates a standard confirm or revise deck submission button pair
        /// </summary>
        /// <param name="playerId">Discord ID of the player submitting the deck</param>
        /// <returns>A list containing confirm and revise buttons</returns>
        public static List<DiscordComponent> CreateDeckConfirmButtons(ulong playerId)
        {
            var buttons = new List<DiscordComponent>();

            var confirmButton = new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                $"confirm_deck_{playerId}",
                "Confirm Deck");

            var reviseButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"revise_deck_{playerId}",
                "Revise Deck");

            buttons.Add(confirmButton);
            buttons.Add(reviseButton);

            return buttons;
        }

        /// <summary>
        /// Creates a standard replay submission button
        /// </summary>
        /// <param name="gameNumber">The game number (0-based index)</param>
        /// <param name="uniqueId">A unique identifier for the component</param>
        /// <returns>A replay submission button component</returns>
        public static DiscordButtonComponent CreateReplaySubmissionButton(int gameNumber, string uniqueId)
        {
            return new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"submit_replay_{gameNumber}_{uniqueId}",
                "Submit Replay File",
                emoji: new DiscordComponentEmoji("📁"));
        }
    }
}