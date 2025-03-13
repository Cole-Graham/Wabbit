# Tournament Management Refactoring

This document outlines the approach for refactoring the large `TournamentManager.cs` class into smaller, more focused service classes.

## Refactoring Goals

1. Split the 3000-line `TournamentManager.cs` into smaller, more maintainable services
2. Follow SOLID principles, particularly Single Responsibility Principle
3. Implement proper dependency injection with interfaces
4. Make the code more testable
5. Maintain backward compatibility during the transition

## Service Architecture

The refactoring splits tournament functionality into these services:

### Core Services

- `ITournamentManagerService` - Main coordination service (replaces TournamentManager)
- `ITournamentRepositoryService` - Data access and persistence for tournaments
- `ITournamentSignupService` - Handling tournament signups
- `ITournamentStateService` - Managing tournament state and rounds
- `ITournamentGroupService` - Group stage management and player utilities
- `ITournamentPlayoffService` - Playoff bracket management

### Interface and Implementation Files

Interfaces are located in `Services/Interfaces`:
- `ITournamentManagerService.cs`
- `ITournamentRepositoryService.cs`
- `ITournamentSignupService.cs`
- `ITournamentStateService.cs` 
- `ITournamentGroupService.cs`
- `ITournamentPlayoffService.cs`

Implementations are located in `Services`:
- `TournamentManagerService.cs`
- `TournamentRepositoryService.cs`
- `TournamentSignupService.cs`
- `TournamentStateService.cs`
- `TournamentGroupService.cs`
- `TournamentPlayoffService.cs`

## Transition Plan

1. Create all interfaces and service implementations
2. Update dependency registration in Program.cs
3. Initially keep the old TournamentManager for backward compatibility
4. Gradually move commands/interactions to use the new services
5. Remove the legacy TournamentManager when transition is complete

## Implementation Status

- [x] Create interface definitions
- [x] Implement ITournamentRepositoryService
- [x] Implement TournamentSignupService
- [x] Implement TournamentGroupService
- [x] Implement TournamentPlayoffService
- [x] Implement TournamentStateService
- [x] Implement TournamentManagerService
- [x] Update Program.cs to register all services
- [x] Update TournamentManagementGroup to use new services
- [x] Fix compilation errors in TournamentMatchService
- [x] Complete implementation of remaining services
- [x] Update Event_Modal to use new services
- [ ] Add unit tests for services
- [x] Deprecate and remove legacy TournamentManager

## Next Steps

With the refactoring now completed, here are the final steps:

### 1. ✅ Fix Compilation Errors

All compilation errors have been addressed:

- ✅ **TournamentMatchService**: Fixed the return type mismatch in `CreateAndStart1v1Match` method
- ✅ **TournamentMatchService**: Implemented missing helper methods like `UpdateGroupStats` and `SortGroupParticipants`
- ✅ **TournamentMatchService**: Fixed DSharpPlus API usage (ChannelType, ButtonStyle, etc.)
- ✅ **TournamentMatchService**: Fixed property access issues (IsComplete being read-only)

### 2. ✅ Complete Service Implementation

All services now implement their functionality directly:

- ✅ **TournamentMapService**: Implemented `GetTournamentMapPool` directly
- ✅ **TournamentGameService**: Implemented `HandleMatchCompletion` and other methods directly
- ✅ **TournamentPlayoffService**: Implemented `SetupPlayoffs` directly

### 3. ✅ Update Command Handlers

All command handlers have been updated to use the new services:

- ✅ **Event_Modal.cs**: Replaced direct TournamentManager usage with service calls
- ✅ **Event_Button.cs**: Updated to use services
- ✅ **Event_MessageCreated.cs**: Updated to use services
- ✅ **TournamentGroup.cs**: Updated to use services

### 4. ⏳ Testing Strategy

Implement a testing strategy to ensure the refactored code works correctly:

1. Create unit tests for each service
2. Test each command with the new services
3. Compare results with the previous implementation
4. Document any differences or issues

### 5. ✅ TournamentManager Removal

The TournamentManager class has been completely removed:

1. ✅ Marked TournamentManager as [Obsolete] with a message directing to new services
2. ✅ Monitored logs and fixed all references
3. ✅ Removed the TournamentManager class
4. ✅ Updated Program.cs to remove TournamentManager registration and use services instead

### 6. ⏳ Documentation

Update documentation to reflect the new architecture:

1. Create class diagrams showing service relationships
2. Document service responsibilities and interfaces

## Current Status

- [x] Create interface definitions
- [x] Implement ITournamentRepositoryService
- [x] Implement TournamentSignupService
- [x] Implement TournamentGroupService
- [x] Implement TournamentPlayoffService
- [x] Implement TournamentStateService
- [x] Implement TournamentManagerService
- [x] Update Program.cs to register all services
- [x] Update TournamentManagementGroup to use new services
- [x] Fix compilation errors in TournamentMatchService
- [x] Complete implementation of remaining services
- [x] Update Event_Modal to use new services
- [ ] Add unit tests for services
- [x] Deprecate and remove legacy TournamentManager

## Timeline

| Task | Estimated Completion | Status |
|------|---------------------|--------|
| Fix compilation errors | Week 1 | Completed ✅ |
| Complete service implementations | Week 2 | Completed ✅ |
| Update command handlers | Week 3 | Completed ✅ |
| Testing | Week 4 | Not Started |
| Deprecation | Week 5 | Completed ✅ |
| Documentation | Week 6 | In Progress |

