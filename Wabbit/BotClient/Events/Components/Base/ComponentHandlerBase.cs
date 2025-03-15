using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Base
{
    /// <summary>
    /// Base abstract class for component interaction handlers
    /// </summary>
    public abstract class ComponentHandlerBase
    {
        protected readonly ILogger _logger;
        protected readonly ITournamentStateService _stateService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        public ComponentHandlerBase(ILogger logger, ITournamentStateService stateService)
        {
            _logger = logger;
            _stateService = stateService;
        }

        /// <summary>
        /// Safely defers an interaction to prevent timeout errors
        /// </summary>
        /// <param name="interaction">The interaction to defer</param>
        protected async Task SafeDeferAsync(DiscordInteraction interaction)
        {
            try
            {
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to defer interaction");
                // Already deferred or cannot defer
            }
        }

        /// <summary>
        /// Automatically deletes a message after a specified delay
        /// </summary>
        /// <param name="message">The message to delete</param>
        /// <param name="seconds">The delay in seconds before deletion</param>
        protected Task AutoDeleteMessageAsync(DiscordMessage message, int seconds)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    await message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-delete message");
                }
            });
        }

        /// <summary>
        /// Sends an error response to the user
        /// </summary>
        /// <param name="e">The interaction event args</param>
        /// <param name="message">The error message to send</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        protected async Task SendErrorResponseAsync(ComponentInteractionCreatedEventArgs e, string message, bool hasBeenDeferred)
        {
            // Call the overload with the default error color
            await SendResponseAsync(e, message, hasBeenDeferred, DiscordColor.Red);
        }

        /// <summary>
        /// Sends a response to the user with a specified color
        /// </summary>
        /// <param name="e">The interaction event args</param>
        /// <param name="message">The message to send</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        /// <param name="color">The color for the response embed</param>
        protected async Task SendResponseAsync(ComponentInteractionCreatedEventArgs e, string message, bool hasBeenDeferred, DiscordColor color)
        {
            try
            {
                // Determine if this is an error message based on the color
                bool isError = color.Value == DiscordColor.Red.Value;

                var embed = new DiscordEmbedBuilder()
                    .WithTitle(isError ? "Error" : "Success")
                    .WithDescription(message)
                    .WithColor(color);

                // Send error response
                if (hasBeenDeferred)
                {
                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral()
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send response");
            }
        }

        /// <summary>
        /// Abstract method that all handlers must implement to handle component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public abstract Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred);

        /// <summary>
        /// Method to determine if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public abstract bool CanHandle(string customId);
    }
}