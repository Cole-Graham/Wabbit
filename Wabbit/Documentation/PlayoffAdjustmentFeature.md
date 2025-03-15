# Tournament Playoff Adjustment Features

## Overview

This document outlines the implementation plan for adding admin controls to adjust tournament playoff settings after the tournament has started.

## Current Status

As of the current implementation:
- Tournament playoff structure is configured once at the transition from group stage to playoffs
- Match formats (best-of-X) are set at creation time based on tournament settings
- Third place match creation is determined by a setting at bracket creation time

## Implementation Plan

### Phase 1: Third Place Match On-Demand (Implemented)
- Add a prompt after semifinals completion for admins to create a third place match
- Only tournament admins/moderators can interact with this prompt
- Third place match is disabled by default, but can be enabled upon admin request

### Phase 2: Match Format Adjustments (Future)
- Allow admins to modify match formats (best-of-X) for individual matches 
- Create admin commands to update match formats with proper permission checks
- Update the match status displays to show the new format

### Technical Design

#### Admin Third Place Match Creation

1. **When**: After both semifinal matches are completed
2. **How**: Display admin-only interaction button in the tournament channel 
3. **Access Control**: Only respond to interactions from users with tournament admin role
4. **Process Flow**:
   - Check if both semifinals are complete
   - Check if third place match already exists
   - If not, present admin with button to create third place match
   - When clicked, call `CreateThirdPlaceMatch` and link to the semifinal losers
   - Update tournament status to show the new match

#### Match Format Adjustment (Future)

1. **Command**: `/tournament adjust-format [match-id] [best-of]`
2. **Access Control**: Only tournament admins
3. **Validation**:
   - Ensure match exists
   - Ensure match hasn't completed
   - Validate best-of value (must be odd, within reasonable limits)
4. **Implementation**:
   - Update `Match.BestOf` property
   - Update match status display
   - Log change in tournament audit log
   - Notify affected players

### Challenges and Edge Cases

1. **Handling matches in progress**:
   - Define clear rules about when format can be changed
   - Potential solution: Only allow before match starts or between games

2. **Tournament visualization**:
   - Update bracket visualization to reflect added third place match
   - Ensure UI correctly shows updated format

3. **Tournament history tracking**:
   - Log format changes in tournament audit log
   - Record who made the change and when

4. **State persistence**:
   - Ensure changes are properly saved to tournament state
   - Handle serialization/deserialization correctly

## Implementation Details

### Component Changes

1. **TournamentPlayoffService**:
   - Added method to check if semifinals are complete
   - Added method to create third place match on demand
   - Updated to respect settings for match formats

2. **UI Components**:
   - Created admin-only "Create Third Place Match" button
   - Added tournament admin role check for interaction

3. **Commands**:
   - Future: Add command handling for format adjustments
   - Future: Add permission validation and error handling

### Testing Plan

1. Test third place match creation after semifinals:
   - Verify only admins can see/interact with button
   - Verify match is correctly created and linked
   - Verify UI updates properly

2. Test format adjustments (future):
   - Verify permission checks
   - Verify validation rules
   - Verify match UI updates correctly

## Development Timeline

1. **Phase 1**: Third Place Match On-Demand - Implemented
2. **Phase 2**: Match Format Adjustments - Estimate 3-5 days 