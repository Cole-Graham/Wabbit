# Map Code Refactoring

## Original Prompt, Thinking, and Analysis

**Prompt**: "Can you analyze the entire TournamentMapService file, and tell me if any of the code seems redundant. If necessary, search for and investigate how it integrates into the MatchStatusService and other code related to tournament match proceedings, for better context."

**Thinking**:
Let me analyze the TournamentMapService.cs file to identify any redundant code, and then investigate its integration with MatchStatusService and other tournament-related code.
First, I'll analyze the TournamentMapService.cs file for redundancy:
1. GetTournamentMapPool(bool oneVOne) - This method retrieves maps from the map collection based on the match type (1v1 or team). It has proper error handling and fallbacks. This seems like a core method without obvious redundancy.

2. GetRandomMap(bool oneVOne) - This method gets a random map from the tournament pool. It uses GetTournamentMapPool, which is appropriate reuse.

3. GetRandomMaps(bool oneVOne, int count) - This gets multiple random maps. It also uses GetTournamentMapPool which is good. It handles edge cases where the requested count exceeds the available maps.

4. GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength, Round? round = null) - This generates a map list for a match considering team bans. It's complex but doesn't appear redundant.

5. GetAvailableMapsForNextGame(Round round) - This gets available maps for the next game in a match, considering banned and played maps. It uses GenerateMapList to handle bans, which is appropriate reuse.

6. ValidateMapBans(List<string> mapBans, bool oneVOne) - This validates map ban selections. It also uses GetTournamentMapPool, which is good reuse.

7. GenerateMapListBo1, GenerateMapListBo3, GenerateMapListBo5 - These are convenience methods that all call GenerateMapList with different match lengths. This is fine since it provides a more semantic API.

8. GetRandomMapWithVisualization() - This gets a random map with visualization data. It doesn't reuse GetRandomMap which might be a redundancy.

9. GetMapByName(string mapName) - Simple utility method without redundancy.

10. GetMapThumbnailAsync(string mapName) - This is a stub method that needs implementation.

11. GetRandomMapForNextGame(Round round) - This gets a random map for the next game. It uses GetAvailableMapsForNextGame, which is appropriate.

Now, looking for potential redundancy:

1. There might be some redundancy between GetRandomMapForNextGame and GetRandomMap. They serve similar purposes but operate at different levels.

2. The TournamentGameService has a GetAvailableMapsForNextGame method that seems to duplicate functionality in TournamentMapService. This was probably the issue that was fixed in our recent changes.

3.The parameter inconsistency in the map list generation methods might cause confusion: some use List<string>? customMapPool while the main method doesn't have this parameter.

Now, let's examine how TournamentMapService integrates with MatchStatusService:

Looking at the code for MatchStatusService.cs, I see that it uses ITournamentMapService for map-related operations. Specifically, it calls the following methods:
`GetRandomMapForNextGame` in UpdateMapInformationAsync
It might use other methods in the service for map operations

The integration between these services is now cleaner after our recent changes where we moved the GetRandomMapForNextGame functionality from TournamentGameService to TournamentMapService and updated the interfaces to reflect this change.

Let's see if there's any remaining redundancy:
1. The GetAvailableMapsForNextGame method in TournamentMapService is quite complex, while the same method in TournamentGameService (which we updated to delegate to TournamentMapService) had a simpler implementation. This might indicate some redundancy or different approaches to the same problem.
2. The GenerateMapList method in TournamentMapService has some complex logic for handling conditional bans that might be redundant if similar logic exists elsewhere.

Overall, the main redundancy I see is between the map-related functionality in TournamentGameService and TournamentMapService, which we've already addressed by having TournamentGameService delegate to TournamentMapService. The convenience methods (GenerateMapListBo1, etc.) introduce a slight redundancy but provide a more semantic API which is a reasonable trade-off.

**Analysis**:
After analyzing the TournamentMapService code and its integration with other components, I've identified several areas of redundancy and potential improvements:

1. **Duplicate Map Functionality in Multiple Services:**
   - Both `TournamentMapService` and `TournamentGameService` implement the `GetAvailableMapsForNextGame` method. This is redundant and could lead to inconsistent behavior.
   - The recent fix where `TournamentGameService.GetRandomMapForNextGame` now delegates to `TournamentMapService` is a good step, but the duplicate implementation of `GetAvailableMapsForNextGame` should also be addressed.

2. **Overlapping Utilities:**
   - `RandomMapExt.cs` contains methods like `GetRandomMaps` that overlap with functionality in `TournamentMapService` (e.g., `GetRandomMaps`).
   - `MapBanExt.cs` implements `GenerateMapListBo1`, `GenerateMapListBo3`, and `GenerateMapListBo5` which are similar to those in `TournamentMapService`.

