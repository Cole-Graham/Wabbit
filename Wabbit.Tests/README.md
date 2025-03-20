# Wabbit Tournament Logic Tests

This project contains unit tests for the tournament logic in the Wabbit Discord bot. The tests focus on verifying the correctness of the tournament-related algorithms and business logic without requiring Discord interaction.

## Test Categories

1. **TournamentGroupTests**: Tests for group creation logic based on participant count.
2. **SeedDistributionTests**: Tests for distributing participants according to seeding.
3. **MatchCreationTests**: Tests for creating matches from groups and associated threads.
4. **MapBanCountTests**: Tests for determining the number of map bans based on match length.
5. **ConditionalMapBanTests**: Tests for map ban guarantees and conditions based on match length.
6. **MatchWinnerTests**: Tests for match result determination and completion logic.
7. **GroupStageCompletionTests**: Tests for determining group stage completion and advancing participants.
8. **BracketGenerationTests**: Tests for bracket generation with proper seeding.

## Implementation Status

The test project is currently in a **planning state** with structural implementation of test classes. There are a number of interface mismatches between the tests and the actual implementation that need to be resolved:

1. The actual interfaces in the Wabbit project have different method signatures than those assumed in the tests.
2. Some properties, like `Tournament.GroupParticipant.Points`, appear to be read-only in the implementation but are modified in the tests.
3. The actual service constructor parameters differ from what's used in the tests.
4. Some expected methods, like `TournamentService.CreateGroups()`, either don't exist or have different signatures in the actual implementation.

## Next Steps

To make the tests usable, the following steps are needed:

1. Adjust test implementations to match the actual interfaces and methods in the Wabbit project.
2. Consider implementing adapters or wrapper classes for testing if the actual interfaces are complex.
3. Use proper constructor arguments for services or consider using more extensive mocking.
4. Verify the actual property access patterns and adjust tests accordingly.

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

The tests use mocks for Discord-related dependencies, allowing the tests to focus on the tournament logic without requiring an actual Discord connection. 