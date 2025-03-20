# Scrimmage System Manual Testing Checklist

## Setup

- [ ] Create two test Discord users (or use existing ones)
- [ ] Ensure proper permissions are configured for both users
- [ ] Verify the bot is online and responsive

## 1. Match Creation

- [ ] Test `/scrimmage create @Player1 @Player2` command
  - [ ] Private thread is created
  - [ ] Initial status embed appears with:
    - [ ] Correct title format
    - [ ] Player names in subtitle
    - [ ] Progress bar showing "Created" stage
    - [ ] "Ready Up" button present
    - [ ] First map automatically selected

## 2. Map Ban Flow

- [ ] Both players click "Ready Up" button
  - [ ] Status updates to "In Progress" 
  - [ ] Map ban dropdown appears
  - [ ] Progress indicator advances to Map Ban stage

- [ ] Player 1 selects map bans
  - [ ] Verify map selections are recorded
  - [ ] "Confirm" and "Revise" buttons appear
  - [ ] Map ban field updates with "My Team" perspective
  - [ ] Unconfirmed map bans display correctly in horizontal format
  - [ ] Maps display in fixed-width columns

- [ ] Player 1 confirms map bans
  - [ ] Verification that bans are accepted 
  - [ ] Status updates to "Waiting for opponent"
  - [ ] Only "Refresh" button remains for Player 1
  - [ ] Instructions update to show waiting message

- [ ] Player 2 submits map bans
  - [ ] Map ban process works for Player 2
  - [ ] After confirmation, stage advances to Deck Submission
  - [ ] Both players see appropriate components

## 3. Deck Submission

- [ ] Player 1 uses `/scrimmage submit_deck` command
  - [ ] Deck code is stored correctly
  - [ ] Status embed updates with team perspective
  - [ ] Only Player 1 can see their own deck code
  - [ ] Player 2 only sees "Submitted" status
  - [ ] Instructions update appropriately

- [ ] Player 2 submits deck
  - [ ] Deck submission works for Player 2
  - [ ] After both decks submitted, stage advances to Game Results
  - [ ] Game winner buttons appear
  - [ ] Replay submission button appears

## 4. Game Results

- [ ] Report first game result (Player 1 wins)
  - [ ] Result recorded correctly
  - [ ] Game results field updates with tree structure:
    - [ ] Game number and map on first line
    - [ ] Winner indented with "└─" prefix
    - [ ] Proper formatting and bold winner name
  - [ ] Score updates correctly
  - [ ] New map selected for next game

- [ ] Report second game result (Player 2 wins)
  - [ ] Result displays correctly in tree format
  - [ ] Score updates to 1-1

- [ ] Continue reporting results until match completion condition met
  - [ ] Match automatically marked as completed when win condition reached
  - [ ] Final status message shows winner
  - [ ] No more game buttons available

## 5. Team Perspective Testing

For each stage, verify these perspective-based elements:

- [ ] Map Ban Stage
  - [ ] "My Team" vs "Opponent" labeling is correct
  - [ ] Own map bans show full details
  - [ ] Opponent map bans only show status
  - [ ] Instructions are tailored to current team's state

- [ ] Deck Submission Stage
  - [ ] Own deck code is visible
  - [ ] Opponent deck only shows status
  - [ ] Instructions change based on submission status

- [ ] Game Results Stage
  - [ ] Results display consistently for both players
  - [ ] Components are appropriate for current game state

## 6. Edge Cases

- [ ] Test canceling a scrimmage
  - [ ] `/scrimmage cancel` command works
  - [ ] Status updated correctly

- [ ] Test revising map bans
  - [ ] "Revise" button works properly
  - [ ] Can reselect maps after revision

- [ ] Test with invalid inputs
  - [ ] Error handling for invalid map selections
  - [ ] Error handling for invalid deck codes

- [ ] Test refresh functionality
  - [ ] "Refresh" button updates the status correctly
  - [ ] State is maintained between refreshes

## 7. Component ID Format Validation

Verify that component IDs follow consistent format patterns:

- [ ] Map Ban Dropdown: `map_ban_{scrimmageId}`
- [ ] Confirm Map Bans Button: `confirm_map_bans_{teamIdentifier}_{scrimmageId}`
- [ ] Revise Map Bans Button: `revise_map_bans_{teamIdentifier}_{scrimmageId}`
- [ ] Ready Button: `ready_{scrimmageId}`
- [ ] Refresh Button: `refresh_status_{scrimmageId}`
- [ ] Submit Deck Button: `submit_deck_{scrimmageId}`
- [ ] Game Winner Dropdown: `select_game_winner_{scrimmageId}_{gameNumber}`
- [ ] Player 1 Win Button: `report_win_1_{scrimmageId}`
- [ ] Player 2 Win Button: `report_win_2_{scrimmageId}`
- [ ] Replay Submission Button: `replay_submission_{gameNumber}_{scrimmageId}`

Component IDs should use consistent:
- [ ] Naming conventions (snake_case)
- [ ] Parameter order (action first, then identifiers)
- [ ] Separator characters (underscore)
- [ ] Parameter format (scrimmageId should be consistent between components)

## Notes

- Document any discrepancies between expected and actual behavior
- Pay special attention to the team perspective views
- Check for consistency with tournament system UX
- Note any performance issues or delays

## Test Results

**Date Tested:** ________________

**Test User 1:** ________________

**Test User 2:** ________________

**Overall Status:** □ PASS  □ FAIL

**Issues Found:**
1. 
2. 
3. 

**Screenshots:**
- _Attach key screenshots here_ 