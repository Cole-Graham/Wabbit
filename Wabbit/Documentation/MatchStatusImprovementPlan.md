# Match Status UI and Flow Improvements

## Implementation Checklist

### UI Changes
- [x] Change map ban UI to use dropdown modal
- [x] Display match progress with emojis and arrows
- [x] Add tournament context header
- [x] Format map pool with clear sections
- [x] Show priority for map bans
- [x] Add confirmation buttons with emoji labels
- [x] Simplify instruction text
- [x] Remove redundant headers
- [x] Add map thumbnails for revealed maps
- [x] Auto-delete map thumbnails after 5 minutes

### Color Coding
- [x] Define color codes for guaranteed bans
- [x] Define color codes for non-guaranteed bans
- [x] Define color codes for played maps
- [x] Define color codes for available maps

### Functional Changes
- [x] Hide opponent's deck codes
- [x] Display map ban priority
- [x] Update score display
- [x] Delay map reveal until both decks submitted
- [x] Show map thumbnail in separate message
- [x] Auto-delete map thumbnail after time limit

### Code Implementation
- [x] Update `MatchStatusService.cs`:
  - [x] Implement horizontal progress bar
  - [x] Sort maps by status
  - [x] Add color coding
  - [x] Integrate confirmation buttons
  - [x] Update map pool formatting
- [x] Update `TournamentMatchService.cs`:
  - [x] Add deck submission check
  - [x] Implement map reveal delay
  - [x] Add map thumbnail display
  - [x] Add auto-deletion timer
- [x] Update `MapBanHandler.cs`:
  - [x] Update dropdown integration
  - [x] Add confirmation handling
  - [x] Update map sorting
  - [x] Integrate color coding

## Overview

This document outlines a comprehensive plan to refine the deck submission process in the Wabbit tournament bot. The goal is to create a cleaner, more minimalist user experience by:

1. Integrating the confirmation buttons directly into the match status embed
2. Moving instructions to the bottom of the embed with consistent formatting
3. Simplifying the deck code display format
4. Removing unnecessary headers to maintain a minimalist design
5. Making certain notifications ephemeral with auto-deletion
6. Ensuring command responses are ephemeral or auto-deleted to reduce clutter

## Current System Analysis

The current system follows this flow:
1. Player uses `/tournament submit_deck` command with a deck code parameter
2. Command creates a new message with Confirm/Revise buttons
3. After confirmation, the match status embed is updated with deck submission info
4. When all decks are submitted, notifications are sent and remain in the thread

Issues with the current approach:
- Multiple separate messages for submission and confirmation
- Inconsistent UI with some elements in the command response and others in the status embed
- Cluttered thread with persistent notifications
- Verbose deck submission display with redundant information

## Detailed Implementation Plan

### 1. Integrate Confirmation Buttons into Match Status Embed

#### Files to Modify:
- `TournamentGroup.cs` - Submit deck command handler
- `MatchStatusService.cs` - Match status display
- `DeckSubmissionHandler.cs` - Button interaction handler

#### Implementation Steps:

1. **Add New MatchStatusService Method**
2. **Update Submit Deck Command in TournamentGroup.cs**
3. **Update DeckSubmissionHandler - HandleConfirmDeckButton**
4. **Add Method to MatchStatusService for Updating Without Buttons**

### 2. Move Instructions to Bottom of Embed

#### Files to Modify:
- `MatchStatusService.cs`

#### Implementation Steps:

1. **Add Instruction Helper Method**
2. **Update Existing Stage Update Methods**

### 3. Simplify Deck Code Display

#### Files to Modify:
- `MatchStatusService.cs` → `RecordDeckSubmissionAsync`

#### Implementation Steps:

1. **Update RecordDeckSubmissionAsync Method**

### 4. Update ReviseButton Handler

#### Files to Modify:
- `DeckSubmissionHandler.cs` → `HandleReviseDeckButton`

#### Implementation Steps:

1. **Update HandleReviseDeckButton Method**

### 5. Update SubmitDeckButton Handler

#### Files to Modify:
- `DeckSubmissionHandler.cs` → `HandleSubmitDeckButton`

#### Implementation Steps:

1. **Update HandleSubmitDeckButton Method**

## Edge Cases and Additional Considerations

### 1. Race Conditions

**Issue**: Multiple users submitting decks simultaneously could lead to race conditions and one submission overwriting another in the match status embed.

**Solution**: 
- Implement concurrency control with lock objects or semaphores per channel
- Add timestamp tracking to detect and resolve conflicts
- Use atomic updates where possible

### 2. Error Recovery

**Issue**: If an error occurs during confirmation, the deck code might be lost or the UI could be left in an inconsistent state.

**Solution**:
- Add transaction-like behavior with rollback capabilities
- Store backup of state before making changes
- Implement retry logic for Discord API calls
- Add recovery mechanism in case buttons disappear

