# Service Provider Factory Pattern in Wabbit

This document explains the service provider factory pattern used in the Wabbit application, particularly how the component and modal handler factories work with dependency injection.

## Overview

The Wabbit application uses a factory pattern combined with dependency injection to create specialized handlers for different types of Discord interactions. This pattern enables:

1. **Dynamic handler resolution** - Select the right handler at runtime
2. **Encapsulated handler creation** - Hide the complexity of handler creation
3. **Standardized dependency injection** - Ensure consistent DI across all handlers
4. **Improved testability** - Make it easier to mock dependencies for testing

## Implementation Details

### 1. Handler Factory Pattern

Both component interactions and modal submissions use a similar factory pattern:

```csharp
public class HandlerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HandlerFactory> _logger;

    public HandlerFactory(IServiceProvider serviceProvider, ILogger<HandlerFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public HandlerBase CreateHandler(string customId)
    {
        using var scope = _serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<HandlerBase>();
        
        var handler = handlers?.FirstOrDefault(h => h.CanHandle(customId));
        
        if (handler != null)
            return handler;
            
        // Return default handler if no specific handler is found
        return scope.ServiceProvider.GetRequiredService<DefaultHandler>();
    }
}
```

### 2. Service Registration

Handlers are registered in `Program.cs` as singletons:

```csharp
// Base factory and default handler
services.AddSingleton<ComponentHandlerFactory>();
services.AddSingleton<DefaultComponentHandler>();

// Specialized handlers
services.AddSingleton<ComponentHandlerBase, Handler1>();
services.AddSingleton<ComponentHandlerBase, Handler2>();
services.AddSingleton<ComponentHandlerBase, Handler3>();

// Main interaction handler
services.AddSingleton<ComponentInteractionHandler>();
```

### 3. Scoped Service Resolution

When resolving handlers, a new service scope is created to ensure proper disposal of resources:

```csharp
using var scope = _serviceProvider.CreateScope();
var handlers = scope.ServiceProvider.GetServices<HandlerBase>();
```

This ensures that scoped services (if used within handlers) are properly managed.

### 4. Handler Selection

Handler selection is determined by the `CanHandle` method on each handler:

```csharp
public virtual bool CanHandle(string customId)
{
    return customId.StartsWith("specific_prefix_");
}
```

The first handler that returns `true` for a given custom ID is used.

## IServiceScopeFactory Usage

In some complex scenarios, handlers might need to create their own service scopes. For this, the `IServiceScopeFactory` is injected:

```csharp
public class ComplexHandler : HandlerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    
    public ComplexHandler(
        ILogger<ComplexHandler> logger,
        IServiceScopeFactory scopeFactory)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }
    
    public override async Task HandleAsync(...)
    {
        using var scope = _scopeFactory.CreateScope();
        var scopedService = scope.ServiceProvider.GetRequiredService<IScopedService>();
        // Use the scoped service...
    }
}
```

## Benefits of This Approach

1. **Decoupling** - Handlers don't need to know about each other
2. **Single Responsibility** - Each handler focuses on one type of interaction
3. **Extendability** - New handlers can be added without changing existing code
4. **Testability** - Easy to mock dependencies for unit testing
5. **Resource Management** - Proper scoping and disposal of resources

## When to Use IServiceProvider vs. Direct Injection

- **Use direct injection** when:
  - Dependencies are fixed and known at design time
  - The class has a single clear purpose
  - All dependencies are used in most methods

- **Use IServiceProvider or IServiceScopeFactory** when:
  - Dependencies vary based on runtime conditions
  - Only a subset of dependencies is needed for certain operations
  - Creating dependent services with shorter lifetimes than the parent

## Example: Using Service Scope Factory

The `MapBanHandler` uses the `IServiceScopeFactory` for situations where it needs to create services on demand:

```csharp
public class MapBanHandler : ComponentHandlerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    
    public MapBanHandler(
        ILogger<MapBanHandler> logger,
        ITournamentStateService stateService,
        IServiceScopeFactory scopeFactory)
        : base(logger, stateService)
    {
        _scopeFactory = scopeFactory;
    }
    
    private async Task SomeComplexOperation()
    {
        using var scope = _scopeFactory.CreateScope();
        var service1 = scope.ServiceProvider.GetRequiredService<IService1>();
        var service2 = scope.ServiceProvider.GetRequiredService<IService2>();
        
        // Use services...
    }
}
```

This pattern allows for more flexible and efficient resource management in complex operations. 