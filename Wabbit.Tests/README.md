# Wabbit Tournament Logic Tests

This project contains unit tests for the tournament logic in the Wabbit Discord bot. The tests focus on verifying the correctness of the tournament-related algorithms and business logic without requiring Discord interaction.

## Test Infrastructure Architecture

1. **TournamentGroupTests**: Tests for group creation logic based on participant count.
2. **SeedDistributionTests**: Tests for distributing participants according to seeding.
3. **MatchCreationTests**: Tests for creating matches from groups and associated threads.
4. **MapBanCountTests**: Tests for determining the number of map bans based on match length.
5. **ConditionalMapBanTests**: Tests for map ban guarantees and conditions based on match length.
6. **MatchWinnerTests**: Tests for match result determination and completion logic.
7. **GroupStageCompletionTests**: Tests for determining group stage completion and advancing participants.
8. **BracketGenerationTests**: Tests for bracket generation with proper seeding.

1. **Adapter Classes**: Test-specific service implementations that bridge between test expectations and actual service interfaces
2. **Model Converters**: Utilities for converting between test models and production models
3. **Mock Factories**: Utilities for creating pre-configured mock objects that avoid expression tree issues
4. **Test Data Factories**: Utilities for generating test data in the expected format
5. **TestBase**: Base class for tests that provides common mock setup and service creation

### Key Components

#### Adapter Classes

- **TestTournamentService**: Adapter for `TournamentService` that implements test-specific methods
- **TestTournamentGroupService**: Adapter for `TournamentGroupService` with methods needed for testing
- **TestTournamentMapService**: Adapter for `TournamentMapService` with methods like `GetMapBanCount`
- **TestMatchStatusService**: Adapter for `MatchStatusService` with simplified match result updates

#### Model Classes

- **TestParticipantInfo**: Test-specific model for tournament participants
- **TestTournament**: Test-specific model for tournaments with additional properties
- **TestTournament.GroupParticipant**: Class for participants in tournament groups
- **TestTournament.Bracket**: Class for tournament brackets
- **TestTournament.Match**: Class for tournament matches
- **TestTournament.MatchParticipant**: Class for participants in bracket matches

#### Utility Classes

- **ModelConverters**: Static class with methods for converting between test and production models
- **MockFactory**: Factory for creating pre-configured Discord mock objects
- **TestDataFactory**: Factory for creating test data objects (participants, groups, etc.)

## Using the Test Infrastructure

### Writing Tests

Tests should inherit from `TestBase` to get access to the mock objects and service creation methods:

```csharp
public class MyTournamentTests : TestBase
{
    [Fact]
    public void MyTest()
    {
        // Create test-specific service instances
        var tournamentService = CreateTestTournamentService();
        
        // Generate test data
        var participants = TestDataFactory.CreateTestParticipants(8, withSeeding: true);
        
        // Use the test-specific service methods
        var groups = tournamentService.CreateGroups(participants);
        
        // Make assertions
        Assert.Equal(2, groups.Count);
    }
}
```

### Creating Test Data

Use the `TestDataFactory` to create test data:

```csharp
// Create participants
var participants = TestDataFactory.CreateTestParticipants(8, withSeeding: true);

// Create a tournament round
var round = TestDataFactory.CreateTestRound("Test Match", 3); // Bo3 match

// Create a tournament with groups
var tournament = TestDataFactory.CreateTestTournament("Test Tournament", 2, 4); // 2 groups, 4 players each
```

### Converting Between Models

Use the `ModelConverters` class to convert between test and production models:

```csharp
// Convert test participant to production participant
var productionParticipant = testParticipant.ToProductionModel();

// Convert list of test participants to production participants
var productionParticipants = testParticipants.ToProductionModels();

// Create mock DiscordMember from participant info
var member = ModelConverters.CreateMockDiscordMember(participant);
```

## Implemented Tests

### MapBanCountTests

Tests to verify the map ban count logic for different match lengths:

- `GetMapBanCount_ReturnsCorrectNumberOfBans`: Tests the map ban count logic for Bo1, Bo3, and Bo5 matches
- `RecordMapBan_ForBo3_AcceptsCorrectBans`: Tests recording map bans for Bo3 matches
- `RecordMapBan_ForBo5_AcceptsCorrectBans`: Tests recording map bans for Bo5 matches
- `GetMapBanCount_UsesDifferentCountsByMatchLength`: Tests the consistency of map ban counts

## Current Implementation Status

The testing infrastructure has been partially implemented:

1. ✅ **Core adapter classes** have been implemented (TestTournamentService, TestTournamentGroupService, etc.)
2. ✅ **Model converters** for bridging test and production models
3. ✅ **Mock factories** for DSharpPlus objects to avoid expression tree issues
4. ✅ **Test data factory** for generating test data
5. ✅ **Updated MapBanCountTests** to use the new infrastructure
6. ❌ **Remaining test classes** need to be updated to use the infrastructure
7. ❌ **Some linter errors** need to be fixed in TestTournamentMapService and MockFactory

## Known Issues

1. **Expression Tree Issues**: There are still some expression tree issues in MockFactory.cs for methods with optional parameters
2. **Method Signature Mismatches**: There are mismatches between TestTournamentMapService.GetRandomMaps signature and actual implementation
3. **Type Conversion Issues**: Need to solve the issue in TestTournamentService where TestGroupService.CreateGroups expects TestParticipantInfo but ModelConverters.ToProductionModels returns ModelsParticipantInfo

## Next Steps

To complete the testing infrastructure, follow these steps:

1. **Fix Mock Factory Implementation**: Update the MockFactory class to properly handle optional arguments in expression trees
2. **Resolve Method Signature Mismatches**: Update the TestTournamentMapService class to match the actual method signatures
3. **Fix Type Conversion Issues**: Update the CreateGroups method in TestTournamentService to handle type conversions properly
4. **Update Remaining Test Classes**: Systematically update each remaining test class to use the testing infrastructure:
   - TournamentGroupTests
   - SeedDistributionTests
   - MatchCreationTests
   - ConditionalMapBanTests
   - MatchWinnerTests
   - GroupStageCompletionTests
   - BracketGenerationTests
5. **Add More Model Converters**: Add additional model converters as needed for tournament matches, rounds, etc.
6. **Add Test-Specific Methods**: Add more test-specific methods to the adapter classes as needed by tests

## Running Tests

Once the implementation issues are resolved:

```bash
# Run all tests
dotnet test Wabbit.Tests

# Run a specific test category
dotnet test Wabbit.Tests --filter "FullyQualifiedName~Wabbit.Tests.MapBanCountTests"
```

## Test Design Philosophy

These tests are designed to verify the tournament logic in isolation from Discord interactions. This allows for testing the core tournament algorithms independently of the Discord API, making tests faster, more reliable, and easier to maintain.

The tests use mocks for Discord-related dependencies, allowing the tests to focus on the tournament logic without requiring an actual Discord connection. The test infrastructure bridges the gaps between test expectations and actual implementations. 