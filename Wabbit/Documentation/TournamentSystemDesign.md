# Tournament System Design

## Overview

This document outlines the complete tournament system architecture, from signup creation through tournament completion. It defines the responsibilities of each service, the flow of data, and the user interaction model.

### Task Markers Legend
🔧 Code adjustment/fix needed
📝 Documentation update needed
🎨 UI/UX change needed
🧪 Testing needed
⚠️ Validation/security task

## Tournament Lifecycle

### 1. Signup Phase

#### 1.1 Signup Creation

**User Command**:
```
/tournament signup_create <name> <format> <gameType> <startTimeUnix>
```
- **Handler**: `TournamentManagementGroup.CreateSignup()` [Line ~502]
- **Service Method**: `TournamentSignupService.CreateSignup()`
- **Parameters**:
  - Tournament name
  - Format selection:
    - 🔧[  ] Remove unsupported tournament formats (e.g., SingleElimination, DoubleElimination) from codebase
    - 🔧[  ] Remove unsupported format options from `TournamentFormatChoiceProvider`
    - 📝[  ] Update command description to specify "Group Stage + Playoffs" as the only supported format
  - Game type:
    - Initial version supports only 1v1 and 2v2 tournaments
  - Participant limits:
    - ⚠️[  ] Add validation in `TournamentSignupService` to enforce player count limits (7-32)
    - 🧪[  ] Add unit tests for player count validation
    - 🔧[  ] Review and optimize group distribution formula for player counts above 18
    - 📝[  ] Update GroupStageFormat.md to document group structures for player counts 19-32
  - Scheduled start time:
    - 🔧[  ] Add scheduled time display without automation logic
    - Visual display only, not connected to automatic tournament creation
  - Signup deadline:
    - 🔧[  ] Add SignupDeadline property to TournamentSignup model
    - 🔧[  ] Update signup_create command to accept optional deadline parameter
    - ⚠️[  ] Implement automatic signup closure at deadline
    - 🎨[  ] Add deadline countdown to signup embed
- 🔧[  ] Create new `/tournament signup_config` command with options:
  - Tournament name (required)
  - New tournament name (optional)
  - New scheduled start time (optional)
  - New signup deadline (optional) 

**UI Elements**:
- Signup embed (`TournamentSignupService.CreateSignupEmbed()`):
  - 🎨[  ] Reuse deadline countdown generation code from `signup_create` for reopened signups
  - Current participant list:
    - Display: `TournamentSignupService.CreateSignupEmbed()` → participant list generation
    - Data: `TournamentSignup.ParticipantInfo` list
    - Loading: `TournamentSignupService.LoadParticipantsAsync()`
  - Registration status:
    - Property: `TournamentSignup.IsOpen`
    - Display: `TournamentSignupService.CreateSignupEmbed()` → status text generation
  - Tournament format:
    - Property: `TournamentSignup.Format`
    - Options: `TournamentFormatChoiceProvider.ProvideAsync()`
  - Scheduled start time:
    - Property: `TournamentSignup.ScheduledStartTime`
    - Display: `TournamentSignupService.CreateSignupEmbed()` → time formatting
  - Player seeds:
    - Data: `TournamentSignup.SeedInfo` list
    - Display: `TournamentSignupService.GetSeedDisplay()`

#### 1.2 Player Registration

**User Commands**:
- **Player Signup**:
  - **Handler**: `TournamentManagementGroup.AddToSignup()` [Line ~758]
  - **Service Method**: `TournamentSignupService.UpdateSignup()`
  - **UI**: 'Signup' button on signup embed

- **Leave Tournament**:
  - **Handler**: `TournamentSignupHandler.HandleWithdrawButton()` and `HandleCancelSignupButton()`
  - **Service Method**: `TournamentSignupService.UpdateSignup()`
  - **UI**: 
    - 'Withdraw' button on signup embed
    - When clicked, shows confirmation dialog with:
      - 'Confirm Withdraw' button (`cancel_signup_{tournamentName}_{userId}`)
      - 'Keep Signup' button (`keep_signup_{tournamentName}_{userId}`)
  - **Flow**:
    1. User clicks 'Withdraw' button
    2. System shows ephemeral confirmation dialog
    3. User confirms with either 'Confirm Withdraw' or 'Keep Signup'
    4. System updates signup and refreshes embed

**Admin Commands**:
- **Manual Add/Remove Player**:
  ```
  /tournament signup_add <tournamentName> <player>
  /tournament signup_remove <tournamentName> <player>
  ```
  - **Handler**: `TournamentManagementGroup.AddToSignup()/RemoveFromSignup()`
  - **Improvements**:
    - 📝[  ] Add logging for all admin actions

- **Set Player Seed**:
  ```
  /tournament signup_set_seed <tournamentName> <player> <seed>
  ```
  - **Handler**: `TournamentManagementGroup.SetSeed()` [Line ~1077]
  - **Validation**:
    - Seed values must be between 0-999
    - 🔧[  ] Update seed value range to 0-3000 to accommodate potential ELO rating system

#### 1.3 Data Management

