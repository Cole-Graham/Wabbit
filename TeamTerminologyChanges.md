# Team Terminology Changes Summary

This document summarizes the targeted changes made to convert "player" terminology to "team" terminology in the codebase while preserving the original property names. This allows for a gradual transition to consistent terminology.

## Approach

1. **Surgical Changes**: Updated only variable names, parameters, and comments within method bodies
2. **Property Preservation**: Kept property names (like "Player1Username") unchanged for now
3. **Backward Compatibility**: Added new methods with team terminology while maintaining existing ones

## Files Changed

### 1. TournamentMatchService.cs

- Changed method parameters: `player1/player2` → `team1/team2` 
- Updated variable names: `team1/team2` → `team1Object/team2Object`
- Updated comments to reflect team terminology
- Modified string templates to use "Team" instead of "Player"

Example:
```csharp
// Before
var team1 = new Round.Team { Name = player1?.DisplayName ?? "Player 1", ... };

// After
var team1Object = new Round.Team { Name = team1?.DisplayName ?? "Team 1", ... };
```

### 2. TournamentGameService.cs

- Added `GetTeamScore` method with identical functionality to `GetPlayerScore`
- Updated variable names: `player1Wins/player2Wins` → `team1Wins/team2Wins`
- Maintained `GetPlayerScore` for backward compatibility

Example:
```csharp
// Before
int player1Wins = round.CustomProperties.ContainsKey("Player1Wins") ?
    Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;

// After
int team1Wins = round.CustomProperties.ContainsKey("Player1Wins") ?
    Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
```

### 3. TournamentMatchOperationsService.cs

- Changed method parameters: `player1/player2` → `team1/team2`
- Updated comments and error messages to use team terminology
- Improved LINQ query for better readability

Example:
```csharp
// Before
_logger.LogError("Cannot create match between the same player");

// After
_logger.LogError("Cannot create match between the same team");
```

### 4. TournamentManagerService.cs

- Changed variable names: `player1/player2` → `team1/team2`
- Updated comments and log messages to reflect team terminology
- Changed log text: "Invalid player pair" → "Invalid team pair"

### 5. Interface Files

- **ITournamentMatchService.cs**: Updated method signature and documentation
- **ITournamentGameService.cs**: Added `GetTeamScore` method while maintaining `GetPlayerScore`

## Player Terminology to Address

This checklist contains remaining instances where "player" terminology should be changed to "team" terminology in a future update.

### Properties and Classes

**Issues**: Property names referring to "players" create a fundamental mismatch with the team-based model. This causes conceptual issues when scaling to 2v2+ formats where a single property like `Player1Id` can't adequately represent multiple team members. The underlying data model becomes strained when trying to represent team-based information in player-centric properties.

- [ ] **ActiveRound Class** (`TournamentStateService.cs`):
  - [ ] `Player1Id`, `Player2Id` properties
  - [ ] `Player1Username`, `Player2Username` properties
  - [ ] `Player1Score`, `Player2Score` properties

- [ ] **Regular1v1 Class** (`Models/Regular1v1.cs`):
  - [ ] `Player1`, `Player2` properties and related deck properties

- [ ] **Tournament.MatchParticipant Class** (`Models/Tournament.cs`):
  - [ ] `Player` property - consider renaming to `TeamMember` or creating a team collection

### Methods and Services

**Issues**: Service methods designed for individual players often fail to handle team contexts properly. These methods make assumptions about 1:1 relationships between teams and players, which breaks when dealing with multi-player teams. This creates friction when implementing team-based functionality, requiring developers to mentally translate between player-focused and team-focused methods.

- [ ] **TournamentGroupService.cs**:
  - [ ] `GetPlayerDisplayName()`, `GetPlayerId()`, `GetPlayerMention()` methods
  - [ ] `ComparePlayerIds()` method
  - [ ] `ConvertToDiscordMember()` method

- [ ] **TournamentRepositoryService.cs**:
  - [ ] `CleanPlayerForSerialization()` method

- [ ] **TournamentStateService.cs**:
  - [ ] `SerializedPlayer` private class
  - [ ] References to player IDs and usernames in state conversion methods

### State Serialization/Deserialization

**Issues**: The current serialization logic assumes a direct mapping between players and teams, creating significant challenges for multi-player teams. When serializing and deserializing state, information about team composition and structure can be lost or incorrectly transformed. This limits the ability to properly represent and preserve team relationships in the stored state.

