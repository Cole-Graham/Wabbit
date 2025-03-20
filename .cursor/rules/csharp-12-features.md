# C# 12 Language Features

This project takes advantage of C# 12 language features. Here are the ones used consistently throughout the codebase:

## Collection Expressions

1. Use collection expressions `[...]` for creating arrays, lists, and other collections:

   ```csharp
   // Preferred for creating arrays
   string[] items = ["one", "two", "three", ];
   
   // Preferred for creating lists
   var options = ["Option A", "Option B", "Option C", ];
   
   // Preferred for parameter lists like allowed mentions
   .WithAllowedMentions([]);
   .WithAllowedMentions([new UserMention(user)]);
   ```

2. Always add trailing commas for maintainability:

   ```csharp
   // Correct - with trailing comma
   var items = [
       "Item1",
       "Item2",
       "Item3", // Makes future additions easier
   ];
   
   // Also applies to traditional array/collection initializers
   var options = new[]
   {
       "Option1",
       "Option2", // Trailing comma
   };
   ```

## Primary Constructors

1. Use primary constructors for simple service classes:

   ```csharp
   public class ExampleService(ILogger logger, IDataService dataService)
   {
       private readonly ILogger _logger = logger;
       private readonly IDataService _dataService = dataService;
       
       public async Task DoSomethingAsync()
       {
           _logger.LogInformation("Doing something");
           await _dataService.GetDataAsync();
       }
   }
   ```

## Required Members

1. Use required properties for models that must have certain properties set:

   ```csharp
   public class Tournament
   {
       public required string Name { get; set; }
       public required ulong CreatorId { get; set; }
       public required DiscordChannel AnnouncementChannel { get; set; }
       public required TournamentGameType GameType { get; set; }
       
       // Optional properties don't use required
       public DateTime? EndDate { get; set; }
   }
   ```

## Pattern Matching

1. Use pattern matching extensively:

   ```csharp
   // Type pattern
   if (result is DiscordMember member)
   {
       await channel.SendMessageAsync($"Found member: {member.Username}");
   }
   
   // Property pattern
   if (tournament is { IsActive: true, Format: TournamentFormat.SingleElimination })
   {
       // Handle active single elimination tournament
   }
   
   // Switch expressions with patterns
   var description = match.Status switch
   {
       MatchStatus.Scheduled => "Match is scheduled",
       MatchStatus.InProgress => "Match is in progress",
       MatchStatus.Completed { WinnerId: not null } winner => $"Match won by {winner.Username}",
       _ => "Unknown status"
   };
   ```

## Range and Index Syntax

1. Use range and index syntax for slicing collections:

   ```csharp
   // Get first 5 items
   var top5 = leaderboard[..5];
   
   // Get items 5-10
   var nextGroup = leaderboard[5..10];
   
   // Get from index to end
   var remainingItems = items[startIndex..];
   
   // Last item using ^1
   var lastItem = list[^1];
   ```

## File-Scoped Namespaces

1. Use file-scoped namespaces:

   ```csharp
   // Preferred
   namespace Wabbit.Services;
   
   public class ExampleService
   {
       // Implementation
   }
   
   // Instead of
   namespace Wabbit.Services
   {
       public class ExampleService
       {
           // Implementation
       }
   }
   ```

## New String Formatting

1. Use string interpolation with multiline support:

   ```csharp
   var message = $"""
       # Tournament: {tournament.Name}
       
       **Format:** {tournament.Format}
       **Players:** {tournament.Players.Count}
       
       Good luck and have fun!
       """;
   ```

## Lambda Improvements

1. Use attributes on lambdas when needed:

   ```csharp
   var handler = [DisallowNull] (DiscordMessage message) => {
       return message.Content.Length > 0;
   };
   ```

## Static Abstract Members in Interfaces

1. Use static abstract members for interfaces that define static behavior:

   ```csharp
   public interface IGameType
   {
       static abstract string DisplayName { get; }
       static abstract GameType ToBaseType();
   }
   ```

Always refer to these patterns when writing new code or modifying existing code to maintain consistency. 