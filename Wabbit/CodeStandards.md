# Code Standards for Wabbit Project

## Type Checking Guidelines

1. **Type Checking with 'is' / 'is not'**
   - Use `is` and `is not` for checking types and null values
   - Example: `if (obj is null)`, `if (user is DiscordMember)`
   - These cannot be overloaded and perform strict type checks
   - For type checking with assignment: `if (obj is DiscordMember member) { /* use member */ }`

2. **Equality with '==' / '!='**
   - Only use for primitive types (int, bool, string, etc.)
   - Avoid using for reference types where possible
   - These can be overloaded by types, so behavior may be inconsistent
   - Bad: `if (team == null)` - Good: `if (team is null)`

3. **Comparing Objects**
   - Prefer `.Equals()` method when comparing complex objects
   - Example: `color.Equals(DiscordColor.Red)`
   - For strings, use methods that allow specifying comparison rules:
     ```csharp
     string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase)
     str1.Contains(str2, StringComparison.OrdinalIgnoreCase)
     ```

## Null Handling

⚠️ **CRITICAL: Preventing Null Reference Exceptions** ⚠️
- Null reference errors are a top priority to prevent
- Always check for null before accessing properties or methods of potentially null objects
- Use defensive coding patterns to ensure your code handles unexpected null values
- When fixing bugs, carefully review all potential null reference points
- Add fallback values to handle null cases gracefully
- Examples of proper null handling:
  ```csharp
  // Use null conditional and null coalescing operators
  string name = user?.Name ?? "Unknown";
  
  // Check collections before accessing
  if (items?.Any() ?? false) { /* ... */ }
  
  // Add null checks to method parameters
  if (parameter is null) return "Default";
  
  // Chain null protections
  return participant?.GetType()?.GetProperty("Username")?.GetValue(participant)?.ToString() ?? "Unknown";
  ```

1. **Nullable Reference Types**
   - Use null conditional operator (`?.`) when accessing members of potentially null objects
   - Example: `team?.Participants?.Any(p => p?.Player?.Id == userId)`

2. **Null Coalescing**
   - Use null coalescing operator (`??`) to provide fallback values
   - Example: `string name = team?.Name ?? "Unknown Team"`
   - Combine with null conditional: `teams?.FirstOrDefault()?.Name ?? "Unknown"`

3. **Collection Handling**
   - Always check collections for null before accessing members
   - Use null conditional access: `if (teams?.Any() ?? false)`
   - Check collection counts: `if (list?.Count > 0)`

4. **Pattern Matching with Nulls**
   - Use pattern matching for more expressive null checks:
     ```csharp
     if (property.GetType().GetProperty("Username") is System.Reflection.PropertyInfo usernameProperty)
     {
         // Use usernameProperty directly
     }
     ```

5. **Handling ToString() and Other Potentially Null-Returning Methods**
   - Even on non-null objects, some methods like `ToString()` might return null in certain implementations
   - Always use nullable type annotations for variables that store these results
   - Use string validation methods to handle potential nulls:
     ```csharp
     // Correct:
     string? result = someObject.ToString();
     return string.IsNullOrEmpty(result) ? "Unknown" : result;
     
     // Also correct (more concise):
     return string.IsNullOrEmpty(someObject.ToString()) ? "Default" : someObject.ToString();
     
     // Defensive approach for high-risk scenarios:
     try
     {
         string? result = someObject?.ToString();
         return string.IsNullOrEmpty(result) ? "Default" : result;
     }
     catch
     {
         return "Default";
     }
     ```

   - Consider wrapping risky operations in `try-catch` blocks when calling methods on objects from external sources or dynamic types
   - ⚠️ **WARNING**: Even though an object is non-null, methods like `ToString()` can return null
   - ⚠️ **WARNING**: Using non-nullable types (like `string` instead of `string?`) for `ToString()` results will cause nullable warning errors
   - Example of incorrect code that will cause warnings:
     ```csharp
     // Incorrect - will cause nullable warning:
     string result = participant.ToString(); // CS8600: Converting null literal or possible null value to non-nullable type
     ```
   - Compiler warnings about nullable types should be taken seriously as they often indicate potential runtime errors

## Coding Practices

1. **Line Length**
   - Maximum line length should not exceed 160 characters
   - Break long lines at logical points for better readability
   - For method chains, break before the dot:
     ```csharp
     // Good
     var query = collection
         .Where(x => x.IsActive)
         .OrderBy(x => x.Name)
         .Select(x => new { x.Id, x.Name });
     
     // For long conditions, break at logical operators
     if (condition1 && 
         condition2 && 
         condition3)
     {
         // ...
     }
     ```
   - Long strings can be split using string concatenation or string interpolation on multiple lines

2. **Async/Await**
   - Always use await when calling async methods
   - Always add proper exception handling in async methods

3. **Error Handling**
   - Log all exceptions with appropriate severity levels
   - Provide user-friendly error messages

4. **String Operations**
   - Always specify StringComparison when comparing strings:
     ```csharp
     if (name.Equals("admin", StringComparison.OrdinalIgnoreCase)) { /* ... */ }
     if (text.Contains("error", StringComparison.OrdinalIgnoreCase)) { /* ... */ }
     var result = names.FirstOrDefault(n => n.StartsWith("bot", StringComparison.OrdinalIgnoreCase));
     ```

