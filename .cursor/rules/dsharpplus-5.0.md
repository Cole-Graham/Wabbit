# DSharpPlus 5.0 Usage Rules

## Core Concepts

1. The codebase uses DSharpPlus version 5.0.0-nightly-02454.
2. All references to DSharpPlus types should use the fully qualified names from the DSharpPlus namespace.

## Enums and Types

1. Always use `DiscordChannelType` (not `ChannelType`) with values:
   ```csharp
   DiscordChannelType.PrivateThread
   DiscordChannelType.PublicThread
   ```

2. Always use `DiscordAutoArchiveDuration` (not `AutoArchiveDuration`) with values:
   ```csharp
   DiscordAutoArchiveDuration.Hour
   DiscordAutoArchiveDuration.Day
   DiscordAutoArchiveDuration.ThreeDays
   DiscordAutoArchiveDuration.Week
   ```

3. Use `DiscordInteractionResponseType` for interaction responses:
   ```csharp
   DiscordInteractionResponseType.ChannelMessageWithSource
   DiscordInteractionResponseType.DeferredMessageUpdate
   ```

4. Use `DiscordPermission` (not `DSharpPlus.Permissions`) for permission checks:
   ```csharp
   // Correct
   member.Permissions.HasFlag(DiscordPermission.Administrator)
   member.Permissions.HasFlag(DiscordPermission.ManageChannels)
   
   // Incorrect (outdated)
   member.Permissions.HasPermission(DSharpPlus.Permissions.Administrator)
   ```

## Command Context Checks

1. Implement `IContextCheck` interface (not `ContextCheckAttribute`) for permission checks:
   ```csharp
   [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
   public class RequireWhitelistedRoleAttribute : Attribute, IContextCheck
   {
       public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
       {
           // Return null for success, error message string for failure
       }
   }
   ```

2. Access dependency services via `ServiceProvider` property:
   ```csharp
   // Correct
   var service = context.ServiceProvider.GetRequiredService<IMyService>();
   
   // Incorrect (doesn't exist)
   var service = context.GetRequiredService<IMyService>();
   ```

3. Access command arguments using `CommandParameter` objects as keys:
   ```csharp
   // Get parameter by name
   var parameter = context.Arguments.Keys.FirstOrDefault(p => p.Name == parameterName);
   if (parameter == null)
       return "Parameter not found.";
       
   // Get the parameter value using the CommandParameter object as key
   if (!context.Arguments.TryGetValue(parameter, out var parameterValue) || parameterValue == null)
       return "Parameter value not found.";
       
   // Convert to string or other type as needed
   string? stringValue = parameterValue.ToString();
   ```

4. Always add proper null checks when accessing arguments:
   ```csharp
   if (parameter == null) { /* Handle missing parameter */ }
   if (parameterValue == null) { /* Handle missing value */ }
   if (string.IsNullOrEmpty(stringValue)) { /* Handle empty string */ }
   ```

5. For common imports in attribute classes:
   ```csharp
   using System.Linq; // For FirstOrDefault on Arguments.Keys
   using DSharpPlus.Commands; // For CommandContext
   using DSharpPlus.Commands.ContextChecks; // For IContextCheck
   using DSharpPlus.Commands.Trees; // For CommandParameter
   using Microsoft.Extensions.DependencyInjection; // For GetRequiredService
   ```

## Message Building

1. Use the builder pattern for creating messages:
   ```csharp
   var messageBuilder = new DiscordMessageBuilder()
       .WithContent("Message content")
       .AddEmbed(embedBuilder)
       .WithAllowedMentions([]);
   ```

2. For embeds, use:
   ```csharp
   var embedBuilder = new DiscordEmbedBuilder()
       .WithTitle("Title")
       .WithDescription("Description")
       .WithColor(DiscordColor.Green);
   ```

3. Always use collection expression syntax `[]` for mentions:
   ```csharp
   // No mentions
   .WithAllowedMentions([])
   
   // Single user mention
   .WithAllowedMentions([new UserMention(user)])
   
   // Multiple mentions
   .WithAllowedMentions([new UserMention(user1), new RoleMention(role)])
   ```

## Thread Management

1. Use `DiscordUtilities.CreateThreadAsync` helper method:
   ```csharp
   var thread = await DiscordUtilities.CreateThreadAsync(
       channel,
       threadName,
       _logger,
       DiscordChannelType.PrivateThread,
       DiscordAutoArchiveDuration.Day);
   ```

2. Add members to threads with the helper:
   ```csharp
   await DiscordUtilities.AddMembersToThreadAsync(thread, [member1, member2], _logger);
   ```

## Interaction Responses

1. Defer interactions to avoid timeouts:
   ```csharp
   await context.DeferResponseAsync();
   ```

2. Create ephemeral responses:
   ```csharp
   await interaction.CreateResponseAsync(
       DiscordInteractionResponseType.ChannelMessageWithSource,
       new DiscordInteractionResponseBuilder().WithContent("Message").AsEphemeral()
   );
   ```

3. Update responses:
   ```csharp
   await context.EditResponseAsync(new DiscordWebhookBuilder()
       .WithContent("Updated content"));
   ```

## Sending Messages

1. Use `SendMessageAsync` with builder:
   ```csharp
   await channel.SendMessageAsync(new DiscordMessageBuilder()
       .WithContent("Message content")
       .AddEmbed(embed)
       .WithAllowedMentions([]));
   ```

2. For mentioning users, prefer formatting over mentions:
   ```csharp
   // Prefer this (no notification)
   await channel.SendMessageAsync($"**{user.Username}** has won the game!");
   
   // Instead of this (sends notification)
   await channel.SendMessageAsync($"{user.Mention} has won the game!");
   ```

## Null Handling

1. Always use null conditional operator and safe checks:
   ```csharp
   // Check before accessing properties
   if (channel?.Type is DiscordChannelType.PrivateThread) { }
   
   // Use null coalescing
   var name = user?.Username ?? "Unknown";
   ```

## Type Checking

1. Use pattern matching with `is` for type checking:
   ```csharp
   if (obj is DiscordMember member) {
       // Use member directly here
   }
   ```

2. Use null checks with `is null`:
   ```csharp
   if (message is null) {
       // Handle null case
   }
   ``` 