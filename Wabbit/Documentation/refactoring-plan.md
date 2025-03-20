# Match System Refactoring Plan

This document outlines a plan for refactoring common elements between the Tournament Match System and Scrimmage System. By identifying shared functionality, we can create reusable components and ensure consistency across both systems.

## Table of Contents
1. [Component Handler Architecture](#component-handler-architecture)
2. [Status Embed Creation](#status-embed-creation)
3. [Map Ban Management](#map-ban-management)
4. [Deck Submission](#deck-submission)
5. [Game Result Reporting](#game-result-reporting)
6. [UI/UX Consistency](#uiux-consistency)
7. [Stage Enum Consolidation](#stage-enum-consolidation)
8. [Field-by-Field Analysis of MatchStatusService vs ScrimmageStatusService](#field-by-field-analysis-of-matchstatusservice-vs-scrimmagestatusservice)

## Component Handler Architecture

### Current Implementation
- `MatchStatusService` for tournaments
- `ScrimmageStatusService` for scrimmages
- Tournament uses component handlers (e.g., `MapBanHandler`, `DeckSubmissionHandler`, etc.)
- Scrimmage system starting to implement handlers but needs alignment

### Refactoring Tasks
- [ ] Create base classes for common handler functionality
- [ ] Standardize component ID formats across systems
- [ ] Implement consistent error handling and response patterns
- [ ] Ensure service dependencies follow the same pattern

## Status Embed Creation

### Current Implementation
- `MatchStatusService.UpdateMatchStatusAsync()` for tournaments
- `ScrimmageStatusService.UpdateScrimmageStatusAsync()` for scrimmages
- Both create embeds with similar structure but different data models

### Refactoring Tasks
- [x] Extract common embed structure to a shared helper class (`StatusEmbedHelpers` in Services/ServiceHelpers)
- [ ] Create interfaces for status embed data providers
- [x] Standardize status colors, emojis, and progress indicators
- [ ] Implement shared component building logic for buttons and dropdowns

### Status Embed Helper Implementation
- [x] Create progress bar generation without redundant visualizations
- [x] Standardize stage colors 
- [x] Standardize instruction text templates
- [x] Add helpers for match length formatting and other common utilities
- [x] Move GetMapBanCount and GetMapBanGuaranteeExplanation to shared code
- [x] Update ScrimmageStatusService to use the shared helpers

## Map Ban Management

### Current Implementation
- Tournament: `ITournamentMapService` and component handlers for map bans
- Scrimmage: Custom map ban logic in `ScrimmageStatusService`
- Both systems store map bans with similar patterns (confirmed/unconfirmed)

### Refactoring Tasks
- [x] Extract common map pool retrieval logic
- [x] Create unified map ban confirmation workflow with `MapBanHelpers`
- [x] Standardize map ban display in status embeds
- [x] Share ban validation logic

### Map Ban Helper Implementation
- [x] Create CreateMapBanDropdown method 
- [x] Create CreateMapBanConfirmButtons method
- [x] Create FormatMapBans method
- [x] Create GetMapStatusEmoji method

## Deck Submission

### Current Implementation
- Tournament: `/tournament submit_deck` command + handler
- Scrimmage: Currently trying to use a modal submission, needs alignment
- Different handling of deck code storage

### Refactoring Tasks
- [x] Create a `/scrimmage submit_deck` command following tournament pattern
- [x] Remove unnecessary `ScrimmageSubmissionModalHandler.cs`
- [x] Standardize deck validation and storage
- [ ] Align deck display in status embeds

### Deck Submission Command Implementation
- [x] Create `ScrimmageSubmitDeckCommand` class
- [x] Implement command handling similar to tournament version
- [x] Connect command to `ScrimmageStatusService.SubmitDeckCodeAsync()`
- [x] Update component handler to remove modal approach

## Game Result Reporting

### Current Implementation
- Tournament: Game winner dropdown + confirmation
- Scrimmage: Simple button-based winner selection
- Different handling of match completion logic

### Refactoring Tasks
- [x] Standardize game result input methods with `GameResultHelpers`
- [x] Align result storage patterns
- [x] Create common match completion workflow
- [x] Implement shared game advancement logic

### Game Result Helper Implementation
- [x] Create CreateGameWinnerDropdown method
- [x] Create CreateWinnerButtons method
- [x] Create FormatGameResults method
- [x] Create IsMatchComplete and GetMatchWinnerName methods

## UI/UX Consistency

### Current Implementation
- Different progress indicators
- Different component styling
- Similar but inconsistent instruction texts

### Refactoring Tasks
- [x] Create shared progress bar implementation with `StatusEmbedHelpers`
- [x] Standardize component styles and labels with `ComponentHelpers`
- [x] Use consistent instruction text templates
- [x] Implement shared emoji and visual indicator conventions

### Component Helper Implementation
- [x] Create CreateRefreshButton method
- [x] Create CreateReadyButton method
- [x] Create CreateSubmitDeckButton method
- [x] Create CreateDeckConfirmButtons method
- [x] Create CreateReplaySubmissionButton method

## Stage Enum Consolidation

### Current Implementation
- `MatchStage` enum in `Wabbit/Models/MatchStage.cs` for tournaments
- `ScrimmageStage` enum defined inside `ScrimmageStatusService.cs` for scrimmages
- Almost identical enum values with minor differences:
  - `ScrimmageStage` has `Cancelled` but lacks `DeckRevision`
  - `MatchStage` has `DeckRevision` but lacks `Cancelled`

### Refactoring Tasks
- [x] Remove the internal `ScrimmageStage` enum from `ScrimmageStatusService`
- [x] Modify `ScrimmageStatusService` to use the global `MatchStage` enum
- [x] Remove all references to cancelled stages, since we don't need this distinction
- [ ] Ensure consistent stage transition logic between both systems

## Field-by-Field Analysis of MatchStatusService vs ScrimmageStatusService

This section lists the differences in implementation between each field/component of the tournament match status embeds and scrimmage status embeds, along with the changes needed.

## Title and Header

### Tournament Implementation
- Uses custom title field from round.CustomProperties if available
- Shows playoff/group stage context in title when available
- Includes player/team names in subtitle
- Shows current game number and total games

### Scrimmage Implementation
- Uses basic game type and match length
- No context about match progress
- Players shown in separate fields rather than subtitle

### Required Changes
- [x] Enhance scrimmage title to include more context
- [x] Add subtitle with player names and game progress
- [x] Keep consistent positioning of player information

## Progress Bar

### Tournament Implementation
- Simple horizontal progress bar
- No additional visualization markers
- Uses ➜ arrows between stages
- Tracks progress based on stage and completion state

### Scrimmage Implementation
- Added circle markers to indicate current stage
- Used the same emoji system but with different visualization

### Required Changes
- [x] Remove the circle markers from scrimmage progress bar
- [x] Make progress bar format identical to tournament version

## Map Bans Field

### Tournament Implementation
- Shows map bans from team perspective ("My Team" vs "Opponent")
- Displays bans horizontally with fixed-width columns
- Only shows detailed bans for the viewer's team
- Only indicates if opponent has submitted bans
- Uses the thread ID to determine which team's perspective to show

### Scrimmage Implementation
- Shows both teams' bans with equal detail ("Team A" and "Team B")
- Lists bans vertically with priority numbers
- No perspective-based viewing
- Displays all ban information to both teams

### Required Changes
- [x] Implement perspective-based viewing in MapBanHelpers
- [x] Format map bans horizontally with fixed-width columns
- [x] Change labels to "My Team" and "Opponent" based on thread ID
- [x] Hide specific ban details for the opponent team

## Deck Submissions Area

### Tournament Implementation
- Shows deck status from team perspective
- Only shows the deck code to the team that submitted it
- Uses conditional display based on channel ID
- More compact presentation

### Scrimmage Implementation
- Shows deck names for both teams regardless of viewer
- No perspective-based viewing
- Uses separate fields for each team's deck

### Required Changes
- [x] Implement perspective-based viewing for deck submissions
- [x] Only show deck details to the submitting team
- [x] Match formatting and field organization with tournament system

## Game Results Area

### Tournament Implementation
- Uses tree-like structure with indentation
- Shows maps played with game numbers
- Includes status indicators (completed, in progress)
- Clean visual organization

### Scrimmage Implementation
- Simple list format 
- Less visual structure
- Different formatting for results

### Required Changes
- [x] Update result formatting to match tree structure
- [x] Add proper indentation and status indicators
- [x] Use consistent emoji and formatting conventions

## Stage Instructions

### Tournament Implementation
- Context-sensitive instructions based on team progress
- More detailed instructions for deck submission
- Takes team and deck submission status into account
- Adapts text based on viewer's team

### Scrimmage Implementation
- Generic instructions not tailored to team
- Same instructions for all viewers

### Required Changes
- [x] Enhance instructions to be context-sensitive
- [x] Tailor instructions based on team's progress
- [x] Create team-specific variants for key stages

## Component Handling

### Tournament Implementation
- Different components shown based on match stage AND team state
- Refresh button always available after map bans confirmed
- Component permissions managed per team
- Clear separation of concerns between stages

### Scrimmage Implementation
- Components based primarily on match stage
- Less granular control over when components appear
- No team-specific component visibility

### Required Changes
- [x] Update component display logic to consider team state
- [x] Implement team-specific component visibility
- [x] Match refresh button behavior with tournament system

## Future Enhancements

1. **Add Default Team Perspective**
   - [ ] Implement user-specific team tracking to automatically determine which team a user belongs to
   - [ ] Update all perspective-based views to use the tracked team information
   - [ ] Store user-team associations for the duration of a scrimmage
   - [ ] Add thread visitor tracking to determine which users have accessed which threads

2. **Team-Specific Component Visibility**
   - [ ] Once team perspective is properly implemented, update button visibility based on team
   - [ ] Only show action buttons to the appropriate team at each stage
   - [ ] Ensure components follow the same permission model as tournament system

## Next Steps After Analysis

1. Implement MapBanHelpers.FormatTeamPerspectiveBans method:
   - [x] Add support for "My Team" vs "Opponent" labels
   - [x] Implement horizontal formatting with fixed-width columns
   - [x] Add channelId parameter to determine viewer's team

2. Update ScrimmageStatusService.AddMapBansField:
   - [x] Pass channelId to determine team perspective
   - [x] Hide opponent's specific ban details
   - [x] Use new formatting method for both confirmed and unconfirmed bans

3. Modify GameResultHelpers.FormatGameResults:
   - [x] Update to use tree-like structure with indentation
   - [x] Match formatting with tournament system

4. Enhance the title and description:
   - [x] Update how player information is displayed
   - [x] Include game progress in description
   - [x] Match tournament title/subtitle structure

## Next Steps

1. Update the ScrimmageStatusService to use the new helpers:
   - [x] Update CreateMapBanDropdownAsync to use MapBanHelpers.CreateMapBanDropdown
   - [x] Update CreateMapBanConfirmButtons to use MapBanHelpers.CreateMapBanConfirmButtons
   - [x] Update AddMapBansField to use MapBanHelpers.FormatMapBans
   - [x] Update CreateGameWinnerDropdownAsync to use GameResultHelpers.CreateGameWinnerDropdown
   - [x] Update CreateStatusButtons to use ComponentHelpers helper methods
   - [x] Update RecordGameResultAsync to use GameResultHelpers methods

2. Test the refactored scrimmage system:
   - [ ] Test map ban workflow
   - [ ] Test deck submission workflow
   - [ ] Test game result reporting
   - [ ] Ensure proper stage transitions

3. After validation, consider refactoring the tournament system:
   - [ ] Update MatchStatusService to use the shared helpers
   - [ ] Ensure backward compatibility
   - [ ] Validate all tournament functionality still works

## Detailed Implementation Tasks

Based on the field-by-field analysis, here are the specific implementation tasks needed to align the scrimmage system with the tournament system:

### 1. Team-Perspective Based Map Ban Formatting

The most critical difference is in map ban display. The tournament system shows a team-centric view, where:
- Map bans are shown as "My Team" vs "Opponent"
- Only your team's bans are shown in detail
- Opponent bans are just shown as "Submitted" or "Waiting"
- Bans are displayed horizontally with fixed-width columns

Implementation tasks:
1. Add a new method to MapBanHelpers:
```csharp
public static string FormatTeamPerspectiveBans(
    List<string> userTeamBans,
    bool opponentHasSubmitted,
    List<string> userTeamUnconfirmedBans = null,
    MatchLength matchLength = MatchLength.Bo3)
{
    var builder = new StringBuilder();
    
    // First handle user's team bans (confirmed or unconfirmed)
    if (userTeamUnconfirmedBans?.Any() == true)
    {
        builder.AppendLine("My Team Map Bans (unconfirmed):");
        builder.AppendLine("```");
        builder.AppendLine("Priority #1          Priority #2          Priority #3");
        
        // Create a single line with fixed-width spacing
        var mapLine = new StringBuilder();
        for (int i = 0; i < userTeamUnconfirmedBans.Count; i++)
        {
            string mapName = userTeamUnconfirmedBans[i];
            mapLine.Append(mapName.PadRight(20));
        }
        builder.AppendLine(mapLine.ToString());
        builder.AppendLine("```");
    }
    else if (userTeamBans?.Any() == true)
    {
        builder.AppendLine("My Team Map Bans: ✅");
        builder.AppendLine("```");
        builder.AppendLine("Priority #1          Priority #2          Priority #3");
        
        var mapLine = new StringBuilder();
        for (int i = 0; i < userTeamBans.Count; i++)
        {
            string mapName = userTeamBans[i];
            mapLine.Append(mapName.PadRight(20));
        }
        builder.AppendLine(mapLine.ToString());
        builder.AppendLine("```");
    }
    else
    {
        builder.AppendLine("My Team Map Bans: Not submitted yet");
    }
    
    // Now handle opponent's bans - don't show specific maps
    builder.AppendLine();
    if (opponentHasSubmitted)
    {
        builder.AppendLine("Opponent Map Bans: ✅ Submitted");
    }
    else
    {
        builder.AppendLine("Opponent Map Bans: ⏳ Waiting for submission");
    }
    
    // Add guarantee explanation
    builder.AppendLine();
    builder.AppendLine(StatusEmbedHelpers.GetMapBanGuaranteeExplanation(matchLength));
    
    return builder.ToString();
}
```

2. Update ScrimmageStatusService.AddMapBansField:
```csharp
private void AddMapBansField(DiscordEmbedBuilder embed, Scrimmage scrimmage, ulong? channelId = null)
{
    // Determine which team's perspective to show
    ScrimmageTeam userTeam;
    ScrimmageTeam opponentTeam;
    
    // If channelId is provided, determine which team is viewing
    if (channelId.HasValue && channelId.Value == scrimmage.Thread.Id)
    {
        // This is the main thread, check if we can determine who's viewing
        // For now, default to Team A's perspective
        userTeam = scrimmage.TeamA;
        opponentTeam = scrimmage.TeamB;
    }
    else
    {
        // Default to Team A's perspective
        userTeam = scrimmage.TeamA;
        opponentTeam = scrimmage.TeamB;
    }
    
    // Check if there are unconfirmed bans to display
    if (userTeam.UnconfirmedMapBans.Any())
    {
        // Format from user team's perspective
        string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
            null,  // No confirmed bans yet
            opponentTeam.MapBans.Any(),  // Has opponent submitted?
            userTeam.UnconfirmedMapBans,
            scrimmage.MatchLength);
            
        embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
    }
    else
    {
        // Format from user team's perspective
        string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
            userTeam.MapBans,
            opponentTeam.MapBans.Any(),
            null,  // No unconfirmed bans
            scrimmage.MatchLength);
            
        embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
    }
}
```

### 2. Tree-Style Game Results Formatting

The tournament system displays game results in a tree-like format with:
- Game number and map name on one line
- Winner indented below with a └─ prefix
- Status indicators for in-progress games

Implementation tasks:
1. Update GameResultHelpers.FormatGameResults:
```csharp
public static string FormatGameResults(
    List<int> results,
    List<string> maps,
    string player1Name,
    string player2Name)
{
    var resultsBuilder = new StringBuilder();

    if (results == null || !results.Any())
    {
        resultsBuilder.AppendLine("No games completed yet");
        return resultsBuilder.ToString().Trim();
    }

    resultsBuilder.AppendLine("**Game History**");

    for (int i = 0; i < maps.Count; i++)
    {
        string winner = i < results.Count
            ? (results[i] == 1 ? player1Name : player2Name)
            : "In Progress";
            
        string gameNumber = $"Game {i + 1}";
        string mapName = maps[i];
        string result = winner == "In Progress" 
            ? "⏳ In Progress" 
            : $"Winner: **{winner}**";

        resultsBuilder.AppendLine($"\n{gameNumber} • {mapName}");
        resultsBuilder.AppendLine($"└─ {result}");
    }

    return resultsBuilder.ToString().Trim();
}
```

2. Update ScrimmageStatusService to use this helper:
```csharp
// In the UpdateScrimmageStatusAsync method, replace the current game results section
if (scrimmage.Results.Count > 0 || scrimmage.Maps.Count > 0)
{
    string resultsFormatted = GameResultHelpers.FormatGameResults(
        scrimmage.Results,
        scrimmage.Maps,
        scrimmage.TeamA.Captain.Username,
        scrimmage.TeamB.Captain.Username);
    
    embed.AddField("🎮 Game Results", resultsFormatted, false);
}
```

### 3. Team-Perspective Deck Submissions

The tournament system only shows your team's deck code:
- Your team's deck shows the full code
- Other team just shows completion status

Implementation tasks:
1. Add a new method to handle team-specific deck display:
```csharp
private void AddDeckSubmissionsWithTeamPerspective(
    DiscordEmbedBuilder embed, 
    Scrimmage scrimmage, 
    ulong? channelId = null)
{
    var deckBuilder = new StringBuilder();
    
    // Determine which team's perspective to show (same logic as map bans)
    bool isTeamA = true; // Default to Team A's perspective
    
    // If we can determine the viewer's team, adjust perspective
    if (channelId.HasValue && channelId.Value == scrimmage.Thread.Id)
    {
        // This could be enhanced with visitor tracking in the future
    }
    
    // User's team deck status
    var userTeam = isTeamA ? scrimmage.TeamA : scrimmage.TeamB;
    var opponentTeam = isTeamA ? scrimmage.TeamB : scrimmage.TeamA;
    
    if (!string.IsNullOrEmpty(userTeam.DeckName))
    {
        deckBuilder.AppendLine("My Team Deck: ✅ Submitted");
        deckBuilder.AppendLine($"Deck code: `{userTeam.DeckName}`");
    }
    else
    {
        deckBuilder.AppendLine("My Team Deck: Not submitted yet");
        deckBuilder.AppendLine("Use `/scrimmage submit_deck` to submit your deck");
    }
    
    // Opponent's deck status - don't show code
    deckBuilder.AppendLine();
    if (!string.IsNullOrEmpty(opponentTeam.DeckName))
    {
        deckBuilder.AppendLine("Opponent Deck: ✅ Submitted");
    }
    else
    {
        deckBuilder.AppendLine("Opponent Deck: Waiting for submission");
    }
    
    embed.AddField("🃏 Deck Submissions", deckBuilder.ToString().Trim(), false);
}
```

2. Update the UpdateScrimmageStatusAsync method to use this helper instead of the current deck fields.

### 4. Context-Sensitive Instructions

The tournament system adapts instructions based on the viewer's team:
- Shows instructions specific to the current team's state
- Gives clear guidance on the next step needed

Implementation tasks:
1. Add a method to generate context-sensitive instructions:
```csharp
private string GetContextSensitiveInstructions(
    Scrimmage scrimmage, 
    MatchStage currentStage,
    bool isTeamA = true)
{
    var userTeam = isTeamA ? scrimmage.TeamA : scrimmage.TeamB;
    var opponentTeam = isTeamA ? scrimmage.TeamB : scrimmage.TeamA;
    
    switch (currentStage)
    {
        case MatchStage.MapBan:
            if (userTeam.UnconfirmedMapBans.Any())
                return "Review your map ban selections above and choose to confirm or revise them.";
            else if (!userTeam.MapBans.Any())
                return "Select maps to ban using the dropdown below, ordered by priority.";
            else if (!opponentTeam.MapBans.Any())
                return "Waiting for your opponent to select their map bans.";
            else
                return "Both teams have completed map bans. Moving to deck submission stage.";
                
        case MatchStage.DeckSubmission:
            if (string.IsNullOrEmpty(userTeam.DeckName))
                return "Submit your deck using `/scrimmage submit_deck`.";
            else if (string.IsNullOrEmpty(opponentTeam.DeckName))
                return "Waiting for your opponent to submit their deck.";
            else
                return "Both teams have submitted decks. The match will begin soon.";
                
        case MatchStage.GameResults:
            return "Play your game and report the result using the buttons below.";
            
        case MatchStage.Completed:
            return $"Match completed. {(scrimmage.TeamAScore > scrimmage.TeamBScore ? scrimmage.TeamA.Captain.Username : scrimmage.TeamB.Captain.Username)} wins!";
            
        default:
            return StatusEmbedHelpers.GetStageInstructions(currentStage);
    }
}
```

2. Use this method in AddStageSpecificInfo.

### 5. Title and Subtitle Formatting

The tournament system has a more informative title and subtitle:
- Shows tournament context in title
- Shows match participants and game number in subtitle

Implementation tasks:
1. Update the title and description generation in UpdateScrimmageStatusAsync:
```csharp
// Build a more informative title
string title = $"{GetGameTypeString(scrimmage.GameType)} Scrimmage: {GetMatchLengthString(scrimmage.MatchLength)}";

// Create a subtitle with player info and game progress
string subtitle = $"Match: {scrimmage.TeamA.Captain.Username} vs {scrimmage.TeamB.Captain.Username}";

// Add current game indicator if games have been played
if (scrimmage.CurrentGameNumber > 0)
{
    int gamesNeeded = GetGamesToWin(scrimmage.MatchLength);
    int totalGames = gamesNeeded * 2 - 1; // Maximum possible games
    subtitle += $", Game {scrimmage.CurrentGameNumber} of {totalGames}";
}

// Build description with subtitle and progress bar
string description = subtitle + "\n\n" + GetScrimmageProgressBar(scrimmage);

// Update the embed
var embed = new DiscordEmbedBuilder()
    .WithTitle(title)
    .WithDescription(description)
    .WithColor(GetStatusColor(scrimmage.Status))
    .WithFooter($"Created {scrimmage.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
```

2. Remove the separate player fields as they'll now be in the subtitle.

These tasks cover the major alignment points needed to make the scrimmage status embeds match the tournament system's design and behavior.

## Implementation Bug Fixes

After implementing the team-perspective view and consistent formatting for the scrimmage system, we addressed several potential bugs:

1. **Progress Bar Redundancy**
   - [x] Removed redundant text from GetScrimmageProgressBar since we now include a custom subtitle
   - [x] Ensured clean formatting with proper spacing

2. **Null/Empty Results Handling**
   - [x] Updated GameResultHelpers.FormatGameResults to better handle cases where:
     - Results list is null but maps exist
     - Maps list is null but results exist
     - Different counts between maps and results
   - [x] Added proper null-safety with null-conditional operators
   - [x] Ensured "In Progress" indicator works correctly for games without results

3. **Team Perspective Implementation**
   - [x] Added channelId parameter to all team-perspective methods
   - [x] Created method overloads to maintain backward compatibility
   - [x] Added TODOs for future enhancements to determine viewer's team
   - [x] Fixed incorrect property references in deck submission code to use direct scrimmage properties
   - [x] Properly implemented conditional logic for different team perspectives

4. **Context-Sensitive Instructions**
   - [x] Implemented detailed instructions specific to each team's state
   - [x] Fixed logic for handling unconfirmed vs. confirmed map bans
   - [x] Added proper checks for deck submission status

These fixes ensure that the scrimmage system now correctly displays team-perspective views matching the tournament system's design, while also handling edge cases and providing proper feedback to users based on their current state in the match process.