**Data Persistence**:
- **Save Operations**:
  - **Method**: `TournamentSignupService.SaveSignupsAsync()`
  - **File**: `Data/signups.json`
  - **Format**: JSON with reference preservation
  - **Improvements**:
    - 🔧[  ] Add periodic backup of signup data
    - ⚠️[  ] Implement data validation on load/save
    - 🧪[  ] Add recovery scenario tests
    - 🔧[  ] Implement comprehensive tournament archival system:
      - Create `TournamentArchiveService` to manage permanent archives
      - Store detailed match history including:
        - Map bans and ban priority
        - Deck codes (encrypted)
        - Match results and scores
        - Player stats and performance metrics
      - Archive format should include:
        - Tournament metadata (format, dates, etc.)
        - Complete group stage results
        - Playoff bracket progression
        - Individual match details
    - 🔧[  ] Add archive search and retrieval functionality:
      - Search by tournament name, date, player
      - Filter by format, game type
      - Export tournament data in various formats
    - 📝[  ] Document archive data structure and API

**Validation Rules**:
- Tournament names must be unique (✓ implemented in `TournamentSignupService.CreateSignup()`)
- Player can't join if signup is closed (✓ implemented in `TournamentSignupHandler.HandleSignupButton()`)
- Admin commands require appropriate permissions (✓ implemented in command handlers)
- Seed values must be between 0-999 (✓ implemented in `TournamentManagementGroup.SetSeed()`)
- Format must match supported tournament types (✓ implemented in model)
- 🔧[  ] Add validation to enforce minimum player count of 7
- 🔧[  ] Fix group distribution code for player counts above 18:
  - Update `TournamentGroupService.DetermineGroupCount()` to handle counts up to 32
  - Update `TournamentGroupService.GetOptimalGroupSizes()` to handle counts up to 32
  - Add test cases for large player counts

### 2. Tournament Creation Phase

#### 2.1 Tournament Initialization

**Core Components**:
- **Command Handler**: `TournamentManagementGroup.CreateTournamentFromSignup()` [Line ~76]
- **Service Methods**:
  - `TournamentService.CreateTournamentAsync()`
  - `TournamentManagerService.CreateTournamentAsync()`
  - `TournamentGroupService.CreateGroups()`

**Flow**:
1. **Signup Validation**:
   - Minimum 3 participants required
   - Check for seeded players in `signup.Seeds`
   - 🔧[  ] Add validation for maximum player count (32)

2. **Tournament Creation**:
   - **Method**: `CreateTournamentWithSeeding()` or `CreateTournamentAsync()`
   - **Properties Set**:
     - Name (from signup)
     - Format (from signup)
     - GameType (from signup)
     - CurrentStage = TournamentStage.Groups
     - AnnouncementChannel (from context)

3. **Group Initialization**:
   - **Method**: `TournamentGroupService.CreateGroups()`
   - **Parameters**:
     - Tournament object
     - List<DiscordMember> players
     - Dictionary<DiscordMember, int>? playerSeeds
   - 🔧[  ] Review group size calculations for player counts 19-32

#### 2.2 Group Stage Setup

**Core Components**:
- **Service**: `TournamentGroupService`
- **Methods**:
  - `DetermineGroupCount(playerCount, format)`
  - `GetOptimalGroupSizes(playerCount, groupCount)`
  - `DistributePlayersWithSeeding(tournament, players, playerSeeds, groupSizes)`
  - `DistributePlayersRandomly(tournament, players, groupSizes)`
  - `GenerateGroupMatches(tournament, group)`

**Group Creation Process**:
1. **Group Count Determination**:
   - Based on player count and tournament format
   - Uses `DetermineGroupCount()` method
   - 🔧[  ] Update group count logic for 19-32 players

2. **Group Size Calculation**:
   - Method: `GetOptimalGroupSizes()`
   - Returns list of sizes for each group
   - 🔧[  ] Verify optimal distribution for larger tournaments

3. **Player Distribution**:
   - **With Seeding**:
     - Snake draft order distribution
     - Seeds from `signup.Seeds` collection
     - Preserves seed values in `GroupParticipant.Seed`
   - **Without Seeding**:
     - Random distribution using `IRandomProvider`
     - Even distribution across groups

4. **Match Generation**:
   - Method: `GenerateGroupMatches()`
   - Creates round-robin matches within each group
   - Sets BestOf value for matches
   - 🔧[  ] Review match scheduling for larger groups

**Data Structures**:
- **Tournament.Group**:
  - Name (e.g., "Group A")
  - List<GroupParticipant>
  - List<Match>
  - IsComplete flag

- **GroupParticipant**:
  - Player (DiscordMember)
  - Seed (int)
  - Stats (wins, losses, draws, etc.)

**Tasks**:
- 🧪[  ] Add unit tests for group creation with 19-32 players
- 🧪[  ] Test seeded vs random distribution balance
- 🔧[  ] Implement group size balancing for odd player counts
- 📝[  ] Document group distribution patterns for each player count
- ⚠️[  ] Add validation for minimum/maximum players per group

#### 2.3 Format Settings

**Current Implementation**:
- Group Stage matches are Best-of-1
- Playoff matches are Best-of-3
- Finals matches are Best-of-3
- Third place matches use the same format as semifinals (Best-of-3)

