# Dependency Injection Standards for Wabbit

This document outlines the standardized dependency injection patterns used throughout the Wabbit project, particularly for event handlers and services.

## Core Principles

1. **Interface-Based Injection**: Always inject dependencies via interfaces instead of concrete types
2. **Constructor Injection**: Use constructor injection for all dependencies
3. **XML Documentation**: Document all constructor parameters with XML comments
4. **Singleton Pattern**: Register services as singletons for state sharing
5. **Consistent Parameter Order**: Maintain consistent parameter ordering in constructors

## Standard Parameter Ordering

For consistent code organization, constructor parameters should follow this ordering:

1. **Logger** (`ILogger<T>`)
2. **State services** (`ITournamentStateService`, etc.)
3. **Core data holders** (`OngoingRounds`)
4. **Service interfaces** (alphabetically by interface name)
5. **Factory services** (`IServiceScopeFactory`, etc.)

## Handler Registration

All handlers follow a consistent registration pattern in `Program.cs`:

```csharp
// Component Handlers
services.AddSingleton<ComponentHandlerFactory>();
services.AddSingleton<DefaultComponentHandler>();
services.AddSingleton<ComponentHandlerBase, SpecificHandler>(); // Register each handler
services.AddSingleton<ComponentInteractionHandler>(); // Main interaction handler

// Modal Handlers
services.AddSingleton<ModalHandlerFactory>();
services.AddSingleton<DefaultModalHandler>();
services.AddSingleton<ModalHandlerBase, SpecificModalHandler>(); // Register each handler
services.AddSingleton<ModalInteractionHandler>(); // Main interaction handler
```

## Component Handler Pattern

Component handlers must:

1. Inherit from `ComponentHandlerBase`
2. Include `ILogger<T>` and `ITournamentStateService` in the constructor (passed to base)
3. Implement a constructor with required dependencies
4. Include XML documentation for the class and constructor parameters

Example:
```csharp
/// <summary>
/// Handles specific component interactions
/// </summary>
public class SpecificHandler : ComponentHandlerBase
{
    private readonly IDependency1 _dependency1;
    private readonly IDependency2 _dependency2;

    /// <summary>
    /// Constructor with required dependencies
    /// </summary>
    /// <param name="logger">Logger for logging events</param>
    /// <param name="stateService">Service for accessing tournament state</param>
    /// <param name="dependency1">Description of dependency1</param>
    /// <param name="dependency2">Description of dependency2</param>
    public SpecificHandler(
        ILogger<SpecificHandler> logger,
        ITournamentStateService stateService,
        IDependency1 dependency1,
        IDependency2 dependency2)
        : base(logger, stateService)
    {
        _dependency1 = dependency1;
        _dependency2 = dependency2;
    }
}
```

## Modal Handler Pattern

Modal handlers must:

1. Inherit from `ModalHandlerBase`
2. Include `ILogger<T>` in the constructor (passed to base)
3. Implement a constructor with required dependencies
4. Include XML documentation for the class and constructor parameters

Example:
```csharp
/// <summary>
/// Handles specific modal submissions
/// </summary>
public class SpecificModalHandler : ModalHandlerBase
{
    private readonly IDependency1 _dependency1;
    private readonly IDependency2 _dependency2;
    private readonly ITournamentStateService _stateService;

    /// <summary>
    /// Constructor with required dependencies
    /// </summary>
    /// <param name="logger">Logger for logging events</param>
    /// <param name="dependency1">Description of dependency1</param>
    /// <param name="dependency2">Description of dependency2</param>
    /// <param name="stateService">Service for accessing tournament state</param>
    public SpecificModalHandler(
        ILogger<SpecificModalHandler> logger,
        IDependency1 dependency1,
        IDependency2 dependency2,
        ITournamentStateService stateService)
        : base(logger)
    {
        _dependency1 = dependency1;
        _dependency2 = dependency2;
        _stateService = stateService;
    }
}
```

## Factory Pattern

For complex service creation, we use the factory pattern:

1. **Handler Factories**: `ComponentHandlerFactory` and `ModalHandlerFactory` determine which handler to use
2. **Scope Factory**: `IServiceScopeFactory` allows creating scoped services when needed
3. **Service Registration**: All services are registered in `Program.cs`

## Benefits of Standardized DI

1. **Consistent Code Structure**: Developers know where to find dependencies
2. **Better Testability**: Interfaces allow for mocking dependencies during testing
3. **Loose Coupling**: Components depend on abstractions, not concrete implementations
4. **Documentation**: Constructor parameters are well-documented
5. **Discoverability**: New developers can quickly understand component dependencies

This standardization ensures that all parts of the Wabbit application follow consistent patterns, making the code more maintainable and easier to extend. 