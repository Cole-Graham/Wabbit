using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Wabbit.BotClient.Events.Components.Base;

namespace Wabbit.BotClient.Events.Components.Factory
{
    /// <summary>
    /// Factory for creating component handlers based on the component's custom ID
    /// </summary>
    public class ComponentHandlerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ComponentHandlerFactory> _logger;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="serviceProvider">Service provider for resolving dependencies</param>
        /// <param name="logger">Logger for logging events</param>
        public ComponentHandlerFactory(IServiceProvider serviceProvider, ILogger<ComponentHandlerFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Creates the appropriate handler for the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>A component handler that can handle the component</returns>
        public ComponentHandlerBase CreateHandler(string customId)
        {
            try
            {
                // Get all registered handlers
                using var scope = _serviceProvider.CreateScope();
                var handlers = scope.ServiceProvider.GetServices<ComponentHandlerBase>();

                // Find the first handler that can handle this component
                var handler = handlers?.FirstOrDefault(h => h.CanHandle(customId));

                if (handler != null)
                {
                    _logger.LogDebug("Found handler {HandlerType} for component with ID: {CustomId}",
                        handler.GetType().Name, customId);
                    return handler;
                }

                // Fall back to default handler if no specific handler found
                _logger.LogWarning("No specific handler found for component with ID: {CustomId}, using default handler", customId);
                return scope.ServiceProvider.GetRequiredService<DefaultComponentHandler>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating handler for component with ID: {CustomId}", customId);

                // If anything goes wrong, return a new default handler
                return _serviceProvider.GetRequiredService<DefaultComponentHandler>();
            }
        }
    }
}