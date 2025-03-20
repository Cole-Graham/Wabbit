# Interface Mappings for Tournament Logic Tests

This document maps the expected interfaces in our tests to the actual implementations in the Wabbit codebase.

## Service Interfaces

| Expected Interface/Method | Actual Implementation | Notes |
|--------------------------|----------------------|-------|
| `TournamentService.CreateGroups()` | `ITournamentGroupService.CreateGroups()` | The group creation functionality is in `TournamentGroupService` rather than `TournamentService` |
| `TournamentMapService.GetMapBanCount()` | Not directly exposed | There's no direct method for this, but can be inferred from the implementation of `GenerateMapList` |
| `MatchStatusService.RecordMapBanAsync()` | Exists | Method signatures match, but parameter usage may be different |
| `MatchStatusService.ConfirmMapBansAsync()` | Exists | Method signatures match, but parameter usage may be different |
| `TournamentService.IsMatchComplete()` | Not directly exposed | Functionality is spread across multiple services |

## Model Differences

| Expected Model | Actual Model | Differences |
|---------------|--------------|-------------|
| `Tournament.Group` | `Tournament.Group` | Exists, but with different property names/access patterns |
| `Tournament.GroupParticipant` | `Tournament.GroupParticipant` | `Points` is a read-only calculated property, not directly settable |
| `Tournament.Match` | `Tournament.Match` | Different from `Round` which is used in some contexts |
| `Round` | `Round` | Separate from `Tournament.Match` |

## Constructor Requirements

### TournamentService

```csharp
public TournamentService(
    ILogger<TournamentService> logger,
    ITournamentManagerService tournamentManagerService,
    ITournamentGroupService groupService,
    ITournamentStateService stateService, 
    ITournamentPlayoffService playoffService,
    ITournamentStateValidator stateValidator)
```

### TournamentGroupService

```csharp
public TournamentGroupService(
    IRandomProvider randomProvider,
    ILogger<TournamentGroupService> logger,
    ITournamentMatchOperationsService matchOperations,
    ITournamentScoreManager scoreManager,
    ITournamentStateValidator stateValidator)
```

### TournamentMapService

```csharp
public TournamentMapService(
    ILogger<TournamentMapService> logger,
    IRandomProvider randomProvider,
    IMapService mapService)
```

### MatchStatusService

```csharp
public MatchStatusService(
    ILogger<MatchStatusService> logger,
    ITournamentMapService mapService)
```

## Differences in Model Properties

### Tournament.GroupParticipant

- `Points` is a calculated property: `public int Points => (Wins * 3) + Draws;`
- Cannot be directly set, must set `Wins` and `Draws` instead

### Tournament.Match vs Round

- `Tournament.Match` is used for tournament rounds
- `Round` is used for the actual gameplay rounds with teams

## Missing Methods in Our Tests

| Test Method | Possible Implementation |
|-------------|-------------------------|
| `GetMapBanCount()` | Based on round length: `return round.Length == 5 ? 2 : 3;` |
| `IsMatchComplete()` | Based on match type and score: `return round.Teams[0].Wins >= requiredWins || round.Teams[1].Wins >= requiredWins;` |

## Implementation Approach

1. Create adapter classes for our tests that bridge between the expected interfaces and the actual implementations
2. Use dependency injection to provide test-specific implementations where needed
3. Implement extension methods to simplify property access patterns
4. Create helper methods for common test scenarios 