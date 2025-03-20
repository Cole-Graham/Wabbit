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

## Implementation Plan

The following is a comprehensive plan to address the issues and make the tests fully functional:

### Phase 1: Analyze & Document Actual Interfaces
1. **Document Actual Service Interfaces**
   - Examine the actual `TournamentService`, `MatchStatusService`, and other service implementations
   - Document constructor parameters, method signatures, and properties
   - Create a table mapping expected interfaces to actual implementations

2. **Add Missing Dependencies**
   - Add Microsoft.Extensions.Logging reference
   - Add specific namespace imports
   - Create a TestBase class with common mock setups

### Phase 2: Create Test Infrastructure
1. **Create Adapter/Facade Classes**
   - Implement test-specific service wrappers
   - Provide simplified APIs for testing purposes
   - Bridge between test expectations and actual implementations

2. **Create Model Extensions**
   - Add extension methods to bridge property differences
   - Create test-specific model classes where needed
   - Implement converters between test models and actual models

### Phase 3: Fix Specific Issues
1. **Missing Methods**
   - Replace direct calls to missing methods with adapter method calls
   - Implement missing functionality in the test project if needed
   - Update tests to use the actual method names/signatures

2. **Property Access Issues**
   - Update TestHelpers.cs to use the correct property access patterns
   - Create builders/factories that set properties using constructor parameters
   - Use reflection for testing-only scenarios where properties are read-only

3. **Type Incompatibilities**
   - Add type converters between Round and Tournament.Match
   - Fix nullability issues in mock setups
   - Update test assertions to work with the actual types

### Phase 4: Incremental Implementation
1. **Start with the TestHelpers Class**
   - Fix the base infrastructure first
   - Update model creation helpers to match actual implementation

2. **Implement One Test Class at a Time**
   - Start with simple classes like MapBanCountTests
   - Progress to more complex tests like BracketGenerationTests

3. **Create Test-Specific Version of Services**
   - Implement test-specific versions of critical services
   - Focus on the specific functionality needed for testing

### Phase 5: Mock Optimizations
1. **Optimize Mock Setups**
   - Fix expression tree issues by using proper lambda expressions
   - Centralize common mock setups in a TestBase class
   - Create reusable mock configurations

2. **Refactor Tests for Better Isolation**
   - Reduce dependencies on concrete implementations
   - Use more interfaces and fewer concrete classes in tests

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