**Match Format Configuration**:
```csharp
public class TournamentSettings
{
    public bool IncludeThirdPlaceMatch { get; set; } = false;  // Default to no third place match
    public int BestOfFinals { get; set; } = 3;                 // Finals are Best-of-3
    public int BestOfSemifinals { get; set; } = 3;            // Semifinals are Best-of-3
    public int BestOfQuarterfinals { get; set; } = 3;         // Quarterfinals are Best-of-3
    public int BestOfGroupStage { get; set; } = 1;            // Group stage is Best-of-1
}
```

**Service Methods**:
- `TournamentService.CreateTournamentAsync()`
- `TournamentGroupService.CreateGroups()`
- `TournamentPlayoffService.GetAdvancementCriteria()`

**Advancement Storage**:
```csharp
tournament.CustomProperties["GroupWinnersAdvance"] = advancementCriteria.groupWinners;
tournament.CustomProperties["BestThirdPlaceAdvance"] = advancementCriteria.bestThirdPlace;
```

**Tasks**:
- 🔧[  ] Add format validation in tournament creation
- 🔧[  ] Implement format-specific group size constraints
- 📝[  ] Document format requirements and limitations
- 🧪[  ] Add test cases for format validation

### 3. Match Phase

#### 3.1 Pre-Match Setup

**Core Components**:
- **Service**: `TournamentMatchService` in `Services/TournamentMatchService.cs`
- **Handler**: `TournamentManagementGroup.StartMatchRoundAsync()` in `BotClient/Commands/TournamentManagementGroup.cs`
- **Status Service**: `MatchStatusService` in `Services/MatchStatusService.cs`
- **Thread Service**: `TournamentThreadService` in `Services/TournamentThreadService.cs`
- **Validator**: `TournamentStateValidator` in `Services/TournamentStateValidator.cs`

**Thread Management**:
- **Creation**: Threads are created when matches are first initialized during group stage setup
  - 🔧[  ] Update `TournamentMatchService` to use "TeamThreads" custom property
  - 🔧[  ] Update `TournamentThreadService` to use "TeamThreads" custom property
  - 🔧[  ] Add migration for existing tournaments using "PlayerThreads"
  - 🧪[  ] Test thread persistence with "TeamThreads" property

- **Storage**: Thread IDs are stored in `Tournament.CustomProperties["TeamThreads"]` as a dictionary mapping team IDs to thread IDs
  - 🔧[  ] Update thread lookup logic in match creation
  - 🔧[  ] Implement thread persistence across bot restarts
  - 🧪[  ] Test thread recovery for 1v1 and 2v2 matches

- **Access Control**: Thread permissions are updated for each match to include only active participants
  - ⚠️[  ] Add thread permission validation
  - 🧪[  ] Verify thread permissions for team members
  - 🔧[  ] Create thread cleanup service for completed tournaments

- **Lifecycle**:
  1. Thread Creation: During match initialization
  2. Permission Updates: Before each match starts
  3. Status Updates: Throughout match progression
  4. Archival: After tournament completion
  - 📝[  ] Document thread management workflow

**Match Status Management**:
- Each match has a unique status embed that includes:
  - Current match stage and format (Bo1/Bo3)
  - Player names and current scores
  - Map pool and banned maps
  - Deck submission status
  - Match result when completed
  - 🎨[  ] Enhance match status embed design
  - 🔧[  ] Add match status embed recovery system

- Status embeds are stored in `Tournament.RelatedMessages`

**Match Status Embed**:
```csharp
public class MatchStatusEmbed
{
    public ulong MessageId { get; set; }
    public string MatchId { get; set; }
    public ulong ChannelId { get; set; }
    public DateTime LastUpdated { get; set; }
    public MatchStatus Status { get; set; }
    public Dictionary<string, object> Components { get; set; } = [];
}
```

#### 3.2 Map Ban Process

**Core Components**:
- **Handler**: `MapBanHandler` in `BotClient/Events/Components/Tournament/MapBanHandler.cs`
- **Service**: `TournamentMapService` (interface: `ITournamentMapService`)
- **Modal**: `MapBanModalHandler` in `BotClient/Events/Modals/Tournament/MapBanModalHandler.cs`

**Map Ban Flow**:
1. **Stage Initialization**:
   ```csharp
   // MatchStatusService.cs
   public async Task<DiscordMessage> UpdateToMapBanStageAsync(DiscordChannel channel, Round round, DiscordClient client)
   ```

2. **Ban Selection**:
   - Dropdown component with map options
   - Priority-based selection (3 maps for Bo1/Bo3, 2 maps for Bo5)
   - Color coding for banned/available maps

3. **Confirmation Process**:
   ```csharp
   // MapBanHandler.cs
   await _matchStatusService.RecordMapBanAsync(channel, round, teamName, bannedMaps, client);
   ```

**State Management**:
- Stored in `Round.Teams[].MapBans`
- Persisted via `TournamentStateService`
- Map pool managed by `TournamentMapService`

**Tasks**:
- ⚠️[  ] Add validation for concurrent map bans