3. **Inconsistent Parameter Naming:**
   - `MapBanExt` uses `OvO` for one-vs-one parameter, while `TournamentMapService` uses `oneVOne`.
   - `MapBanExt` uses `customMapPool` parameter, while `TournamentMapService.GenerateMapList` doesn't have this parameter (but other methods do).

4. **Method Duplication in TournamentMapService:**
   - The `GenerateMapListBo1`, `GenerateMapListBo3`, and `GenerateMapListBo5` methods in `TournamentMapService` all just call `GenerateMapList` with different match lengths. While this provides a cleaner API, it adds some maintenance overhead.

5. **Random Number Generation:**
   - `TournamentMapService` creates its own `Random` instance, while other services like `RandomMapExt` inject an `IRandomProvider`. Inconsistent approaches to randomization could lead to unpredictable behavior.

6. **Conditional Ban Logic Duplication:**
   - Complex conditional ban logic appears in both `TournamentMapService.GenerateMapList` and `MapBanExt` methods. This logic should be centralized.

7. **Error Handling Inconsistency:**
   - `TournamentMapService` uses extensive try-catch blocks with logging, while `MapBanExt` and `RandomMapExt` use direct console output for errors.

8. **Unimplemented Method:**
   - `GetMapThumbnailAsync` in `TournamentMapService` is a stub method that returns null values.

The most pressing issue is the duplicate implementation of `GetAvailableMapsForNextGame` in `TournamentGameService`, which should be removed and all code updated to use the implementation in `TournamentMapService`.

## Additional Analysis: Basic Commands vs Tournament Functionality

After examining the `BasicGroup.cs` class and its `random_map` command implementation, I've found several areas of concern regarding the separation between casual map selection and tournament map functionality:

1. **Overlapping Visualization Logic:**
   - The `random_map` command in `BasicGroup.cs` creates an embed with map details and handles map thumbnails (as URLs or local files).
   - The `GetRandomMapWithVisualization()` method in `TournamentMapService` performs a similar function but doesn't share code with the command implementation.

2. **Different Random Map Selection Implementations:**
   - `BasicGroup.cs` uses `IRandomMapExt.GetRandomMap()` to get a random map for casual play.
   - `TournamentMapService` has its own `GetRandomMap(bool oneVOne)` method for tournament maps.
   - These implementations could diverge in behavior, especially as features are added to one but not the other.

3. **Inconsistent Error Handling:**
   - The error handling in `BasicGroup.cs` commands uses different patterns than `TournamentMapService`.
   - Some errors are logged to console in `BasicGroup.cs` while `TournamentMapService` uses proper logger injection.

4. **Local File Path Handling Duplication:**
   - Both `BasicGroup.cs` and potentially tournament-related code handle local file paths for map thumbnails, but with different implementations.

5. **Lack of Common Utilities:**
   - Common operations like creating an embed for a map or handling map thumbnails are duplicated across different areas of the codebase.

## Map Thumbnail Handling and Temporary Display

Looking at the code for handling map thumbnails, I've identified shared functionality that should be centralized:

1. **Thumbnail Path Resolution:**
   - `BasicGroup.cs` has logic to handle both URL and local file thumbnails
   - This logic includes path normalization for different operating systems
   - Similar functionality is needed in tournament contexts

2. **Temporary Thumbnail Display:**
   - Tournament code needs to temporarily display thumbnails in match threads (for 5 minutes)
   - This could be a useful feature for casual commands as well
   - Implementing a shared service for timed message deletion would be beneficial

3. **Thumbnail Embedding:**
   - Both contexts need to create DiscordEmbedBuilders with thumbnails
   - The attachment logic for local files is duplicated
   - The URL handling is duplicated

4. **Error Handling:**
   - Both implementations need to handle missing files and invalid paths
   - Currently they use different approaches

## Map Selection Logic for Tournaments and Casual Play

The tournament code has sophisticated map selection logic that could be leveraged for casual commands:

1. **Map Pool Filtering:**
   - `TournamentMapService.GetTournamentMapPool()` provides maps based on match type
   - This could be generalized for both tournament and casual contexts

2. **Banned and Played Maps Tracking:**
   - Tournament code tracks banned maps and already-played maps
   - This could be useful for casual sequential games as well

3. **Random Map Selection:**
   - Both contexts need random map selection with different constraints
   - The tournament implementation has more robust error handling