## DSharpPlus Guidelines

Our project uses DSharpPlus version 5.0.0-nightly-02454. Follow these guidelines for consistent usage:

1. **Type Names**
   - Use `DiscordChannelType` enum for channel types, not `ChannelType`
   - Examples:
     ```csharp
     // Correct:
     if (channel.Type is DiscordChannelType.PrivateThread) { /* ... */ }
     
     // Incorrect:
     if (channel.Type is ChannelType.PrivateThread) { /* ... */ }
     ```

2. **Response Types and Message Visibility**
   - Use interaction deferring consistently to handle Discord API timeouts
   - Make error messages ephemeral (visible only to the user)
   - Make confirmations visible to all team members when appropriate
   - Example for ephemeral messages:
     ```csharp
     // Ephemeral message (only visible to the user who triggered the interaction)
     await interaction.CreateResponseAsync(
         DiscordInteractionResponseType.ChannelMessageWithSource,
         new DiscordInteractionResponseBuilder().WithContent("Message").AsEphemeral()
     );
     
     // Defer the response to buy time for processing
     await interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
     ```

3. **Handling User Mentions**
   - For simple user mentions, use the `.Mention` property with string interpolation:
     ```csharp
     // Direct mention
     await channel.SendMessageAsync($"{user.Mention} Your message here");
     ```
   - To control which mentions are processed in a message, use the `WithAllowedMentions()` method:
     ```csharp
     // Allow only a specific user mention (prevents accidental mass pings)
     var message = new DiscordMessageBuilder()
         .WithContent($"Hello {user.Mention}!")
         .WithAllowedMentions([new UserMention(user)]);
     await channel.SendMessageAsync(message);
     
     // Allow only a specific role mention
     var message = new DiscordMessageBuilder()
         .WithContent($"Attention {role.Mention}!")
         .WithAllowedMentions([new RoleMention(role)]);
     await channel.SendMessageAsync(message);
     
     // Disable all mentions completely
     var message = new DiscordMessageBuilder()
         .WithContent("This message won't ping anyone, even if it contains @everyone")
         .WithAllowedMentions([]);
     await channel.SendMessageAsync(message);
     ```

   - **Important Notes on WithAllowedMentions Syntax**:
     - The collection expression shorthand syntax (`[new UserMention(user)]`) requires .NET 9.0 and C# 12 (which are project is built on)
     - This syntax is a standard feature of our project and MUST be used (not optional)
     - The required types (`UserMention`, `RoleMention`) are all part of the `DSharpPlus.Entities` namespace (already imported in most files)
     - This shorthand is preferred over the more verbose `new List<IMention>` or array syntax
     - Examples of correct usage:
       ```csharp
       // Single user mention (most common case)
       .WithAllowedMentions([new UserMention(member)]);
       
       // Multiple mentions
       .WithAllowedMentions([new UserMention(user1), new UserMention(user2)]);
       
       // No mentions
       .WithAllowedMentions([]);
       ```
     - ⚠️ WARNING: Do not use older syntax like `new IMention[]` or `new List<IMention>` as these are not compatible with our coding standards
   
   - For user visibility and notification management:
     - Make error messages ephemeral (only visible to the user who triggered them)
     - Make success messages visible to all relevant participants in the channel
     - Use bold formatting for usernames instead of mentions to avoid notification spam in results:
       ```csharp
       // Instead of mentioning users in results, format their username
       await channel.SendMessageAsync($"**{user.Username}** has won the game!");
       ```
     - When sending public success messages, include a follow-up to the user explaining that the message is visible to everyone

4. **Component Handling**
   - Prefer using the component ID pattern for routing in handlers:
     ```csharp
     if (e.Id.StartsWith("confirm_") || e.Id == "submit_button")
     ```
   - Use pattern matching when switching on component IDs:
     ```csharp
     switch (e.Id)
     {
         case string s when s.StartsWith("confirm_"):
             // Handle confirmation
             break;
         case "submit_button":
             // Handle submission
             break;
     }
     ```

5. **Response Method Naming Conventions**
   - Use `SendErrorResponseAsync` for error messages (uses red color by default)
   - Use `SendResponseAsync` for success or informational messages with custom colors
   - Examples:
     ```csharp
     // For error messages:
     await SendErrorResponseAsync(e, "An error occurred", hasBeenDeferred);
     
     // For success messages:
     await SendResponseAsync(e, "Operation successful", hasBeenDeferred, DiscordColor.Green);
     
     // For informational messages:
     await SendResponseAsync(e, "Processing your request", hasBeenDeferred, DiscordColor.Blue);
     ```
   - This maintains clarity about the intent of the message in the code

## Autocomplete Standards

1. **Trailing Commas**
   - Always add trailing commas to autocomplete suggestions and arrays
   - Example: 
     ```csharp
     new[] { 
         "Item1", 
         "Item2",  // Note the trailing comma
     }
     ```
   - This makes future additions cleaner in git diffs

This file lists common patterns to follow in the codebase. It should be consulted when adding or modifying code. 