#### 3.3 Deck Submission

**Core Components**:
- **Handler**: `DeckSubmissionHandler` in `BotClient/Events/Components/Tournament/DeckSubmissionHandler.cs`
- **Modal**: `DeckSubmissionModalHandler` in `BotClient/Events/Modals/Tournament/DeckSubmissionModalHandler.cs`
- **Service**: `MatchStatusService.UpdateToDeckSubmissionStageAsync()`

**Submission Flow**:
1. **Stage Transition**:
   ```csharp
   // MatchStatusService.cs
   public async Task<DiscordMessage> UpdateToDeckSubmissionStageAsync(DiscordChannel channel, Round round, DiscordClient client)
   ```

2. **Deck Submission**:
   - Command: `/tournament submit_deck`
   - Button component: `submit_deck_{roundHash}`
   - Confirmation/revision system

3. **Privacy Controls**:
   - Deck codes hidden from opponents
   - Ephemeral responses for submissions
   - Map reveal only after both decks submitted

**State Management**:
```csharp
// Round.Team properties
HasSubmittedDeck
DeckCode
```

**Tasks**:
- 🔧[  ] Implement deck code validation
- 🎨[  ] Update deck submission UI per MatchStatusImprovementPlan.md

#### 3.4 Game Execution

**Core Components**:
- **Service**: `TournamentGameService` in `Services/TournamentGameService.cs`
- **Handler**: `GameResultHandler` in `BotClient/Events/Components/Tournament/GameResultHandler.cs`
- **Interface**: `ITournamentGameService` in `Services/Interfaces/ITournamentGameService.cs`

**Game Flow**:
1. **Result Selection**:
   ```csharp
   // GameResultHandler.cs
   await _gameService.HandleGameResultAsync(tournamentRound, e.Channel, winnerId, client);
   ```
   - Validates current stage is `MatchStage.GameResults`
   - Processes winner selection from dropdown
   - Updates match status with result

2. **Progress Tracking**:
   - Score updates via `GetPlayerScore(round, playerId)`
   - Game number tracking with `GetCurrentGameNumber(round)`
   - Win/loss recording in `RecordGameResultAsync()`

3. **Multi-Game Management**:
   - Best-of-X format support
   - Map selection for next game if match continues
   - Series completion check via `IsMatchComplete(round)`

**State Management**:
```csharp
// Round properties
CurrentStage = MatchStage.GameResults
Teams[].Wins
CustomProperties["Winner"]
Maps[] // Tracks played maps
```

**Tasks**:
- 🔧[  ] Add replay verification system
- 🧪[  ] Add test cases for edge case scenarios
- ⚠️[  ] Add validation for concurrent result submissions

#### 3.5 Match Completion

**Core Components**:
- **Service**: `TournamentMatchService` in `Services/TournamentMatchService.cs`
- **Manager**: `TournamentManagerService` in `Services/TournamentManagerService.cs`
- **Repository**: `TournamentRepositoryService` in `Services/TournamentRepositoryService.cs`
- **Validator**: `TournamentStateValidator` in `Services/TournamentStateValidator.cs`

**Completion Flow**:
1. **State Validation**:
   ```csharp
   // TournamentMatchService.cs
   if (!_stateValidator.ValidateMatchCompletion(tournament, match))
   {
       _logger.LogError($"Cannot complete match {match.Name}: validation failed");
       return;
   }
   ```

2. **Result Finalization**:
   ```csharp
   // TournamentMatchService.cs
   await UpdateMatchResultAsync(tournament, match, winner, winnerScore, loserScore);
   ```
   - Updates match result with final scores
   - Sets winner and completion timestamp
   - Updates group statistics for group stage matches

3. **Stage-Specific Processing**:
   ```csharp
   // TournamentMatchService.cs
   switch (match.Type)
   {
       case TournamentMatchType.GroupStage:
           await _scoreManager.UpdateGroupScores(tournament, match);
           break;
       case TournamentMatchType.Quarterfinal:
       case TournamentMatchType.Semifinal:
       case TournamentMatchType.Final:
           // Delegate playoff progression to playoff service
           await _playoffService.UpdateBracketAdvancementAsync(tournament, match);
           break;
   }
   ```

4. **State Updates**:
   ```csharp
   match.Result = new Tournament.MatchResult
   {
       CompletedAt = DateTime.Now,
       Winner = winner,
       MapResults = round.Maps?.ToList(),
       DeckCodes = /* ... */
   };
   ```
   - Records match completion time
   - Stores map history and results
   - Archives deck codes securely

5. **Thread Management**:
   - Archives match thread
   - Updates status embed with final result
   - Cleans up temporary messages

**Data Archival**:
```csharp
// TournamentRepositoryService.cs
cleanedMatch.Result = new Tournament.MatchResult
{
    Winner = CleanPlayerForSerialization(match.Result.Winner),
    MapResults = match.Result.MapResults?.ToList(),
    CompletedAt = match.Result.CompletedAt,
    DeckCodes = match.Result.DeckCodes
};
```

