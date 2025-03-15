using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Wabbit.BotClient.Events.Modals.Base
{
    /// <summary>
    /// Base abstract class for modal submission handlers
    /// </summary>
    public abstract class ModalHandlerBase
    {
        protected readonly ILogger _logger;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        protected ModalHandlerBase(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Safely defers an interaction to prevent timeout errors
        /// </summary>
        /// <param name="interaction">The interaction to defer</param>
        protected async Task SafeDeferAsync(DiscordInteraction interaction)
        {
            try
            {
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredChannelMessageWithSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to defer modal interaction");
                // Already deferred or cannot defer
            }
        }

        /// <summary>
        /// Sends an error response to the user
        /// </summary>
        /// <param name="e">The modal submission event args</param>
        /// <param name="message">The error message to send</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        protected async Task SendErrorResponseAsync(ModalSubmittedEventArgs e, string message, bool hasBeenDeferred)
        {
            // Call the overload with the default error color
            await SendResponseAsync(e, message, hasBeenDeferred, DiscordColor.Red);
        }

        /// <summary>
        /// Sends a response to the user with a specified color
        /// </summary>
        /// <param name="e">The modal submission event args</param>
        /// <param name="message">The message to send</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        /// <param name="color">The color for the response embed</param>
        protected async Task SendResponseAsync(ModalSubmittedEventArgs e, string message, bool hasBeenDeferred, DiscordColor color)
        {
            try
            {
                // Determine if this is an error message based on the color
                bool isError = color.Value == DiscordColor.Red.Value;

                var embed = new DiscordEmbedBuilder()
                    .WithTitle(isError ? "Error" : "Success")
                    .WithDescription(message)
                    .WithColor(color);

                if (hasBeenDeferred)
                {
                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send modal response");
            }
        }

        /// <summary>
        /// Abstract method that all handlers must implement to handle modal submissions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public abstract Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred);

        /// <summary>
        /// Method to determine if this handler can handle the given modal
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>True if this handler can handle the modal, false otherwise</returns>
        public abstract bool CanHandle(string customId);
    }
}