### 3. Multiple Deck Submissions

**Issue**: Players might submit multiple decks for the same game, causing confusion or UI clutter.

**Solution**:
- Detect and handle duplicate submissions
- Clearly mark previous submissions as replaced
- Provide visual indicators for the most recent submission

### 4. Message Deletion Resilience

**Issue**: Auto-deleted messages could cause confusion if a user is still interacting with them.

**Solution**:
- Add warning text indicating the message will be deleted
- Use longer timers for error messages to ensure they're seen
- Implement graceful handling of message deletion errors

### 5. User Experience for Spectators

**Issue**: Tournament spectators should have a clear view of match progress without UI clutter.

**Solution**:
- Design the match status display to be clean and informative for all viewers
- Keep confirmation UI elements ephemeral and user-specific
- Ensure match status shows clear progression for all users

### 6. Backward Compatibility

**Issue**: Existing deck submissions and ongoing matches will need to work with the new system.

**Solution**:
- Add code to detect and convert existing submissions to the new format
- Support both old and new field formats during the transition
- Implement fallback mechanisms for unexpected states

## Design Adjustments

Based on tournament requirements and user feedback, the following adjustments need to be made to the deck submission flow design:

### 1. Map Reveal Timing

**Requirement**: Players should not know which maps they will play until after deck submissions are confirmed.

**Implementation Changes**:
- Maps for each game should only be revealed after both players have confirmed their deck codes for that game
- Map Pool section will use color coding to indicate map status (banned, played, available)
- Add a method in MatchStatusService to reveal maps progressively
- Create a map reveal notification to show in the thread after deck submissions

### 2. Match Progress Horizontal Layout

**Requirement**: Make the Match Progress a horizontal list to take up less vertical space.

**Implementation Changes**:
- Update the match progress field to display stages horizontally
- Combine emoji indicators with stage names for compact display
- Adjust the formatting to ensure proper spacing

### 3. Map Pool Display

**Requirement**: Map Pool section should always be displayed with color coding for map status.

**Implementation Changes**:
- Always display the Map Pool section in the status embed
- Use color coding: red for banned maps (player's team), yellow for maps already played, and green for available maps
- The opposing team's bans should not be indicated (they remain green unless banned by the player's team)

### 4. Map Ban Priority Display

**Requirement**: Clearly indicate map ban priority and non-guaranteed bans.

**Implementation Changes**:
- Show map ban priority in a clear format
- Use orange to indicate non-guaranteed bans in the Map Pool
- Update the map ban field to show priority without redundant text

### 5. Map Thumbnail Display

**Requirement**: Display map thumbnail for 5 minutes after reveal.

**Implementation Changes**:
- Create a method to send map thumbnail after reveal
- Schedule message deletion after 5 minutes
- Integrate with map reveal process

### 6. Concise Match Status

**Requirement**: Merge match status and Final Score sections on one line.

**Implementation Changes**:
- Combine match status and final score information
- Use a more concise format for completed matches

### 7. Concise Instructions

**Requirement**: Make instructions one line if possible.

**Implementation Changes**:
- Condense instructions to a single line
- Maintain clarity while reducing vertical space

### 8. Confirm and Revise Buttons for Map Bans

**Requirement**: Show Confirm and Revise buttons for map bans.

**Implementation Changes**:
- Include Confirm and Revise buttons in the map ban display
- Show priority order to help users decide whether to confirm or revise

## Visual Design Examples

The following ASCII representations illustrate how the match status embed will look at various stages of a tournament match with the design adjustments implemented.

### Map Banning Stage - Initial State

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ⬜ Deck Submission → ⬜ Game Results ║
╠══════════════════════════════════════════════════╣
║ Select maps to ban in order of priority.         ║
║ Your first two bans are guaranteed, third is     ║
║ conditional on opponent's bans.                  ║
╠══════════════════════════════════════════════════╣
║ [Select maps to ban ▼]                           ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟩 Map 1  🟩 Map 2  🟩 Map 3  🟩 Map 4            ║
║ 🟩 Map 5  🟩 Map 6  🟩 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ (Not yet submitted)                              ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ⏳ Waiting for submission     ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ (No decks submitted yet)                         ║
╚══════════════════════════════════════════════════╝
```

### Map Banning Stage - After Selection, Before Confirmation

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ⬜ Deck Submission → ⬜ Game Results ║
╠══════════════════════════════════════════════════╣
║ Review your map ban selections below.            ║
║ Click Confirm to lock in your choices or         ║
║ Revise to make changes.                         ║
╠══════════════════════════════════════════════════╣
║ [Map 3, Map 6, Map 7 selected]                   ║
║ [Confirm Bans] [Revise Bans]                     ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟩 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ⏳ Waiting for submission     ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ (No decks submitted yet)                         ║
╚══════════════════════════════════════════════════╝
```