**Tasks**:
- 🔧[  ] Implement comprehensive match statistics
- 🔧[  ] Add match replay storage system
- 🔧[  ] Create match summary generation
- 🎨[  ] Update completion visualization
- ⚠️[  ] Add validation for result modifications
- 📝[  ] Document match completion workflow
- 🧪[  ] Add completion handler tests
- 🔧[  ] Add state validation for all match operations
- 🔧[  ] Implement stage-specific completion handlers

### 4. Group Stage Management

#### 4.1 Progress Tracking

**Core Components**:
- **Service**: `TournamentStateService` in `Services/TournamentStateService.cs`
- **Model**: `Tournament.GroupParticipant` in `Models/Tournament.cs`
- **Handler**: `TournamentManagementGroup` in `BotClient/Commands/TournamentManagementGroup.cs`

**Match Result Processing**:
```csharp
// Update wins/losses/draws
if (winner == 1)
{
    groupParticipant1.Wins++;
    groupParticipant2.Losses++;
}
else if (winner == 2)
{
    groupParticipant1.Losses++;
    groupParticipant2.Wins++;
}
else
{
    groupParticipant1.Draws++;
    groupParticipant2.Draws++;
}
```
- 🔧[  ] Add support for custom point systems
- 🧪[  ] Add tests for result processing edge cases

**Standing Calculations**:
- Points System:
  - Win = 3 points
  - Draw = 1 point
  - Loss = 0 points
  - 🔧[  ] Add configuration for custom point values
  - 🧪[  ] Test point calculation scenarios
- Tiebreaker Order:
  1. Total Points
  2. Match Wins
  3. Game Differential (GamesWon - GamesLost)
  4. Head-to-Head Record (if applicable)
  5. Tiebreaker Match (if still tied)
  - 🔧[  ] Implement head-to-head tiebreaker
  - 🧪[  ] Add tests for tiebreaker scenarios
  - 📝[  ] Document tiebreaker rules

**Tiebreaker Match System**:
- 🔧[  ] Create `CreateTiebreakerMatch` method in `TournamentGroupService`
- 🔧[  ] Add `TiebreakerMatch` type for special scenarios
- 🔧[  ] Add `IsTiebreakerMatch` flag to `Tournament.Match`
- 🔧[  ] Create tie detection method
- 🔧[  ] Implement admin tiebreaker commands
- 🎨[  ] Add UI for tiebreaker match status
- 📝[  ] Document tiebreaker process
- 🧪[  ] Test multi-way tie scenarios

**Group Participant Properties**:
```csharp
public class GroupParticipant
{
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int Points => (Wins * 3) + Draws;
    public int GamesWon { get; set; }
    public int GamesLost { get; set; }
    public bool AdvancedToPlayoffs { get; set; }
}
```
- 🎨[  ] Create standings visualization
- 🔧[  ] Add performance statistics tracking

**Tasks**:
- 🔧[  ] Add support for custom point systems
- 🔧[  ] Implement head-to-head tiebreaker
- 🧪[  ] Add tests for tiebreaker scenarios
- 🎨[  ] Create standings visualization
- 📝[  ] Document tiebreaker rules
- 🔧[  ] Implement group stage tiebreaker match system:
  - Create `CreateTiebreakerMatch` method in `TournamentGroupService`
  - Add `TiebreakerMatch` type to handle special tiebreaker scenarios
  - Add `IsTiebreakerMatch` flag to `Tournament.Match` class
  - Create method to detect when teams are tied on all criteria (points, wins, game differential)
  - Implement admin command to create tiebreaker match when needed
  - Add UI for displaying tiebreaker match status
  - Store tiebreaker match results in tournament history
  - Update group standings after tiebreaker completion
  - Add validation to ensure tiebreaker matches only occur when necessary
  - Document tiebreaker match process in admin guide
  - Ensure tiebreaker matches follow tournament format (Best-of-3)
  - Add support for multiple tiebreaker matches if needed (e.g., 3-way tie)
  - Implement proper seeding for playoff qualification based on tiebreaker results

#### 4.2 Advancement Processing

**Core Components**:
- **Service**: `TournamentPlayoffService` in `Services/TournamentPlayoffService.cs`
- **Method**: `GetQualifiedParticipants()` for playoff qualification

**Advancement Criteria**:
```csharp
// TournamentMatchService.cs
public (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
{
    return playerCount switch
    {
        7 => (4, 0),      // Top 4 advance to playoffs
        8 => (2, 0),      // Top 2 from each group
        9 => (2, 2),      // Top 2 from each group + 2 best third-place
        10 => (2, 0),     // Top 2 from each group
        11 => (2, 2),     // Top 2 from each group + 2 best third-place
        12 => (2, 2),     // Top 2 from each group + 2 best third-place
        // ... more cases
    };
}
```

**Qualification Process**:
1. **Group Winners**:
   ```csharp
   var sortedParticipants = group.Participants
       .OrderByDescending(p => p.Points)
       .ThenByDescending(p => p.Wins)
       .ThenByDescending(p => p.GamesWon - p.GamesLost)
       .ToList();
   ```

2. **Best Third Place**:
   ```csharp
   var bestThirdPlaces = thirdPlaceParticipants
       .OrderByDescending(p => p.Points)
       .ThenByDescending(p => p.Wins)
       .ThenByDescending(p => p.GamesWon - p.GamesLost)
       .Take(bestThirdPlace)
       .ToList();
   ```

