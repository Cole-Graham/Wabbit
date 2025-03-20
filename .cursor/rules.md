# Wabbit Project Rules

This file defines the rules and guidelines for coding in the Wabbit Discord bot project. These rules help ensure consistent, high-quality code across the codebase.

## Rules Files

The following rule files contain specific guidelines:

1. [DSharpPlus 5.0 Usage](.cursor/rules/dsharpplus-5.0.md) - Guidelines for working with DSharpPlus 5.0.0-nightly-02454
2. [Code Standards](.cursor/rules/code-standards.md) - General coding standards for the project
3. [Permission Handling](.cursor/rules/permission-handling.md) - Guidelines for handling Discord permissions
4. [Enum Standards](.cursor/rules/enum-standards.md) - Working with enums and specialized generated enums
5. [C# 12 Features](.cursor/rules/csharp-12-features.md) - Guidelines for utilizing C# 12 language features

## Repository Information

- Project: Wabbit - A Discord bot for managing Wabbit tournaments and scrimmages
- Framework: .NET 9.0
- Language: C# 12
- Discord API: DSharpPlus 5.0.0-nightly-02454

## Important Principles

1. **Type Safety** - Prefer strongly-typed approaches over dynamic or stringly-typed approaches
2. **Null Safety** - Always handle potential null references with proper null checks
3. **Error Handling** - Include proper exception handling and logging
4. **Code Consistency** - Follow established patterns in the codebase
5. **Performance** - Be mindful of asynchronous code and avoid blocking operations
6. **Permission System** - Always use Discord's built-in permission system (no legacy admin lists)

## Essential Guidelines

1. Add proper XML documentation to public methods and classes
2. Follow async/await best practices throughout the codebase
3. Use the appropriate DSharpPlus types and methods as defined in the rules
4. Ensure proper error handling and logging for Discord API interactions
5. Follow the nullable reference types guidance consistently
6. NEVER write backward compatibility code unless explicitly instructed to do so

## Permission Handling

1. Always use Discord's built-in permission system for checking admin privileges:
   - Check for `DiscordPermission.Administrator` or server owner status
   - Consider appropriate moderator permissions like `DiscordPermission.ManageGuild`
   - Do NOT use custom admin lists or hardcoded user IDs
2. Create proper permission checking methods in services
3. Apply consistent permission checks in command handlers
4. Use appropriate error messages for permission failures

Always refer to the specific rules files for detailed guidance on particular topics. 