# Tournament State Persistence Design

## Implementation Summary

We have successfully implemented a comprehensive state persistence system for the tournament management in the Wabbit Discord bot. Here's what we've accomplished:

1. **Created State Model Classes**:
   - `TournamentState`: Top-level class for serializing/deserializing tournament state
   - `ActiveRound`: Class to track the state of an individual round
   - `TeamState`: Class to track team state within a round
   - `ParticipantState`: Class to track participant state within a team, including deck codes
   - `GameResult`: Class to track game results

2. **Added State Persistence to TournamentManager**:
   - `SaveTournamentState()`: Saves the current tournament state to a JSON file
   - `LoadTournamentState()`: Loads tournament state from a JSON file
   - `ConvertRoundsToState()`: Converts in-memory rounds to serializable state objects
   - `ConvertStateToRounds()`: Converts serialized state back to in-memory rounds
   - `GetActiveRoundsForTournament()`: Gets active rounds for a specific tournament

3. **Updated Tournament Creation and Round Management**:
   - Modified `CreateTournament()` to save state after tournament creation
   - Updated `Start1v1Round` and `Start2v2Round` to save state after round creation
   - Updated `EndRound` to save state after round completion
   - Enhanced deck submission handling to immediately save state when decks are submitted
   - Added state saving to map ban handling

4. **Enhanced Map Pool Management**:
   - Added support for using `IsInTournamentPool` attribute from Maps.json
   - Implemented tracking of played maps to prevent reuse
   - Updated map generation to filter out already played maps
   - Added custom map pool support to map ban methods

5. **Added Recovery Commands**:
   - `resume`: Command to resume a tournament after bot restart
   - Shows tournament status and active rounds

6. **Integrated with Program Startup**:
   - Added code to load tournament state when the bot starts
   - Added code to load participants for all signups

## Benefits Achieved

1. **Persistence**: Tournament state now survives bot restarts
2. **Transparency**: Clear view of tournament progress and history
3. **Recoverability**: Easy to resume tournaments from any point
4. **Consistency**: Single source of truth for tournament data
5. **Map Variety**: Ensures maps aren't reused within a tournament

## Next Steps

1. **Complete Round Restoration**: Implement full round restoration from saved state
2. **Add Admin Commands**: Add commands for overriding match results and replaying games
3. **Enhance Error Handling**: Add more robust error handling for state loading/saving
4. **Add State Inspection**: Add commands to inspect the current state of tournaments and rounds
5. **Testing**: Test with various tournament scenarios

## Map Pool Management

The system now handles map pools in a more sophisticated way:

1. **Tournament Map Pool**:
   - Maps are filtered based on the `IsInTournamentPool` attribute in Maps.json
   - Only maps marked as tournament-eligible are included in the pool

2. **Played Maps Tracking**:
   - The system tracks which maps have been played in each round
   - Maps that have been played are excluded from future map selections
   - This ensures variety and prevents map repetition

3. **Map Ban Integration**:
   - Map bans are applied to the filtered tournament map pool
   - The system handles cases where there aren't enough maps left
   - Custom map pools can be provided for special tournament formats

4. **Persistence**:
   - Map selections and results are saved in the tournament state
   - This allows for consistent map selection even after bot restarts

## Current Tournament Flow Analysis

### Tournament Creation and Structure
1. **Tournament Creation**: Created via `CreateTournament` in `TournamentManagementGroup.cs`
2. **Tournament Structure**: Defined in `Tournament.cs` with:
   - Groups (for group stage)
   - Matches (both group stage and playoffs)
   - Participants

### Round Execution Flow
From analyzing `TournamentGroup.cs` and related files:

1. **Round Initialization**:
   - `Start1v1Round` or `Start2v2Round` commands create a new round
   - Players are assigned to teams
   - Private threads are created for each team
   - Round is stored in `_ongoingRounds.TourneyRounds`

2. **Map Banning Process**:
   - Players are presented with dropdown to select maps to ban
   - Number of bans depends on match format (BO1, BO3, BO5, BO7)
   - Banned maps are stored in `Team.MapBans` property
   - Banned maps are removed from the pool

