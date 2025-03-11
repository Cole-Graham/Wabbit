# Wabbit Bot Design Document

This document outlines the design and feature roadmap for the Wabbit Discord bot, which manages game matchmaking, tournaments, and other gaming-related functionality.

## Terminology

- **Stage**: Stage of a tournament, i.e. "Groups", "Quarter-Finals", "Semi-Finals", "Finals"
- **Round**: Round within a stage, if the stage has multiple rounds (e.g. a group stage with multiple rounds of advancement)
- **Match/Series**: A series of games between two players (e.g. Bo1, Bo3, Bo5, Bo7)
- **Game**: Individual game played within a series

## Core Features

Features are categorized as either **Priority** (focus for initial release) or **Long-term** (planned for future releases). For long-term features, only the data structures and foundational plumbing will be implemented in the initial version (if necessary, otherwise that can be left for later as well).

### Regular Features

#### Random Map
**Priority**
- Generate a random map from a pool defined in a JSON file
- Display the map overview image stored on the server (we should add a default image for maps which don't have one on the server yet)
- Users can filter by "Size" and "IsInTournamentPool" with dropdown menus
  - Filter menus dynamically generated based on available values in the JSON under "Size" and "IsInTournamentPool" keys.
- Prevent duplicate map generation for the same user running the command multiple times within a short window (maybe have a button to regenerate another map)
- Only selects from maps with "IsInRandomPool" set to true

#### Scrimmage
**Long-term** (may be promoted to Priority based on time constraints)
- Tournament-style casual games where players can compete in matches (Bo1, Bo3)
- Players must submit deck codes before each game
- Map banning system based on series length
- Rating system with persistent storage of game history, including:
  - Date/time played
  - Deck codes used
  - Replay files
- Separate rating systems for Bo1 and Bo3 formats
- Automated verification of deck codes by parsing replay files
  - Potentially rewrite existing Python parser in C# for consistency

### Tournament Features

#### Group + Playoffs Format
**Priority**
- Support for player counts from 8 to 16 (maybe 7 as well if we are short 1 player, since I plan on hosting these every weekend)
- Handle odd numbers of players with bye rounds or qualifiers
- Support for 1v1 and 2v2 matchups

##### Group Stage
**Priority**
- Initially support 1 round of matches
  - In groups of 4, each player plays 3 matches
- Tie breakers always resolved with a tie breaker match
- Best-of-1 (Bo1) matches for initial design
- Priority support for groups of 4

**Long-term**
- Support for groups of 3 and 5 (can be short term priority if its trivial to add support for this?)

##### Playoff Stage
**Priority**
- Support for Bo3 and Bo5 matches
- For Bo5, implement a 7-map pool with 1 ban per player
  - No special handling for duplicate bans (pool simply reduced to 6 maps)
- Only 1st and 2nd place winners receive prizes (no 3rd place matches)

#### Tournament Signups
**Priority**
- Signup and withdraw buttons
- Admin commands (all ephemeral, disappearing after 5-10 seconds):
  - Close signups
  - Open signups (with specified duration, default is permanent)
  - Set signup channel
  - Create tournament from signups
- Dedicated signup channel displaying only signup embeds
- Tournament date displayed in PST with dynamic local time conversion
- For 2v2 and larger tournaments, private temporary Discord threads for team communication

#### Standings Display
**Priority**
- Dedicated channel for tournament standings
- Graphical representation of standings
- Automatic updates as match results are submitted
- Ephemeral admin commands (disappearing after 5-10 seconds)

#### Data Persistence
**Priority**
- Tournament state stored in database
- Permanent history of tournaments including deck codes and replay references

#### Additional Tournament Formats
**Long-term**
- Rating system integration
- Elimination and Double elimination formats
- Multiple Round robin format
- Hybrid formats combining different tournament structures

## Technical Implementation

The bot will be developed in C# using DSharpPlus for Discord integration. Data will be stored in JSON files for the initial release, with potential migration to a more robust database system in future versions.

### Data Models

The system will use consistent data models across all features to ensure synchronization and prevent data integrity issues:

- **Tournament Model**: Core structure for all tournament data, maintaining references to stages, rounds, and matches
- **Signup Model**: Aligned with Tournament model to ensure smooth conversion from signups to tournament creation
- **Player/Participant Model**: Consistent representation across signups, tournaments, and regular features
- **Match Result Model**: Standardized structure for recording outcomes regardless of tournament context

Models should be designed with future extensions in mind, using nullable properties and interfaces where appropriate to maintain backward compatibility as features are added.

Map data, tournament configurations, and player information will follow a well-defined schema to ensure consistency and enable future extensions of functionality. 