## Conclusion

The refactoring is progressing well, with most of the core services implemented. The next focus should be on fixing compilation errors and completing the remaining service implementations. Once these are done, we can move on to testing and eventually deprecating the legacy TournamentManager.

## Benefits of Refactoring

This refactoring offers several significant benefits:

1. **Better Organization**: Each service has a single, clear responsibility
2. **Improved Testability**: Smaller, focused services are easier to unit test
3. **Dependency Injection**: Services use proper DI patterns for better extensibility
4. **Simpler Maintenance**: Smaller files are easier to understand and modify
5. **Better Error Handling**: Improved error logging and recovery options

## Architecture Overview

The new architecture follows a layered approach:

- **TournamentManagerService**: Acts as a facade coordinating between other services
- **Specialized Services**: Handle specific aspects of tournament management
- **Data Layer**: Repository pattern for data access and persistence

## Notes

- The refactoring preserves the existing data structures and file formats
- Some utility methods (like player display/ID handling) have been moved to appropriate services
- Methods related to Discord integration remain in the services for now 

## Service Implementation Details

### TournamentManagerService
- Acts as the facade for tournament operations
- Coordinates between specialized services
- Provides a simple interface for command handlers

### TournamentRepositoryService
- Handles data persistence (save/load)
- Manages tournament creation, retrieval, and deletion
- Provides repository pattern implementation for tournaments

### TournamentSignupService
- Manages tournament signups and registration
- Handles player roster management
- Processes signup commands and interactions

### TournamentGroupService
- Creates and manages tournament groups
- Handles group stage completion logic
- Provides player utility methods (display names, IDs, etc.)

### TournamentPlayoffService
- Manages playoff brackets and advancement
- Determines advancement criteria based on tournament format
- Creates and links playoff matches

### TournamentStateService
- Manages tournament state persistence
- Links rounds to tournaments
- Handles round state conversion and active rounds

### TournamentMatchService
- Manages match creation and completion
- Handles match scheduling and results
- Coordinates player interactions for matches

### TournamentGameService
- Manages individual games within matches
- Processes game results
- Handles match completion when all games are done

## Usage Examples for Command Handlers

Here's how to migrate from the legacy TournamentManager to the new services in command handlers:

### Before (using TournamentManager):

```csharp
public class TournamentCommands
{
    private readonly TournamentManager _tournamentManager;

    public TournamentCommands(TournamentManager tournamentManager)
    {
        _tournamentManager = tournamentManager;
    }

    public async Task CreateTournamentCommand(string name, List<DiscordMember> players)
    {
        var tournament = await _tournamentManager.CreateTournamentAsync(
            name, 
            players, 
            TournamentFormat.Default, 
            Context.Channel);
            
        // Handle tournament creation...
    }
}
```

### After (using new services):

```csharp
public class TournamentCommands
{
    private readonly ITournamentManagerService _tournamentService;

    public TournamentCommands(ITournamentManagerService tournamentService)
    {
        _tournamentService = tournamentService;
    }

    public async Task CreateTournamentCommand(string name, List<DiscordMember> players)
    {
        var tournament = await _tournamentService.CreateTournamentAsync(
            name, 
            players, 
            TournamentFormat.Default, 
            Context.Channel);
            
        // Handle tournament creation...
    }
}
```

### More Complex Example:

```csharp
public class TournamentManagementCommands
{
    private readonly ITournamentManagerService _tournamentManager;
    private readonly ITournamentSignupService _signupService;
    private readonly ITournamentStateService _stateService;

    public TournamentManagementCommands(
        ITournamentManagerService tournamentManager,
        ITournamentSignupService signupService,
        ITournamentStateService stateService)
    {
        _tournamentManager = tournamentManager;
        _signupService = signupService;
        _stateService = stateService;
    }

    public async Task StartTournamentCommand(string name)
    {
        // Get tournament
        var tournament = _tournamentManager.GetTournament(name);
        if (tournament == null)
        {
            await RespondAsync($"Tournament {name} not found", ephemeral: true);
            return;
        }

        // Check if enough players signed up
        if (!_signupService.HasEnoughPlayers(tournament))
        {
            await RespondAsync("Not enough players signed up for this tournament", ephemeral: true);
            return;
        }

        // Start the tournament
        await _tournamentManager.StartTournamentAsync(tournament, Context.Client);
        
        // Save the tournament state
        _stateService.SaveTournamentState(tournament);
        
        await RespondAsync($"Tournament {name} has been started!");
    }
}
```

## Conclusion

This refactoring effort has successfully split the monolithic TournamentManager class into smaller, focused services. The new architecture provides:

1. **Clear Separation of Concerns**: Each service handles a specific aspect of tournament management
2. **Better Maintainability**: Smaller classes are easier to understand and modify
3. **Improved Testability**: Services can be tested in isolation with mock dependencies
4. **Proper DI**: All services use dependency injection for better extensibility
5. **Backward Compatibility**: The legacy TournamentManager remains available during transition

The next phase will involve updating command handlers to use these new services, then gradually removing the legacy code. This approach ensures a smooth transition without disrupting existing functionality. 