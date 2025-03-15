using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Wabbit.BotClient.Events.Modals.Base;
using Wabbit.BotClient.Events.Modals.Tournament;

namespace Wabbit.BotClient.Events.Modals.Factory
{
    /// <summary>
    /// Factory for creating modal handlers based on the modal's custom ID
    /// </summary>
    public class ModalHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ModalHandlerFactory> _logger;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="serviceProvider">Service provider for resolving dependencies</param>
        /// <param name="logger">Logger for logging events</param>
        public ModalHandlerFactory(IServiceProvider serviceProvider, ILogger<ModalHandlerFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Creates the appropriate handler for the given modal
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>A modal handler that can handle the modal</returns>
        public ModalHandlerBase CreateHandler(string customId)
        {
            try
            {
                // Get all registered handlers
                using var scope = _serviceProvider.CreateScope();
                var handlers = scope.ServiceProvider.GetServices<ModalHandlerBase>();

                // Find the first handler that can handle this modal
                var handler = handlers?.FirstOrDefault(h => h.CanHandle(customId));

                if (handler != null)
                {
                    _logger.LogDebug("Found handler {HandlerType} for modal with ID: {CustomId}",
                        handler.GetType().Name, customId);
                    return handler;
                }

                // Fall back to default handler if no specific handler found
                _logger.LogWarning("No specific handler found for modal with ID: {CustomId}, using default handler", customId);
                return scope.ServiceProvider.GetRequiredService<DefaultModalHandler>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating handler for modal with ID: {CustomId}", customId);

                // If anything goes wrong, return a new default handler
                return _serviceProvider.GetRequiredService<DefaultModalHandler>();
            }
        }
    }
}