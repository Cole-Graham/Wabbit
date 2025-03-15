using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Modals.Factory;

namespace Wabbit.BotClient.Events.MainHandlers
{
    /// <summary>
    /// Main handler for modal interactions using the factory pattern.
    /// This class delegates modal handling to specialized handlers based on the modal ID.
    /// </summary>
    public class ModalInteractionHandler : IEventHandler<ModalSubmittedEventArgs>
    {
        private readonly ModalHandlerFactory _factory;
        private readonly ILogger<ModalInteractionHandler> _logger;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="factory">Factory for creating modal handlers</param>
        /// <param name="logger">Logger for logging events</param>
        public ModalInteractionHandler(ModalHandlerFactory factory, ILogger<ModalInteractionHandler> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        /// <summary>
        /// Handles modal submission events by delegating to specialized handlers
        /// </summary>
        /// <param name="sender">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        public Task HandleEventAsync(DiscordClient sender, ModalSubmittedEventArgs e)
        {
            // Run asynchronously to not block the event
            _ = Task.Run(async () =>
            {
                bool hasBeenDeferred = false;

                try
                {
                    // Try to defer the interaction
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredChannelMessageWithSource);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to defer modal interaction or already deferred");
                        // Already deferred or cannot defer
                    }

                    _logger.LogInformation(
                        "Handling modal interaction: User={User}, Modal={Modal}, Channel={Channel}",
                        e.Interaction.User.Username,
                        e.Interaction.Data.CustomId,
                        e.Interaction.Channel.Name);

                    // Create the appropriate handler and let it process the event
                    var handler = _factory.CreateHandler(e.Interaction.Data.CustomId);
                    await handler.HandleAsync(sender, e, hasBeenDeferred);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in modal handler");

                    // Generic error handling
                    try
                    {
                        if (hasBeenDeferred)
                        {
                            await e.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().WithContent(
                                    "An error occurred while processing your submission. Please try again or contact an administrator."));
                        }
                        else
                        {
                            await e.Interaction.CreateResponseAsync(
                                DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().WithContent(
                                    "An error occurred while processing your submission. Please try again or contact an administrator.").AsEphemeral());
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Failed to send error response");
                    }
                }
            });

            return Task.CompletedTask;
        }
    }
}