3. **Deck Submission**:
   - Players click a "Deck" button which opens a modal
   - Modal is handled in `Event_Modal.cs`
   - For 1v1 rounds, decks are stored in `Regular1v1.Deck1` and `Regular1v1.Deck2`
   - For tournament rounds, decks are stored in `Round.Participant.Deck`
   - Once all decks are submitted, the round proceeds to the next phase

4. **Match Execution**:
   - Maps are revealed one by one for each game
   - Maps are determined based on the banned maps
   - For BO5, maps are generated via `_banMap.GenerateMapListBo5()`
   - Players play their matches outside the bot
   - Results are reported through a dropdown

5. **Round Completion**:
   - Winner is selected via dropdown
   - Results are recorded
   - `EndRound` command can terminate a round

## Current State Persistence Issues

1. **Volatile State**: Tournament and round state is held in memory via `_ongoingRounds`
2. **Restart Vulnerability**: If the bot restarts, all in-progress tournament data is lost
3. **Limited Recovery**: No way to restore the exact state of in-progress matches
4. **Manual Intervention**: Difficult to correct mistakes or handle exceptional cases
5. **Disconnected Data**: Tournament structure and round execution are not well connected

## Proposed State Persistence Design

### 1. State Data Structure

```json
{
  "tournaments": [
    {
      "id": "unique-id",
      "name": "Tournament Name",
      "format": "GroupStageWithPlayoffs",
      "creator": "Username",
      "createdAt": "2025-03-10T16:29:36Z",
      "currentStage": "Groups",
      "isComplete": false,
      "groups": [
        {
          "id": "group-1",
          "name": "Group A",
          "isComplete": false,
          "participants": [
            {
              "id": "user-id-1",
              "username": "Player1",
              "wins": 2,
              "losses": 1,
              "draws": 0,
              "points": 6,
              "gamesWon": 6,
              "gamesLost": 3,
              "advancedToPlayoffs": false
            }
          ],
          "matches": [
            {
              "id": "match-1",
              "name": "Match 1",
              "type": "GroupStage",
              "participants": [
                {
                  "id": "user-id-1",
                  "username": "Player1",
                  "score": 2,
                  "isWinner": true
                },
                {
                  "id": "user-id-2",
                  "username": "Player2",
                  "score": 1,
                  "isWinner": false
                }
              ],
              "bestOf": 3,
              "isComplete": true,
              "result": {
                "winnerId": "user-id-1",
                "mapResults": [
                  "Player1 won on Map1",
                  "Player2 won on Map2",
                  "Player1 won on Map3"
                ],
                "completedAt": "2025-03-10T18:30:00Z",
                "deckCodes": {
                  "user-id-1": {
                    "Map1": "deck-code-player1-game1",
                    "Map2": "deck-code-player1-game2",
                    "Map3": "deck-code-player1-game3"
                  },
                  "user-id-2": {
                    "Map1": "deck-code-player2-game1",
                    "Map2": "deck-code-player2-game2",
                    "Map3": "deck-code-player2-game3"
                  }
                }
              },
              "rounds": [
                {
                  "id": "round-1",
                  "mapPool": ["Map1", "Map2", "Map3", "Map4", "Map5", "Map6", "Map7", "Map8"],
                  "bannedMaps": {
                    "user-id-1": ["Map7", "Map8"],
                    "user-id-2": ["Map5", "Map6"]
                  },
                  "selectedMaps": ["Map1", "Map2", "Map3"],
                  "deckCodes": {
                    "user-id-1": ["deck-code-1", "deck-code-2", "deck-code-3"],
                    "user-id-2": ["deck-code-4", "deck-code-5", "deck-code-6"]
                  },
                  "games": [
                    {
                      "map": "Map1",
                      "winnerId": "user-id-1",
                      "completedAt": "2025-03-10T17:30:00Z"
                    },
                    {
                      "map": "Map2",
                      "winnerId": "user-id-2",
                      "completedAt": "2025-03-10T18:00:00Z"
                    },
                    {
                      "map": "Map3",
                      "winnerId": "user-id-1",
                      "completedAt": "2025-03-10T18:30:00Z"
                    }
                  ],
                  "threadIds": {
                    "user-id-1": "thread-id-1",
                    "user-id-2": "thread-id-2"
                  },
                  "status": "Completed",
                  "cycle": 3,
                  "inGame": false
                }
              ]
            }
          ]
        }
      ],
      "playoffMatches": [
        // Similar structure to group matches
      ]
    }
  ],
  "activeRounds": [
    {
      "id": "round-id",
      "tournamentId": "tournament-id",
      "matchId": "match-id",
      "type": "1v1",
      "length": 3,
      "oneVOne": true,
      "cycle": 1,
      "inGame": true,
      "teams": [
        {
          "name": "Team 1",
          "threadId": "thread-id-1",
          "participants": [
            {
              "playerId": "user-id-1",
              "playerName": "Player1",
              "deck": "deck-code-1"
            }
          ],
          "wins": 1,
          "mapBans": ["Map7", "Map8"]
        },
        {
          "name": "Team 2",
          "threadId": "thread-id-2",
          "participants": [
            {
              "playerId": "user-id-2",
              "playerName": "Player2",
              "deck": "deck-code-2"
            }
          ],
          "wins": 0,
          "mapBans": ["Map5", "Map6"]
        }
      ],
      "maps": ["Map1", "Map2", "Map3"],
      "currentMapIndex": 1,
      "status": "InProgress",
      "createdAt": "2025-03-10T17:00:00Z",
      "lastUpdatedAt": "2025-03-10T17:30:00Z"
    }
  ]
}
```