**Stage Transition**:
1. Group completion check
2. Playoff bracket generation
3. Participant seeding for playoffs
4. Tournament stage update

**Tasks**:
- 🔧[  ] Add support for custom advancement rules
- 🔧[  ] Implement group stage completion detection
- 🧪[  ] Add tests for advancement edge cases
- 🎨[  ] Create playoff bracket preview
- ⚠️[  ] Add validation for advancement criteria
- 📝[  ] Document advancement rules for all formats

### 5. Playoff Stage

#### 5.1 Playoff Initialization

**Core Components**:
- **Service**: `TournamentPlayoffService` in `Services/TournamentPlayoffService.cs`
- **Interface**: `ITournamentPlayoffService` in `Services/Interfaces/ITournamentPlayoffService.cs`
- **Model**: `Tournament.Match` and `Tournament.MatchParticipant` in `Models/Tournament.cs`
- **Bracket Manager**: `TournamentBracketManager` in `Services/TournamentBracketManager.cs`
- **State Validator**: `TournamentStateValidator` in `Services/TournamentStateValidator.cs`

**Initialization Process**:
```csharp
// TournamentPlayoffService.cs
public void SetupPlayoffs(Tournament tournament)
{
    // Validate state transition
    if (!_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
    {
        _logger.LogWarning($"Invalid state transition to Playoffs for tournament {tournament.Name}");
        return;
    }

    // Get advancement criteria
    (int groupWinners, int bestThirdPlace) = GetAdvancementCriteria(totalParticipants, groupCount);
    
    // Mark tournament as in playoff stage
    tournament.CurrentStage = TournamentStage.Playoffs;
    
    // Get qualified participants
    var qualifiedParticipants = GetQualifiedParticipants(tournament, groupWinners, bestThirdPlace);
    
    // Create playoff bracket using bracket manager
    var matches = _bracketManager.CreatePlayoffBracket(tournament, qualifiedParticipants);
    tournament.PlayoffMatches.AddRange(matches);

    // Link matches in the bracket
    _bracketManager.LinkBracketMatches(tournament, matches);

    // Create third place match if needed
    if (tournament.Settings?.IncludeThirdPlaceMatch == true)
    {
        CreateThirdPlaceMatch(tournament);
    }
}
```

**Match Format Settings**:
- Quarterfinals: Best-of-3 (configurable)
- Semifinals: Best-of-3 (configurable)
- Finals: Best-of-3 (configurable)
- Third Place Match: Best-of-3 (if enabled)

**Tasks**:
- 🔧[  ] Add support for custom match formats per stage:
  - Create `PlayoffMatchSettings` class
  - Add format configuration in tournament settings
  - Update match creation to use custom formats
- 🔧[  ] Implement bracket size optimization:
  - Add support for non-power-of-2 brackets
  - Optimize bye placement algorithm
  - Add seeding preferences for byes
- 🧪[  ] Add comprehensive playoff tests:
  - Test various player counts
  - Verify seeding algorithm
  - Test bracket advancement
  - Validate third place matches
- 📝[  ] Document playoff configuration:
  - Add format requirements
  - Document seeding rules
  - Create bracket examples
- ⚠️[  ] Add validation for playoff setup:
  - Verify participant count
  - Validate match format settings
  - Check advancement criteria

#### 5.2 Bracket Management

**Core Components**:
- **Service**: `TournamentBracketManager` in `Services/TournamentBracketManager.cs`
- **Interface**: `ITournamentBracketManager` in `Services/Interfaces/ITournamentBracketManager.cs`
- **Visualization**: `TournamentVisualization` in `Misc/TournamentVisualization.cs`

**Match Types**:
```csharp
public enum TournamentMatchType
{
    GroupStage,
    GroupStageTiebreaker,
    RoundOf16,
    Quarterfinal,
    Semifinal,
    Final,
    PlayoffThirdPlace,
}
```

**Bracket Creation**:
```csharp
// TournamentBracketManager.cs
public List<Tournament.Match> CreatePlayoffBracket(Tournament tournament, List<Tournament.MatchParticipant> participants)
{
    var matches = new List<Tournament.Match>();
    
    // Calculate bracket size
    int bracketSize = NextPowerOfTwo(participants.Count);
    
    // Create first round matches with byes
    var firstRoundMatches = CreateFirstRoundMatches(participants, bracketSize);
    matches.AddRange(firstRoundMatches);
    
    // Create subsequent rounds
    var currentRoundMatches = firstRoundMatches;
    while (currentRoundMatches.Count > 1)
    {
        var nextRoundMatches = CreateNextRoundMatches(currentRoundMatches);
        matches.AddRange(nextRoundMatches);
        currentRoundMatches = nextRoundMatches;
    }
    
    return matches;
}
```

