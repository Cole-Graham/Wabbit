# Focused Player → Team Terminology Changes

This document proposes targeted changes to convert "player" terminology to "team" terminology in variables, parameters, and comments, without modifying property names like "Player1Username" yet.

## Strategy

1. Focus only on variable names, parameters, and comments in method bodies
2. Preserve property names like "Player1Username" and "Player2Username" for now
3. Make minimal surgical changes to maintain consistency

## Proposed Changes

### 1. TournamentMatchService.cs

```csharp
/// <summary>
/// Creates and starts a 1v1 match between two teams
/// </summary>
/// <remarks>
/// This method assumes that team scheduling (ensuring teams aren't double-booked)
/// has already been handled by the TournamentManagerService's scheduling system.
/// </remarks>
/// <param name="tournament">The tournament the match belongs to</param>
/// <param name="group">The group the match belongs to (null for playoff matches)</param>
/// <param name="team1">The first team</param>
/// <param name="team2">The second team</param>
/// <param name="client">The Discord client</param>
public async Task CreateAndStart1v1Match(
    Tournament tournament,
    Tournament.Group? group,
    DiscordMember team1,
    DiscordMember team2,
    DiscordClient client,
    int matchLength,
    Tournament.Match? existingMatch = null)
{
    // Method body with team1/team2 variable names
    
    // Example variable rename:
    // Create team objects
    var team1Object = new Round.Team
    {
        Name = team1?.DisplayName ?? "Team 1",
        Participants = new List<Round.Participant>
        {
            new Round.Participant { Player = team1 }
        },
        MapBans = new List<string>()
    };
    
    // ...rest of method...
}
```

### 2. TournamentGameService.cs

```csharp
// Variables in methods:
public int GetPlayerScore(Round round, ulong playerId)
{
    if (round.CustomProperties == null)
        return 0;

    string playerKey = playerId.ToString();
    if (round.Teams?.FirstOrDefault()?.Participants?.FirstOrDefault()?.Player?.Id.ToString() == playerKey)
    {
        return round.CustomProperties.ContainsKey("Player1Wins") ?
            Convert.ToInt32(round.CustomProperties["Player1Wins"]) : 0;
    }
    // ...rest of method...
}
```

### 3. ServiceHelpers/TournamentMatchOperationsService.cs

```csharp
/// <inheritdoc/>
public Tournament.Match CreateMatch(
    string matchName,
    TournamentMatchType matchType,
    int bestOf,
    DiscordMember team1,
    DiscordMember team2,
    Tournament.Group? sourceGroup = null)
{
    var match = new Tournament.Match
    {
        Name = matchName,
        Type = matchType,
        BestOf = bestOf,
        Participants = new List<Tournament.MatchParticipant>
        {
            new Tournament.MatchParticipant
            {
                Player = team1,
                SourceGroup = sourceGroup
            },
            new Tournament.MatchParticipant
            {
                Player = team2,
                SourceGroup = sourceGroup
            }
        }
    };

    // ...rest of method...
}

/// <inheritdoc/>
public bool ValidateMatchCreation(
    Tournament tournament,
    DiscordMember team1,
    DiscordMember team2)
{
    // Check if teams are different
    if (team1.Id == team2.Id)
    {
        _logger.LogError("Cannot create match between the same team");
        return false;
    }
    
    // ...rest of method...
}
```

### 4. TournamentManagerService.cs

```csharp
// Method implementation:
public async Task StartMatchRoundAsync(Tournament tournament, Tournament.Match match, DiscordChannel channel, DiscordClient client)
{
    // Get team members
    var team1 = _groupService.ConvertToDiscordMember(match.Participants[0].Player);
    var team2 = _groupService.ConvertToDiscordMember(match.Participants[1].Player);

    // Ensure both teams are valid
    if (team1 is null || team2 is null)
    {
        _logger.LogError($"Cannot start match {match.Name}: One or both teams could not be converted to DiscordMember");
        return;
    }

    // ...rest of method...
}
```

## Implementation Plan

1. Make the changes to variable names and parameters in method bodies
2. Update comments to reflect team terminology
3. Keep property names unchanged for now ("Player1Username", etc.)
4. Ensure existing functionality is preserved

This approach allows us to isolate the property naming issue for later while making progress on the terminology consistency. 