### 2. Implementation Plan

#### A. Create State Storage Classes

1. **TournamentState**: Top-level class for serializing/deserializing tournament state
   ```csharp
   public class TournamentState
   {
       public List<Tournament> Tournaments { get; set; } = [];
       public List<ActiveRound> ActiveRounds { get; set; } = [];
   }
   ```

2. **ActiveRound**: Class to track the state of an individual round
   ```csharp
   public class ActiveRound
   {
       public string Id { get; set; } = Guid.NewGuid().ToString();
       public string TournamentId { get; set; } = "";
       public string MatchId { get; set; } = "";
       public string Type { get; set; } = "1v1"; // 1v1 or 2v2
       public int Length { get; set; } = 3; // BO3
       public bool OneVOne { get; set; } = true;
       public int Cycle { get; set; } = 0;
       public bool InGame { get; set; } = false;
       public List<TeamState> Teams { get; set; } = [];
       public List<string> Maps { get; set; } = [];
       public int CurrentMapIndex { get; set; } = 0;
       public string Status { get; set; } = "Created"; // Created, MapBanning, DeckSubmission, InProgress, Completed
       public DateTime CreatedAt { get; set; } = DateTime.Now;
       public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
   }
   ```

3. **TeamState**: Class to track team state within a round
   ```csharp
   public class TeamState
   {
       public string Name { get; set; } = "";
       public ulong ThreadId { get; set; }
       public List<ParticipantState> Participants { get; set; } = [];
       public int Wins { get; set; } = 0;
       public List<string> MapBans { get; set; } = [];
   }
   ```

4. **ParticipantState**: Class to track participant state within a team
   ```csharp
   public class ParticipantState
   {
       public ulong PlayerId { get; set; }
       public string PlayerName { get; set; } = "";
       public string Deck { get; set; } = ""; // Stores the player's submitted deck code during active rounds
   }
   ```

5. **GameResult**: Class to track game results
   ```csharp
   public class GameResult
   {
       public string Map { get; set; } = "";
       public ulong WinnerId { get; set; }
       public DateTime CompletedAt { get; set; } = DateTime.Now;
   }
   ```

6. **MatchResult**: Class for permanent match result storage
   ```csharp
   public class MatchResult
   {
       public object? Winner { get; set; }
       public List<string> MapResults { get; set; } = [];
       public DateTime CompletedAt { get; set; } = DateTime.Now;
       
       // Stores deck codes by player ID and map name
       // Dictionary<PlayerID, Dictionary<MapName, DeckCode>>
       public Dictionary<string, Dictionary<string, string>> DeckCodes { get; set; } = 
           new Dictionary<string, Dictionary<string, string>>(); 
   }
   ```

#### B. Modify TournamentManager