**Match Linking**:
```csharp
// TournamentBracketManager.cs
public void LinkBracketMatches(Tournament tournament, List<Tournament.Match> matches)
{
    // Link matches in sequential rounds
    var roundMatches = matches.GroupBy(m => m.Type).OrderBy(g => g.Key);
    
    foreach (var round in roundMatches.Skip(1))
    {
        var previousRound = roundMatches.ElementAt(roundMatches.ToList().IndexOf(round) - 1);
        
        for (int i = 0; i < round.Count(); i++)
        {
            var nextMatch = round.ElementAt(i);
            var sourceMatches = previousRound.Skip(i * 2).Take(2);
            
            foreach (var sourceMatch in sourceMatches)
            {
                sourceMatch.NextMatch = nextMatch;
            }
        }
    }
}
```

**Bracket Advancement**:
```csharp
// TournamentBracketManager.cs
public bool UpdateBracketAdvancement(Tournament tournament, Tournament.Match match)
{
    if (match.Result?.Winner == null) return false;

    var nextMatch = match.NextMatch;
    if (nextMatch == null) return true;

    // Find match position and advance winner
    var prevMatches = tournament.PlayoffMatches
        .Where(m => m.NextMatch == nextMatch)
        .ToList();

    int matchIndex = prevMatches.IndexOf(match);
    int nextMatchSlot = matchIndex % 2;

    // Update next match participant
    nextMatch.Participants[nextMatchSlot] = new Tournament.MatchParticipant
    {
        Player = match.Result.Winner,
        SourceMatch = match
    };

    return true;
}
```

**Tasks**:
- 🔧[  ] Implement comprehensive match management:
  - Create match history tracking
  - Add match state validation
  - Implement forfeit handling
- 🔧[  ] Enhance bracket visualization:
  - Add dynamic bracket scaling
  - Create match preview cards
- 🔧[  ] Add advanced seeding options:
  - Support custom seeding rules
  - Add regional separation
  - Implement group winner protection
- 🧪[  ] Add bracket management tests:
  - Test advancement scenarios
  - Verify third place matches
  - Test visualization components
- 📝[  ] Document bracket management:
  - Add admin commands
- ⚠️[  ] Implement bracket validation:
  - Add result verification
  - Validate advancement rules
  - Check bracket integrity
- 🎨[  ] Improve bracket UI/UX:
  - Add match status indicators
  - Implement match details panel

#### 5.4 Third Place Match System

**Service Architecture**:
- **Dependencies** (Services required by handler):
  - `ITournamentService`: Tournament data access
  - `ITournamentPlayoffService`: Playoff stage management
  - `ITournamentStateService`: State persistence
  - `ITournamentBracketManager`: Bracket updates

- **Dependents** (Systems using handler):
  - `ComponentInteractionHandler`: Routes Discord button interactions
  - `DSharpPlus.ComponentInteractionCreatedEventArgs`: Provides interaction data
  - `Tournament Component System`: Manages tournament-related Discord components

**Workflow**:
1. System monitors semifinal completion through `TournamentPlayoffService.AreSemifinalsCompleted()`
2. Upon completion, generates interactive prompt in `BotChannelId`:
   ```
   🏅 Third Place Match Available
   Both semifinals are now complete. Would you like to create a third place match?
   
   [Create Third Place Match] (Button)
   ```

**Component Details**:
- **Handler**: `AdminThirdPlaceMatchHandler`
- **Button ID**: `admin_create_third_place_{tournamentId}`
- **Access Control**: Requires `ManageGuild` permission
- **Validation**:
  - Tournament exists and is active
  - Both semifinals are completed
  - No existing third place match
  - User has required permissions

**Match Creation Process**:
1. Validates state via `CanCreateThirdPlaceMatch()`
2. Creates match with `CreateThirdPlaceMatchAsync()`
3. Links semifinal losers automatically
4. Updates tournament display
5. Sends confirmation embed with match details

**State Management**:
- Updates tournament state after creation
- Persists changes via `TournamentStateService`
- Updates bracket visualization
- Maintains match linkage with semifinals

**Error Handling**:
- Validates prerequisites before creation
- Provides clear error messages for invalid states
- Logs all creation attempts and failures
- Maintains tournament state consistency

### 6. Tournament Completion

#### 6.1 Finalization

**Core Components**:
- **Service**: `TournamentArchiveService` (to be implemented)
- **Methods**:
  - `ArchiveTournamentAsync()`
  - `GenerateTournamentSummaryAsync()`
  - `ExportTournamentDataAsync()`

**Data Collection**:
- Match Details:
  - 🔧[  ] Implement map ban history with priorities
  - 🔧[  ] Store deck submissions with timestamps
  - 🔧[  ] Record game results and scores
  - 🔧[  ] Track player performance metrics
- Replay System:
  - 🔧[  ] Add replay download functionality
  - 🔧[  ] Store game and mod version info
  - 🔧[  ] Implement replay verification
- Administrative:
  - 🔧[  ] Store chat logs
  - 🔧[  ] Track admin actions
  - 📝[  ] Document data retention policies

**Data Organization**:
- Archive Structure:
  - 🔧[  ] Create tournament overview format
  - 🔧[  ] Design player profiles schema
  - 🔧[  ] Define match history format
  - 🔧[  ] Implement data compression
  - 🔧[  ] Add metadata tagging