- [ ] Update serialization logic to handle teams with multiple players:
  - [ ] `ConvertRoundsToState()` method in `TournamentStateService.cs`
  - [ ] `ConvertStateToRounds()` method in `TournamentStateService.cs`

### UI/UX Elements

**Issues**: Inconsistent terminology in the UI creates confusion for users, especially in contexts where they identify as teams rather than individual players. When UI elements and messages refer to "players" in clearly team-based contexts, it reduces clarity and undermines the user experience. This is particularly problematic in tournament formats with multiple players per team.

- [ ] Update UI elements, help text, and error messages:
  - [ ] Button labels and modal titles
  - [ ] Command help text
  - [ ] Error messages shown to users

## Future Work

For a complete transition to team terminology, these items remain:

1. Rename property names (Player1Username → Team1Username, etc.)
2. Update serialization/deserialization logic to handle multi-player teams
3. Update UI labels and user-facing text
4. Create a proper multi-member team object model for 2v2+ tournaments

## Benefits

- More consistent terminology across the codebase
- Better support for different tournament formats (1v1, 2v2)
- Improved code readability
- Smoother transition path from "player" to "team" terminology 

## Multi-Player Team Design

This section outlines a proposed design for handling team names and structures across different tournament formats.

### Team Name Handling

1. **2v2, 3v3, and 4v4 Tournaments**:
   - Add an additional parameter to the tournament signup process for users to provide a team name
   - Implement a new `/tournament signup` command option: `team_name` (optional for 1v1, required for 2v2+)
   - Allow the team captain (first player to sign up) to set the team name during initial registration
   - Other team members join the existing team by referencing the team name or by invitation

2. **1v1 Tournaments**:
   - Automatically generate a team name using the player's username
   - Example: User "ExamplePlayer" would be assigned team name "ExamplePlayer"
   - This maintains backward compatibility while supporting the unified team model

### Data Model Changes

```csharp
// New Team class to replace player-centric model
public class Team
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<TeamMember> Members { get; set; } = new List<TeamMember>();
    public ulong CaptainId { get; set; } // Discord ID of team captain
    public bool Is1v1Team => Members.Count <= 1;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class TeamMember
{
    public ulong DiscordId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsCaptain { get; set; }
    public Dictionary<string, string> DeckCodes { get; set; } = new Dictionary<string, string>();
}
```

### Potential Issues and Solutions

1. **Team Name Uniqueness**:
   - **Issue**: Multiple teams might try to use the same name within a tournament
   - **Solution**: Enforce uniqueness at the tournament level, with clear error messages for duplicates
   - For 1v1 tournaments, append a numerical suffix if usernames would conflict (e.g., "ExamplePlayer #2")

2. **Team Persistence**:
   - **Issue**: Teams may want to persist across multiple tournaments
   - **Solution**: Implement a team registry system that stores team compositions
   - Allow users to select from previously created teams during signup
   - Add commands for team management: `/team create`, `/team invite`, `/team join`, `/team leave`

3. **Permission Structure**:
   - **Issue**: Who can modify team details and manage membership
   - **Solution**: Implement a captain system where the team creator has special privileges
   - Allow transfer of captaincy with `/team captain @newCaptain`
   - Support co-captains for larger teams to distribute team management

4. **Handling Team Changes**:
   - **Issue**: Players may need to change teams mid-tournament due to availability
   - **Solution**: Add tournament configuration option to allow/disallow team changes
   - Implement admin commands for tournament organizers to manage exceptions

5. **UI/UX for Team Creation**:
   - **Issue**: Team creation process might be cumbersome for casual 1v1 players
   - **Solution**: Keep team creation implicit for 1v1 tournaments, explicit for 2v2+
   - Provide intuitive team management UI in Discord with buttons and dropdowns

6. **Migration Path**:
   - **Issue**: Transitioning existing tournaments to the new model
   - **Solution**: Implement a database migration script to convert existing player entries to 1v1 teams
   - Add compatibility layer to handle both old and new formats during transition

### Implementation Phases

1. **Phase 1**: Add the team name parameter to tournament signup while maintaining current player structure
2. **Phase 2**: Create the team data model and setup team persistence
3. **Phase 3**: Implement team management commands and UI
4. **Phase 4**: Migrate existing tournaments and data to the new model
5. **Phase 5**: Remove legacy player-centric code and fully embrace team terminology 