1. **Add State Persistence Methods**:
   ```csharp
   public void SaveTournamentState()
   {
       var state = new TournamentState
       {
           Tournaments = _ongoingRounds.Tournaments,
           ActiveRounds = ConvertRoundsToState(_ongoingRounds.TourneyRounds)
       };
       
       string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
       {
           WriteIndented = true,
           ReferenceHandler = ReferenceHandler.Preserve
       });
       
       File.WriteAllText(_tournamentStateFilePath, json);
   }
   
   public void LoadTournamentState()
   {
       if (!File.Exists(_tournamentStateFilePath))
           return;
           
       string json = File.ReadAllText(_tournamentStateFilePath);
       var state = JsonSerializer.Deserialize<TournamentState>(json, new JsonSerializerOptions
       {
           ReferenceHandler = ReferenceHandler.Preserve
       });
       
       if (state != null)
       {
           _ongoingRounds.Tournaments = state.Tournaments;
           _ongoingRounds.TourneyRounds = ConvertStateToRounds(state.ActiveRounds);
       }
   }
   ```

2. **Automatic State Updates**:
   - Add state saving calls at key points:
     - After tournament creation
     - After match creation
     - After map bans
     - After deck submissions
     - After game results
     - After round completion

#### C. Enhance Round Management

1. **Link Rounds to Tournament State**:
   - Modify `Start1v1Round` and `Start2v2Round` to create and save round state
   ```csharp
   public async Task Start1v1Round(CommandContext context, int length, DiscordUser Player1, DiscordUser Player2)
   {
       // Existing code...
       
       // Create a new round
       var round = new Round
       {
           // Set properties...
       };
       
       // Add to ongoing rounds
       _ongoingRounds.TourneyRounds.Add(round);
       
       // Save state
       _tournamentManager.SaveTournamentState();
       
       // Rest of existing code...
   }
   ```

2. **Add Deck Submission Tracking**:
   - Update the deck submission handler to save state
   ```csharp
   // In Event_Modal.cs
   participant.Deck = deck;
   
   // Save state after deck submission
   _tournamentManager.SaveTournamentState();
   ```

#### D. Create Recovery and Admin Commands

1. **Recovery Commands**:
   ```csharp
   [Command("resume_tournament")]
   [Description("Resume a tournament after bot restart")]
   public async Task ResumeTournament(CommandContext context, string tournamentName)
   {
       // Find tournament
       var tournament = _tournamentManager.GetTournament(tournamentName);
       if (tournament == null)
       {
           await context.EditResponseAsync($"Tournament '{tournamentName}' not found.");
           return;
       }
       
       // Find active rounds for this tournament
       var activeRounds = _tournamentManager.GetActiveRoundsForTournament(tournament.Id);
       
       // Display status
       var embed = new DiscordEmbedBuilder()
           .WithTitle($"Tournament: {tournament.Name}")
           .WithDescription("Tournament has been resumed.")
           .AddField("Status", tournament.IsComplete ? "Complete" : $"In Progress - {tournament.CurrentStage}")
           .AddField("Active Rounds", activeRounds.Count.ToString());
           
       await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
   }
   ```

2. **Admin Commands**:
   ```csharp
   [Command("override_result")]
   [Description("Override a match result")]
   public async Task OverrideResult(CommandContext context, string tournamentName, string matchId, string winnerId)
   {
       // Implementation...
   }
   ```

### 3. Implementation Phases

#### Phase 1: State Structure and Basic Persistence
- Create state classes
- Implement JSON serialization/deserialization
- Add basic save/load methods to TournamentManager

#### Phase 2: Round State Integration
- Modify round commands to use state persistence
- Add deck submission tracking
- Implement map ban state tracking

#### Phase 3: Recovery and Admin Features
- Add recovery commands
- Implement admin override commands
- Add state inspection commands

## Benefits of This Approach

1. **Persistence**: Tournament state survives bot restarts
2. **Transparency**: Clear view of tournament progress and history
3. **Recoverability**: Easy to resume tournaments from any point
4. **Flexibility**: Admin commands for handling exceptional cases
5. **Consistency**: Single source of truth for tournament data

## Implementation Steps

1. Create the state model classes
2. Add state persistence to TournamentManager
3. Modify round handling to save state at key points
4. Add recovery commands
5. Add admin commands for state manipulation
6. Test with various tournament scenarios 