4. **Map Validation:**
   - `ValidateMapBans()` could be generalized for any context requiring map validation

## Separate Map Pools: Tournament vs Random

After examining `Maps.json`, it's clear that there are intentionally separate map pools:

1. **Tournament Map Pool (`IsInTournamentMapPool`):**
   - Maps that are officially sanctioned for competitive tournament play
   - Used by `TournamentMapService.GetTournamentMapPool()`
   - These maps are carefully balanced and tested

2. **Random Map Pool (`IsInRandomMapPool`):**
   - Maps available for casual play via the `random_map` command
   - Used by `RandomMapExt.GetRandomMap()`
   - May include experimental maps or maps rotated out of tournament play
   - Provides variety for casual players

This separation is an intentional design decision rather than a code duplication issue. Our refactoring should maintain this separation while still sharing common infrastructure where appropriate.

## Refactoring Proposal Checklist

### 1. Create Common Map Service in ServiceHelpers

- [x] Create a new `IMapService` interface in `Wabbit/Services/Interfaces/IMapService.cs` for shared map operations
- [x] Implement `MapService` in `Wabbit/Services/ServiceHelpers/MapService.cs` 
- [x] Add methods for handling both URL and local file thumbnails
- [x] Implement a method for creating embeds with map thumbnails: `CreateMapEmbed(Map map, string? description = null)`
- [x] Add support for temporary thumbnail display with auto-deletion after 5 minutes
- [x] Move path normalization logic from `BasicGroup.cs` to this service
- [x] Add proper error handling with logger injection
- [x] Create a static class `MapUtilities` in the same file for pure utility functions

### 2. Standardize Random Number Generation

- [x] Inject `IRandomProvider` into `TournamentMapService` constructor
- [x] Replace direct `new Random()` usage with the injected provider
- [x] Update `RandomMapExt` to consistently use the same `IRandomProvider`
- [x] Address thread-safety issues by using the same randomization approach everywhere

### 3. Clarify Interface Definitions

- [x] Update `ITournamentMapService` in `Wabbit/Services/Interfaces/ITournamentMapService.cs` to clearly indicate tournament context
- [x] Update `IRandomMapExt` in `Wabbit/Services/Interfaces/IRandomMapExt.cs` to clearly indicate casual/random context
- [x] Ensure each interface has clear XML documentation describing its purpose

### 4. Refactor Map Services with Clear Responsibilities

- [x] Update `TournamentMapService` to focus solely on tournament-specific operations
- [x] Update `RandomMapExt` to focus on casual map selection
- [x] Ensure proper dependency injection and constructor parameters
- [x] Update `Program.cs` to register all services correctly

### 5. Remove Duplicate Map Functions from TournamentGameService

- [x] Remove `GetAvailableMapsForNextGame` from `TournamentGameService`
- [x] Remove this method from `ITournamentGameService` interface
- [x] Update all references to call `TournamentMapService.GetAvailableMapsForNextGame` instead
- [x] Ensure `TournamentGameService.GetRandomMapForNextGame` properly delegates to `TournamentMapService`

### 6. Implement new "Scrimmage" system, to replace "regular_1v1" command.

#### 6.1 Create basic infrastructure for scrimmage system
- [x] Create a `RequireUserAttribute` for the whitelist system to restrict access to the scrimmage commands (implemented but commented out pending DSharpPlus compatibility fixes)
- [x] Update BotConfig.cs and ConfigManager.cs to support the new dedicated channel with `ScrimmageChannelId`
- [x] Create a new `Scrimmage.cs` model file to represent scrimmage matches
- [x] Update `OngoingRounds.cs` to track scrimmage matches

#### 6.2 Implement scrimmage services and commands
- [x] Create new `IScrimmageStatusService` interface and `ScrimmageStatusService` implementation
- [x] Create a new `ScrimmageGroup.cs` command file with the scrimmage commands
- [x] Remove the "regular_1v1" command from BasicGroup.cs
- [x] Add and register all new services in Program.cs

