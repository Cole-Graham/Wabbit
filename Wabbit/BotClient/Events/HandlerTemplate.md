# Handler Implementation Template

This document provides templates for creating new component and modal handlers following the project's standardized dependency injection patterns.

## Component Handler Template

```csharp
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.YourNamespace
{
    /// <summary>
    /// Handles [description of what this handler does]
    /// </summary>
    public class YourComponentHandler : ComponentHandlerBase
    {
        // Declare private fields for dependencies
        private readonly IService1 _service1;
        private readonly IService2 _service2;
        
        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="service1">Service for [describe purpose]</param>
        /// <param name="service2">Service for [describe purpose]</param>
        public YourComponentHandler(
            ILogger<YourComponentHandler> logger,
            ITournamentStateService stateService,
            IService1 service1,
            IService2 service2)
            : base(logger, stateService)
        {
            _service1 = service1;
            _service2 = service2;
        }
        
        /// <summary>
        /// Determines if this handler can handle the component interaction
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle this component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            // Determine if this handler should handle components with this ID
            return customId.StartsWith("your_component_prefix_");
        }
        
        /// <summary>
        /// Handles the component interaction
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreateEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling component interaction from {User}", e.User.Username);
                
                // Implement your handling logic here
                
                // Example of sending a success response
                await SendResponseAsync(e, "Operation successful", hasBeenDeferred, DiscordColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {HandlerName}", nameof(YourComponentHandler));
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}
```

## Modal Handler Template

```csharp
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Modals.Base;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Modals.YourNamespace
{
    /// <summary>
    /// Handles [description of what this handler does]
    /// </summary>
    public class YourModalHandler : ModalHandlerBase
    {
        // Declare private fields for dependencies
        private readonly ITournamentStateService _stateService;
        private readonly IService1 _service1;
        private readonly IService2 _service2;
        
        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="service1">Service for [describe purpose]</param>
        /// <param name="service2">Service for [describe purpose]</param>
        public YourModalHandler(
            ILogger<YourModalHandler> logger,
            ITournamentStateService stateService,
            IService1 service1,
            IService2 service2)
            : base(logger)
        {
            _stateService = stateService;
            _service1 = service1;
            _service2 = service2;
        }
        
        /// <summary>
        /// Determines if this handler can handle the modal submission
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>True if this handler can handle this modal, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            // Determine if this handler should handle modals with this ID
            return customId.StartsWith("your_modal_prefix_");
        }
        
        /// <summary>
        /// Handles the modal submission
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling modal submission from {User}", e.Interaction.User.Username);
                
                // Extract values from the modal
                if (!e.Values.TryGetValue("field_name", out var fieldValue))
                {
                    await SendErrorResponseAsync(e, "Required field not found in the modal submission.", hasBeenDeferred);
                    return;
                }
                
                // Implement your handling logic here
                
                // Save state changes
                await _stateService.SaveTournamentStateAsync(client);
                
                // Example of sending a success response
                await SendResponseAsync(e, "Modal processed successfully", hasBeenDeferred, DiscordColor.Green);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {HandlerName}", nameof(YourModalHandler));
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}
```

## Service Registration

After creating your handler, register it in `Program.cs`:

### Component Handler Registration
```csharp
// In the ConfigureServices method
services.AddSingleton<ComponentHandlerBase, YourComponentHandler>();
```

### Modal Handler Registration
```csharp
// In the ConfigureServices method
services.AddSingleton<BotClient.Events.Modals.Base.ModalHandlerBase, BotClient.Events.Modals.YourNamespace.YourModalHandler>();
```

## Best Practices

1. Follow the standard parameter ordering in constructors
2. Use proper XML documentation for all classes and methods
3. Add error handling with detailed logging
4. Use dependency injection for all external dependencies
5. Register handlers as singletons
6. Implement clear and specific CanHandle logic 