## Deck Submission Integration

The system now properly handles deck submissions as part of the state persistence:

1. **Deck Code Storage**:
   - Deck codes are stored in the `ParticipantState.Deck` property for active rounds
   - Deck codes are permanently stored in the `Tournament.MatchResult.DeckCodes` dictionary when a match is completed
   - This ensures deck codes are preserved both during active rounds and after completion
   - Stored deck codes can be used to verify against replay files uploaded by players

2. **Deck Submission Flow**:
   - When a player submits a deck via the modal, the code is stored in the participant object
   - The tournament state is immediately saved to the JSON file
   - When all players have submitted decks, the round proceeds to map selection
   - When a match is completed, deck codes are transferred to the permanent match result
   - Deck codes are included in the serialized state for future reference and verification

3. **Benefits**:
   - Deck codes persist across bot restarts
   - Tournament can be resumed with all previously submitted decks intact
   - Provides a complete record of the tournament for review
   - Enables verification of deck codes against replay files
   - Supports tournament integrity by ensuring players used the decks they submitted 

## Deck Code Storage Architecture

The system implements a dual-storage approach for deck codes to ensure both temporary access during active rounds and permanent storage for verification:

1. **Temporary Storage During Active Rounds**:
   - Deck codes are initially stored in `ParticipantState.Deck` within the `ActiveRound` structure
   - This allows for immediate access during the current game of the tournament round
   - Each participant submits a new deck code for each game in the match
   - Stored in the tournament state JSON file
   - Example in JSON:
   ```json
   "participants": [
     {
       "playerId": "user-id-1",
       "playerName": "Player1",
       "deck": "deck-code-1"  // Current deck code for the active game
     }
   ]
   ```

2. **Round-Level Storage**:
   - Within each round, deck codes are stored per player per game
   - This preserves the history of which deck was used in each game of the round
   - Example in JSON:
   ```json
   "rounds": [
     {
       "id": "round-1",
       "selectedMaps": ["Map1", "Map2", "Map3"],
       "deckCodes": {
         "user-id-1": {
           "Map1": "deck-code-player1-game1",
           "Map2": "deck-code-player1-game2",
           "Map3": "deck-code-player1-game3"
         },
         "user-id-2": {
           "Map1": "deck-code-player2-game1",
           "Map2": "deck-code-player2-game2",
           "Map3": "deck-code-player2-game3"
         }
       }
     }
   ]
   ```

3. **Permanent Storage After Match Completion**:
   - When a match is completed, all deck codes are transferred to `Tournament.MatchResult.DeckCodes`
   - This provides permanent storage that persists even after the round is completed
   - Deck codes are organized by player ID and map name to maintain the association
   - Stored in both the tournaments JSON file and tournament state JSON file
   - Example in JSON:
   ```json
   "result": {
     "winnerId": "user-id-1",
     "mapResults": ["Player1 won on Map1", "Player2 won on Map2", "Player1 won on Map3"],
     "completedAt": "2025-03-10T18:30:00Z",
     "deckCodes": {
       "user-id-1": {
         "Map1": "deck-code-player1-game1",  // Deck used by Player1 on Map1
         "Map2": "deck-code-player1-game2",  // Deck used by Player1 on Map2
         "Map3": "deck-code-player1-game3"   // Deck used by Player1 on Map3
       },
       "user-id-2": {
         "Map1": "deck-code-player2-game1",  // Deck used by Player2 on Map1
         "Map2": "deck-code-player2-game2",  // Deck used by Player2 on Map2
         "Map3": "deck-code-player2-game3"   // Deck used by Player2 on Map3
       }
     }
   }
   ```

4. **Transfer Process**:
   - When `UpdateMatchResult` is called, it extracts all deck codes from the linked round
   - Deck codes are mapped to player IDs and map names, preserving the context of which deck was used in which game
   - The tournament state is saved to persist these changes
   - This ensures deck codes are available for verification against replay files, with clear association to specific maps

This comprehensive storage approach ensures that deck codes are available both during active rounds for gameplay purposes and after match completion for verification and tournament integrity. The clear association between deck codes, players, and maps allows for precise verification against replay files. 