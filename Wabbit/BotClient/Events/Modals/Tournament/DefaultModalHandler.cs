using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Modals.Base;

namespace Wabbit.BotClient.Events.Modals.Tournament
{
    /// <summary>
    /// Default handler for modal submissions that don't match any specific handler
    /// </summary>
    public class DefaultModalHandler : ModalHandlerBase
    {
        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        public DefaultModalHandler(ILogger<DefaultModalHandler> logger) : base(logger)
        {
        }

        /// <summary>
        /// This handler can handle any modal if no other handler is found
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>Always returns false - this is a fallback handler</returns>
        public override bool CanHandle(string customId)
        {
            // This is a fallback handler, so it doesn't explicitly handle any modals
            // It will be used if no other handler is found
            return false;
        }

        /// <summary>
        /// Handles modal submissions with a generic response
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred)
        {
            _logger.LogWarning("Using default handler for modal with ID: {ModalId}", e.Interaction.Data.CustomId);

            try
            {
                var message = $"No handler found for modal with ID: {e.Interaction.Data.CustomId}. This modal may be from an older version of the bot or is not currently supported.";

                await SendResponseAsync(e,
                    message,
                    hasBeenDeferred,
                    DiscordColor.Yellow);

                // Log all submitted values for debugging
                foreach (var value in e.Values)
                {
                    _logger.LogInformation("Modal value - Key: {Key}, Value: {Value}", value.Key, value.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DefaultModalHandler");
                await SendErrorResponseAsync(e, "An error occurred while processing your submission.", hasBeenDeferred);
            }
        }
    }
}