- Search System:
  - 🔧[  ] Implement archive search functionality
  - 🔧[  ] Create filtering options
  - 🎨[  ] Design search interface

**Storage and Access**:
- System Implementation:
  - 🔧[  ] Create file-based JSON archives
  - 🔧[  ] Add optional database integration
  - 🔧[  ] Set up cloud storage support
- Access Control:
  - ⚠️[  ] Implement archive permissions
  - 🔧[  ] Create admin management tools
  - 🔧[  ] Design API for archive access
  - 📝[  ] Document access policies

**Tasks**:
- 🔧[  ] Design archive data schema
- 🔧[  ] Implement TournamentArchiveService
- 🔧[  ] Add archive search and retrieval system
- 🔧[  ] Create archive management tools
- 📝[  ] Document archival system
- 🧪[  ] Add archive system tests
- ⚠️[  ] Implement data privacy controls

## Service Architecture

### Core Services Hierarchy

1. **Low-Level Services**:
   - `TournamentMatchOperationsService`:
     - Core match creation and result updates
     - No dependencies on higher-level services
     - Focused on individual match operations
   - `TournamentStateValidator`:
     - Validates tournament state transitions
     - Ensures operations are allowed in current state
     - No dependencies on other services

2. **Mid-Level Services**:
   - `TournamentScoreManager`:
     - Manages score calculations and updates
     - Group standings and statistics
     - No dependencies on match operations
   - `TournamentBracketManager`:
     - Manages playoff bracket creation and updates
     - Handles match linking and advancement
     - No dependencies on other services

3. **High-Level Services**:
   - `TournamentMatchService`:
     - Coordinates match operations and score management
     - Handles tournament stage transitions
     - Dependencies:
       - `ITournamentMatchOperationsService`
       - `ITournamentScoreManager`
       - `ITournamentStateValidator`
   - `TournamentPlayoffService`:
     - Manages playoff stage progression
     - Dependencies:
       - `ITournamentMatchOperationsService`
       - `ITournamentStateValidator`
       - `ITournamentBracketManager`
   - `TournamentGroupService`:
     - Manages group creation and progression
     - Dependencies:
       - `ITournamentMatchOperationsService`
       - `ITournamentScoreManager`
       - `ITournamentStateValidator`

### Service Responsibilities

#### TournamentMatchOperationsService
- Create individual matches
- Update match results
- Validate match creation parameters
- No score management or tournament state management

#### TournamentStateValidator
- Validate tournament state transitions
- Check if operations are allowed in current state
- Provide validation rules for each tournament stage

#### TournamentScoreManager
- Calculate and update group scores
- Maintain group standings
- Track player statistics
- No direct match operations

#### TournamentBracketManager
- Create and manage playoff brackets
- Handle match linking and advancement
- Manage bracket visualization data
- No tournament state management

#### TournamentMatchService
- Coordinate between match operations and score management
- Handle tournament stage transitions
- Manage match creation and updates at a higher level
- Delegate specific operations to specialized services

#### TournamentPlayoffService
- Manage playoff stage progression
- Create and update playoff brackets via bracket manager
- Handle third-place match creation
- Process playoff match results and advancement

#### TournamentGroupService
- Create and manage groups
- Handle group stage progression
- Use match operations service for core functionality
- Use score manager for standings and statistics

### Dependency Flow

# Service Dependencies and Flow

## Command/UI Layer
TournamentManagementGroup (command handler)
├─> TournamentService
├─> TournamentManagerService
├─> TournamentMatchService
├─> TournamentPlayoffService
└─> TournamentGroupService

AdminThirdPlaceMatchHandler
├─> TournamentService
└─> TournamentPlayoffService

MapBanModalHandler, MapBanHandler, GameResultHandler
└─> TournamentMatchService

## Service Layer
TournamentService
├─> TournamentPlayoffService
└─> TournamentGroupService

TournamentManagerService
├─> TournamentMatchService
├─> TournamentPlayoffService
└─> TournamentGroupService

## Core Services
TournamentMatchService
├─> ITournamentMatchOperationsService
├─> ITournamentScoreManager
└─> ITournamentStateValidator

TournamentPlayoffService
├─> ITournamentMatchOperationsService
├─> ITournamentStateValidator
└─> ITournamentBracketManager

TournamentGroupService
├─> ITournamentMatchOperationsService
├─> ITournamentScoreManager
└─> ITournamentStateValidator

## Base Services (No Dependencies)
├─> TournamentMatchOperationsService
├─> TournamentScoreManager
├─> TournamentStateValidator
└─> TournamentBracketManager

### Key Design Principles
1. **Separation of Concerns**:
   - Match operations are isolated from score management
   - State validation is independent of business logic
   - Bracket management is separate from tournament state
   - Higher-level services coordinate between specialized services

2. **Dependency Direction**:
   - Dependencies flow from high-level to low-level services
   - Core operations have no cross-dependencies
   - Validation and management services are independent
   - Coordination happens at higher levels

3. **Service Boundaries**:
   - Clear separation between match operations and score management
   - State validation is centralized and consistent
   - Playoff and group services focus on their specific domains
   - Match service handles cross-cutting concerns
