using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Factory;

namespace Wabbit.BotClient.Events.MainHandlers
{
    /// <summary>
    /// Main handler for component interactions using the factory pattern.
    /// This class delegates component handling to specialized handlers based on the component ID.
    /// </summary>
    public class ComponentInteractionHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
    {
        private readonly ComponentHandlerFactory _factory;
        private readonly ILogger<ComponentInteractionHandler> _logger;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="factory">Factory for creating component handlers</param>
        /// <param name="logger">Logger for logging events</param>
        public ComponentInteractionHandler(ComponentHandlerFactory factory, ILogger<ComponentInteractionHandler> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        /// <summary>
        /// Handles component interaction events by delegating to specialized handlers
        /// </summary>
        /// <param name="sender">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        public Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
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
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to defer interaction or already deferred");
                        // Already deferred or cannot defer
                    }

                    _logger.LogInformation(
                        "Handling component interaction: User={User}, Component={Component}, Channel={Channel}",
                        e.User.Username,
                        e.Id,
                        e.Channel.Name);

                    // Create the appropriate handler and let it process the event
                    var handler = _factory.CreateHandler(e.Id);
                    await handler.HandleAsync(sender, e, hasBeenDeferred);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in component handler");

                    // Generic error handling
                    try
                    {
                        if (hasBeenDeferred)
                        {
                            await e.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().WithContent(
                                    "An error occurred while processing your interaction. Please try again or contact an administrator."));
                        }
                        else
                        {
                            await e.Interaction.CreateResponseAsync(
                                DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().WithContent(
                                    "An error occurred while processing your interaction. Please try again or contact an administrator.").AsEphemeral());
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