### Map Banning Stage - After Confirmation

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ⬜ Deck Submission → ⬜ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟩 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ⏳ Waiting for submission     ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ (No decks submitted yet)                         ║
╠══════════════════════════════════════════════════╣
║ **Waiting for opponent to submit map bans.**     ║
╚══════════════════════════════════════════════════╝
```

### Deck Submission Stage - After Map Bans

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ▶️ Deck Submission → ⬜ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟩 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ✅ Submitted                  ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ (No decks submitted yet)                         ║
╠══════════════════════════════════════════════════╣
║ **Submit your deck for Game 1 using the `/tournament submit_deck` command.** ║
╚══════════════════════════════════════════════════╝
      [Submit Deck]
```

### Deck Submission Stage - After My Deck Submission (Pending)

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ▶️ Deck Submission → ⬜ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟩 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ✅ Submitted                  ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ Game 1: `AAECAea5AwLHxgOL1QMO1r4D4LwDusYD...` *(pending)* ║
║ Opponent: ⏳ Waiting for submission              ║
╠══════════════════════════════════════════════════╣
║ **Please confirm or revise your deck submission using the buttons below.** ║
╚══════════════════════════════════════════════════╝
      [Confirm Deck]    [Revise Deck]
```

### Deck Submission Stage - Both Players Confirmed, Map Revealed

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 1 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ✅ Deck Submission → ▶️ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟦 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ✅ Submitted                  ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ (No games completed yet)                         ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ Game 1: `AAECAea5AwLHxgOL1QMO1r4D4LwDusYD...`   ║
║ Opponent: ✅ Deck submitted                      ║
╠══════════════════════════════════════════════════╣
║ **Game 1: Play on Map 1 and report the result when finished.** ║
╚══════════════════════════════════════════════════╝
      [Report Result]
```

### Game Results - After Game 1 (Bo3)

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 2 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ▶️ Deck Submission → ▶️ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟦 Map 1  🟩 Map 4            ║
║ 🟩 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ✅ Submitted                  ║
╠══════════════════════════════════════════════════╣
║ Current Score: Player1 1 - 0 Player2             ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ Game 1 (Map 1): Player1 won                      ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ Game 1: `AAECAea5AwLHxgOL1QMO1r4D4LwDusYD...`   ║
║ Opponent: ✅ Deck submitted                      ║
╠══════════════════════════════════════════════════╣
║ **Submit your deck for Game 2 using the `/tournament submit_deck` command.** ║
╚══════════════════════════════════════════════════╝
      [Submit Deck]
```

### Final Match Results - Bo3

```
╔══════════════════════════════════════════════════╗
║ Group Stage (Group A): Round 1 of 3              ║
║ Match 1: Player1 vs Player2, Game 3 of 3         ║
╠══════════════════════════════════════════════════╣
║ ✅ Map Bans → ✅ Deck Submission → ✅ Game Results ║
╠══════════════════════════════════════════════════╣
║ Map Pool                                         ║
║ 🟥 Map 3  🟩 Map 2  🟦 Map 1  🟦 Map 4            ║
║ 🟦 Map 5  🟥 Map 6  🟨 Map 7  🟩 Map 8            ║
║ 🟩 Map 9                                          ║
╠══════════════════════════════════════════════════╣
║ My Team Map Bans:                                ║
║ Priority #1      Priority #2      Priority #3    ║
║ Map 3            Map 6            Map 7          ║
╠══════════════════════════════════════════════════╣
║ Opponent Map Bans: ✅ Submitted                  ║
╠══════════════════════════════════════════════════╣
║ Game Results                                     ║
║ Game 1 (Map 1): Player1 won                      ║
║ Game 2 (Map 5): Player2 won                      ║
║ Game 3 (Map 4): Player1 won                      ║
╠══════════════════════════════════════════════════╣
║ Deck Submissions                                 ║
║ Game 1: `AAECAea5AwLHxgOL1QMO1r4D4LwDusYD...`   ║
║ Game 2: `AAECAZICBNulA5LNA/DUA7uKBA1AiL0D...`   ║
║ Game 3: `AAECAea5AwS8xgPaxgP21gO/7QMN1b4D...`   ║
║ Opponent: ✅ All decks submitted                 ║
╠══════════════════════════════════════════════════╣
║ Match completed! Winner: Player1 (2-1)           ║
║ Player1 advances to playoffs.                    ║
╚══════════════════════════════════════════════════╝
```

## Conclusion

The adjusted design addresses important tournament requirements:
- Map reveal timing provides strategic depth by hiding maps until deck submission
- Clear map ban priority display helps players understand selection order
- Privacy for deck codes ensures fair competition
- Consistent game results area provides a clear match history
- Map thumbnails enhance user experience with visual elements

These changes will create a more strategic, fair, and user-friendly tournament experience while maintaining the clean, minimalist design approach of the overall plan. 