#### 6.3 Enhance scrimmage features
- [x] Create a new command called "scrimmage", with the following parameters and options:
  -GameType: 1v1, 2v2, 3v3, 4v4 (implemented as ScrimmageType to avoid naming conflicts with Tournament's GameType)
  -MatchLength: Bo1, Bo3, Bo5
  -Opponent(s): select discord user if 1v1, otherwise select team
  -Rated: True or False
  -TournamentMapPool: Auto-defaults to True for rated matches, but configurable by the user for true/false in unrated matches.
- [x] Add methods to initiate and execute scrimmage procedures, with the following design specs:
  -Create a new `ScrimmageStatusService` and code for a dynamic and interactive scrimmage status embed
  -Reuse as much of the MatchStatusEmbed code as possible, possibly refactor common code to a new `MatchStatusHelper`
   file created in ServiceHelpers.

> Note: The Scrimmage system has been fully implemented. The build is successful after resolving the naming conflict between GameType and ScrimmageType. The scrimmage system can now handle different game types (1v1, 2v2, 3v3, 4v4), match lengths (Bo1, Bo3, Bo5), and supports both rated and unrated matches with appropriate map pool selection.

### 7. Implement new ratings system for rated scrimmages, and team registration.

#### 7.1 Create data models and storage
- [x] Create a `Team.cs` model to represent teams with the following properties:
  - TeamId (unique identifier)
  - TeamName (with validation for uniqueness)
  - TeamGameType (1v1, 2v2, 3v3, 4v4)
  - CorePlayers (unalterable members)
  - SecondaryPlayers (changeable monthly)
  - SubstitutePlayers (changeable weekly)
  - Rating (ELO rating, starting at 1200)
  - LastNameChangeDate (for enforcing cooldowns)
  - LastSecondaryChangeDate (for enforcing cooldowns)
  - LastSubstituteChangeDate (for enforcing cooldowns)
  - CreationDate
  - Records (wins, losses)
  - Team composition requirements:
    - 1v1 Teams: 1 core player, 0 secondary players, 0 substitute players
    - 2v2 Teams: 2 core players, up to 1 secondary player, 0 substitute players
    - 3v3 Teams: 2 core players, up to 1 secondary player, up to 1 substitute player
    - 4v4 Teams: 3 core players, up to 1 secondary player, up to 1 substitute player
  - Cooldown periods:
    - Team name changes: Weekly cooldown (tracked via LastNameChangeDate)
    - Secondary player changes: Monthly cooldown (tracked via LastSecondaryChangeDate)
    - Substitute player changes: Weekly cooldown (tracked via LastSubstituteChangeDate)
- [x] Create a `PlayerRating.cs` model to track individual player ratings
  - PlayerId (Discord userId)
  - Username (for display)
  - Ratings (dictionary mapping game types to ratings)
  - Records (dictionary mapping game types to win/loss records)
- [x] Create a `Season.cs` model to represent competitive seasons
  - SeasonId
  - SeasonName
  - StartDate
  - EndDate
  - IsActive
- [x] Define appropriate JSON schemas for storing:
  - teams.json - storing team configurations
  - ratings.json - storing player and team ratings
  - seasons.json - storing season information

#### 7.2 Implement repository services
- [x] Create TeamRepositoryService and RatingRepositoryService
- [x] Implement JSON serialization/deserialization 
- [x] Add CRUD operations with proper validation

#### 7.3 Implement state services
- [x] Create `ITeamStateService` interface in `Wabbit/Services/Interfaces/ITeamStateService.cs`
- [x] Implement `TeamStateService` in `Wabbit/Services/TeamStateService.cs`
  - Track active teams and user memberships
  - Manage state transitions (player changes, cooldowns)
  - Validate team operations
- [x] Create `ISeasonService` interface in `Wabbit/Services/Interfaces/ISeasonService.cs`
- [x] Implement `SeasonService` in `Wabbit/Services/SeasonService.cs`
  - Methods for creating, ending, and transitioning between seasons
  - Archiving previous season data
  - Resetting ratings for new seasons

#### 7.3a Refactor GameType enums with Source Generator approach
- [x] Create a new project called `Wabbit.SourceGenerators` in the solution
  - Add package references: `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.Analyzers`
  - Configure project as a source generator (SDK="Microsoft.NET.Sdk")
  - Add `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>` to the project file
  - Set the output type to library

- [x] Define a base enum model in `Wabbit/Models/GameType.cs`:
  ```csharp
  namespace Wabbit.Models;
  
  [GenerateSpecializedEnums("Scrimmage", "Signup", "Team", "Tournament")]
  public enum GameType
  {
      [Display(Name = "1v1")]
      OneVOne,
      
      [Display(Name = "2v2")]
      TwoVTwo,
      
      [Display(Name = "3v3")]
      ThreeVThree,
      
      [Display(Name = "4v4")]
      FourVFour
  }
  ```

- [x] Create a source generator in the new project:
  - Create `GameTypeEnumGenerator.cs` with the `[Generator]` attribute
  - Implement `ISourceGenerator` interface
  - Create attribute class `GenerateSpecializedEnumsAttribute` for marking base enums
  - Write code to detect and process enums decorated with this attribute
  - Generate the specialized enum types in their appropriate namespaces:
    ```csharp
    // Generated file example
    namespace Wabbit.Models
    {
        public enum GameType { OneVOne, TwoVTwo, ThreeVThree, FourVFour }
        public enum ScrimmageGameType { OneVOne, TwoVTwo, ThreeVThree, FourVFour }
    }
    namespace Wabbit.Models.Rating
    {
        public enum RatingGameType { OneVOne, TwoVTwo, ThreeVThree, FourVFour }
    }
    ```
  - Generate extension methods for conversions between all enum types
  - Generate helper methods for getting player counts, display names, etc.

- [x] Add reference to the source generator project in the main Wabbit project:
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\Wabbit.SourceGenerators\Wabbit.SourceGenerators.csproj" 
                      OutputItemType="Analyzer" 
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
  ```

- [x] Update code to use the generated enum types:
  - [x] Update `Tournament.cs` to use `TournamentGameType` instead of `GameType`
  - [x] Update `TournamentSignup.cs` to use `SignupGameType` instead of `GameType`
  - [x] Update `Scrimmage.cs` to use `ScrimmageGameType` instead of `ScrimmageType`
  - [x] Update all services to use the appropriate enum types
  - [x] Use the generated extension methods for conversions between domains

- [ ] Create helper extension methods in the source generator:
  - [ ] `GetPlayerCount()` - Returns the number of players based on game type
  - [ ] `GetDisplayName()` - Returns the friendly display name ("1v1", "2v2", etc.)
  - [ ] `GetDescription()` - Returns the full description for the game type
  - [ ] `ToScrimmageGameType()`, `ToRatingGameType()`, etc. - Convert between enum types
  - [ ] `GetMaxTeamSize()` - Returns the number of players per team

- [ ] Update serialization handling:
  - [ ] Ensure all enums serialize to the same integer values
  - [ ] Add custom JsonConverter if needed to handle deserialization of old values

- [ ] Create unit tests for the enum conversions and helpers
  - [ ] Test all conversion methods between enum types
  - [ ] Test that serialization/deserialization works correctly
  - [ ] Test that helper methods return correct values

#### 7.4 Create team registration system and commands
- [x] Create a new `TeamGroup.cs` command file with the following commands:
  - [x] `create_team` - Create a new team with name validation
  - [x] `join_team` - Join an existing team (with appropriate role checks)
  - [x] `leave_team` - Leave a team (with restrictions for core players)
  - [x] `change_name` - Change team name (with weekly cooldown)
  - [x] `change_secondary` - Change secondary player (with monthly cooldown)
  - [x] `change_substitute` - Change substitute player (with weekly cooldown)
  - [x] `list_teams` - List all registered teams
  - [x] `team_info` - Show detailed information about a team
  - [x] `my_teams` - Show teams the user belongs to
- [ ] Implement proper permission checks using RequireUserAttribute
- [x] Add channel verification for team commands
- [x] Implement cooldown tracking for team modifications

#### 7.5 Implement ratings system
- [ ] Create `IRatingService` interface in `Wabbit/Services/Interfaces/IRatingService.cs`
- [ ] Implement `RatingService` in `Wabbit/Services/RatingService.cs` with:
  - ELO calculation for matches (simple ELO system starting at 1200)
  - Separate rating tracking for each game type (1v1, 2v2, 3v3, 4v4)
  - K-factor adjustment for longer match lengths (i.e. bo5 would reward more ELO than bo3)
  - Support for team vs team rating calculations
  - Methods to update ratings after scrimmage completion
  - Rating history tracking
- [ ] Update ScrimmageStatusService to integrate with RatingService
  - Call rating updates when scrimmages are completed
  - Only update ratings for rated matches
  - Handle player/team rating changes

#### 7.6 Implement seasonal support and leaderboard system
- [x] Add commands to TeamGroup.cs for season management (admin only):
  - [x] `create_season` - Start a new season with a name
  - [x] `end_season` - End the current season and archive data
  - [x] `reset_ratings` - Reset all ratings to 1200 at start of new season
  - [x] `list_seasons` - Show all seasons and their status
  - [x] `season_info` - Show details about a specific season
- [x] Create a comprehensive leaderboard system:
  - [x] Create `ILeaderboardService` interface in `Wabbit/Services/Interfaces/ILeaderboardService.cs`
  - [x] Implement `LeaderboardService` in `Wabbit/Services/LeaderboardService.cs`
  - [x] Build dynamic embeds for each GameType leaderboard
  - [x] Implement pagination buttons to navigate through rankings (25 entries per page)
  - [x] Add season selection dropdown to view leaderboards from different seasons
  - [x] Create game type buttons to switch between 1v1, 2v2, 3v3, and 4v4 leaderboards
  - [x] Implement caching to improve performance (15-minute cache)
- [x] Add leaderboard commands to SeasonGroup.cs:
  - [x] `leaderboard` - View the current season's leaderboard for a specific game type
  - [x] `my_rank` - View personal ranking information
  - [x] `team_rank` - View a specific team's ranking information
- [x] Create LeaderboardHandler.cs to process component interactions:
  - [x] Handle pagination button presses
  - [x] Handle game type button presses
  - [x] Handle season dropdown selection
  - [x] Implement proper error handling for component interactions

#### 7.7 Seventh, create admin commands
  - [ ] Implement privileged team and rating management
    - [ ] Add admin commands to TeamGroup.cs:
      - [ ] `admin_edit_team` - Edit any aspect of a team
      - [ ] `admin_set_rating` - Manually set a player or team's rating
      - [ ] `admin_reset_cooldown` - Reset cooldowns for name/player changes
      - [ ] `admin_reset_all_cooldowns` - Reset all cooldowns with exception support
      - [ ] `admin_remove_team` - Remove a team entirely
      - [ ] `admin_merge_teams` - Merge two teams together
      - [ ] `admin_transfer_ownership` - Transfer team ownership
  - [ ] Add cooldown reset functionality
  - [ ] Create audit logging for admin actions
  - [ ] Create audit trail for admin rating modifications
  
#### 7.8 Eighth, integrate with tournament system
  - [ ] Update tournament signup to use teams
    - [ ] Modify `TournamentSignupService` to accept team IDs for registration
    - [ ] Validate team membership during signup
    - [ ] Handle team name display in tournament brackets
  - [ ] Enhance displays to show team information
    - [ ] Update match status embeds to display team names
    - [ ] Show team ratings in tournament information
    - [ ] Display team members in match threads
  - [ ] Implement tournament-specific ratings
    - [ ] Add tournament-specific rating adjustments using k-factors
    - [ ] Store tournament ratings separately from scrimmage ratings
    - [ ] Adjust k-factors based on tournament stage

### 8. Standardize Parameter Names

- [ ] Use consistent naming for parameters (`oneVOne` vs `OvO`) across all services
- [ ] Update `MapBanExt` to match parameter naming in `TournamentMapService`
- [ ] Add missing parameters where needed (e.g., `customMapPool` in `GenerateMapList`)
- [ ] Ensure parameter documentation is consistent across all interfaces and implementations

### 9. Improve Error Handling

- [ ] Replace `Console.WriteLine` calls in helper classes with logger calls
- [ ] Ensure consistent error handling patterns across all map-related services
- [ ] Add more detailed error information in log messages
- [ ] Standardize how errors are presented to users across both casual and tournament contexts

### 10. Address Method Duplication

- [ ] Evaluate if the Bo1/Bo3/Bo5 wrapper methods add enough value to keep
- [ ] Consider adding XML documentation explaining that they're convenience methods
- [ ] Ensure parameter signatures are consistent between these methods
- [ ] Extract common logic for map visualization into shared methods

### 11. Update Command Implementation

- [ ] Update `BasicGroup.cs` to use new shared utilities
- [ ] Implement temporary thumbnail display for the `random_map` command
- [ ] Add optional parameters to allow for map filtering in commands
- [ ] Add a new command to allow starting casual sequential games with banned maps

### 12. Add Performance Optimizations

- [ ] Implement caching for frequently accessed data like map pools
- [ ] Add a `MapPoolCache` class that can be invalidated when maps change
- [ ] Optimize thumbnail handling to prevent loading the same thumbnail multiple times
- [ ] Use async methods consistently for I/O operations

### 13. Update Architecture Diagram

- [ ] Update `wabbit-bot-architecture.puml` to reflect the new structure
- [ ] Add the relationship between map services and other components
- [ ] Document the separation between tournament and casual map functionality
- [ ] Clarify the flow of map operations in both casual and tournament contexts

## Implementation Strategy

1. ✅ First, create the common map service in ServiceHelpers
   - ✅ Create the interface and implementation
   - ✅ Implement thumbnail handling with temporary display (5 min timeout)
   - ✅ Add path normalization for cross-platform support

2. ✅ Second, standardize random number generation
   - ✅ Inject `IRandomProvider` consistently
   - ✅ Eliminate direct `Random` instantiation

3. Third, clarify interface definitions
   - [x] Update `ITournamentMapService` in `Wabbit/Services/Interfaces/ITournamentMapService.cs` to clearly indicate tournament context
   - [x] Update `IRandomMapExt` in `Wabbit/Services/Interfaces/IRandomMapExt.cs` to clearly indicate casual/random context
   - [x] Ensure each interface has clear XML documentation describing its purpose

4. Fourth, refactor map services
   - [x] Update `TournamentMapService` to focus solely on tournament-specific operations
   - [x] Update `RandomMapExt` to focus on casual map selection
   - [x] Ensure proper dependency injection and constructor parameters
   - [x] Update `Program.cs` to register all services correctly

5. Fifth, eliminate the duplicate implementation in `TournamentGameService`
   - [x] Remove `GetAvailableMapsForNextGame` from `TournamentGameService`
   - [x] Remove this method from `ITournamentGameService` interface
   - [x] Update all references to call `TournamentMapService.GetAvailableMapsForNextGame` instead
   - [x] Ensure `TournamentGameService.GetRandomMapForNextGame` properly delegates to `TournamentMapService`

6. Sixth, implement new Scrimmage system
   - 6.1 Create infrastructure for scrimmage system
     - [x] Create a `RequireUserAttribute` for the whitelist system to restrict access to the scrimmage commands (implemented but commented out pending DSharpPlus compatibility fixes)
     - [x] Update BotConfig.cs and ConfigManager.cs to support the new dedicated channel with `ScrimmageChannelId`
     - [x] Create a new `Scrimmage.cs` model file to represent scrimmage matches
     - [x] Update `OngoingRounds.cs` to track scrimmage matches
     
   - 6.2 Implement scrimmage services and commands
     - [x] Create new `IScrimmageStatusService` interface and `ScrimmageStatusService` implementation
     - [x] Create a new `ScrimmageGroup.cs` command file with the scrimmage commands
     - [x] Remove the "regular_1v1" command from BasicGroup.cs
     - [x] Add and register all new services in Program.cs

   - 6.3 Enhance scrimmage features
     - [x] Create a new command called "scrimmage", with the following parameters and options:
       -GameType: 1v1, 2v2, 3v3, 4v4 (implemented as ScrimmageType to avoid naming conflicts with Tournament's GameType)
       -MatchLength: Bo1, Bo3, Bo5
       -Opponent(s): select discord user if 1v1, otherwise select team
       -Rated: True or False
       -TournamentMapPool: Auto-defaults to True for rated matches, but configurable by the user for true/false in unrated matches.
     - [x] Add methods to initiate and execute scrimmage procedures, with the following design specs:
       -Create a new `ScrimmageStatusService` and code for a dynamic and interactive scrimmage status embed
       -Reuse as much of the MatchStatusEmbed code as possible, possibly refactor common code to a new `MatchStatusHelper`
        file created in ServiceHelpers.

7. Seventh, implement ratings and team system
   - 7.1 First, create the data models and storage
     - [x] Define Team and PlayerRating models
     - [x] Design JSON schemas for persistent storage
     - [x] Implement validation logic for team operations
     
   - 7.2 Second, implement repository services
     - [x] Create TeamRepositoryService and RatingRepositoryService
     - [x] Implement JSON serialization/deserialization 
     - [x] Add CRUD operations with proper validation
     
   - 7.3 Third, implement state services
     - [x] Create TeamStateService for runtime state management
     - [x] Implement SeasonService for managing competitive seasons
     - [x] Add state transition validation and tracking
   
   - 7.3a Refactor GameType enums with Source Generator approach
     - [x] Create the Wabbit.SourceGenerators project with necessary configurations
     - [x] Define the GameType enum with appropriate attributes
     - [x] Implement the source generator to produce specialized enum types
     - [x] Update all code to use the specialized enum types
     - [x] Add helper methods and conversion utilities
     - [x] Ensure proper serialization/deserialization handling
     - [x] Write unit tests to verify correct behavior
     
   - 7.4 Fourth, create team registration commands
     - [x] Implement user-facing commands for team management
     - [x] Add cooldown enforcement for team modifications
     - [x] Create team information and listing commands
     
   - 7.5 Fifth, implement the ratings system
     - [ ] Create RatingService with ELO calculations
     - [ ] Integrate ratings with scrimmage completions
     - [ ] Implement separate ratings for each game type
     
   - 7.6 Sixth, implement seasonal functionality and leaderboard system
     - [x] Add commands to TeamGroup.cs for season management (admin only):
       - [x] `create_season` - Start a new season with a name
       - [x] `end_season` - End the current season and archive data
       - [x] `reset_ratings` - Reset all ratings to 1200 at start of new season
       - [x] `list_seasons` - Show all seasons and their status
       - [x] `season_info` - Show details about a specific season
     - [x] Create a comprehensive leaderboard system:
       - [x] Create `ILeaderboardService` interface in `Wabbit/Services/Interfaces/ILeaderboardService.cs`
       - [x] Implement `LeaderboardService` in `Wabbit/Services/LeaderboardService.cs`
       - [x] Build dynamic embeds for each GameType leaderboard
       - [x] Implement pagination buttons to navigate through rankings (25 entries per page)
       - [x] Add season selection dropdown to view leaderboards from different seasons
       - [x] Create game type buttons to switch between 1v1, 2v2, 3v3, and 4v4 leaderboards
       - [x] Implement caching to improve performance (15-minute cache)
     - [x] Add leaderboard commands to SeasonGroup.cs:
       - [x] `leaderboard` - View the current season's leaderboard for a specific game type
       - [x] `my_rank` - View personal ranking information
       - [x] `team_rank` - View a specific team's ranking information
     - [x] Create LeaderboardHandler.cs to process component interactions:
       - [x] Handle pagination button presses
       - [x] Handle game type button presses
       - [x] Handle season dropdown selection
       - [x] Implement proper error handling for component interactions

   - 7.7 Seventh, create admin commands
     - [ ] Implement privileged team and rating management
       - [ ] Add admin commands to TeamGroup.cs:
         - [ ] `admin_edit_team` - Edit any aspect of a team
         - [ ] `admin_set_rating` - Manually set a player or team's rating
         - [ ] `admin_reset_cooldown` - Reset cooldowns for name/player changes
         - [ ] `admin_reset_all_cooldowns` - Reset all cooldowns with exception support
         - [ ] `admin_remove_team` - Remove a team entirely
         - [ ] `admin_merge_teams` - Merge two teams together
         - [ ] `admin_transfer_ownership` - Transfer team ownership
       - [ ] Add cooldown reset functionality
       - [ ] Create audit logging for admin actions
       - [ ] Create audit trail for admin rating modifications
       
   - 7.8 Eighth, integrate with tournament system
     - [ ] Update tournament signup to use teams
       - [ ] Modify `TournamentSignupService` to accept team IDs for registration
       - [ ] Validate team membership during signup
       - [ ] Handle team name display in tournament brackets
     - [ ] Enhance displays to show team information
       - [ ] Update match status embeds to display team names
       - [ ] Show team ratings in tournament information
       - [ ] Display team members in match threads
     - [ ] Implement tournament-specific ratings
       - [ ] Add tournament-specific rating adjustments using k-factors
       - [ ] Store tournament ratings separately from scrimmage ratings
       - [ ] Adjust k-factors based on tournament stage

8. Eighth, standardize parameter names
   - [ ] Use consistent naming for parameters across all services
   - [ ] Update helpers to match parameter naming in main services
   - [ ] Add missing parameters where needed
   - [ ] Ensure parameter documentation is consistent

9. Improve Error Handling
   - [ ] Replace `Console.WriteLine` calls in helper classes with logger calls
   - [ ] Ensure consistent error handling patterns across all map-related services
   - [ ] Add more detailed error information in log messages
   - [ ] Standardize how errors are presented to users across both casual and tournament contexts

10. Address Method Duplication
    - [ ] Evaluate if the Bo1/Bo3/Bo5 wrapper methods add enough value to keep
    - [ ] Consider adding XML documentation explaining that they're convenience methods
    - [ ] Ensure parameter signatures are consistent between these methods
    - [ ] Extract common logic for map visualization into shared methods

11. Update Command Implementation
    - [ ] Update `BasicGroup.cs` to use new shared utilities
    - [ ] Implement temporary thumbnail display for the `random_map` command
    - [ ] Add optional parameters to allow for map filtering in commands
    - [ ] Add a new command to allow starting casual sequential games with banned maps

12. Add Performance Optimizations
    - [ ] Implement caching for frequently accessed data like map pools
    - [ ] Add a `MapPoolCache` class that can be invalidated when maps change
    - [ ] Optimize thumbnail handling to prevent loading the same thumbnail multiple times
    - [ ] Use async methods consistently for I/O operations

13. Update Architecture Diagram
    - [ ] Update `wabbit-bot-architecture.puml` to reflect the new structure
    - [ ] Add the relationship between map services and other components
    - [ ] Document the separation between tournament and casual map functionality
    - [ ] Clarify the flow of map operations in both casual and tournament contexts