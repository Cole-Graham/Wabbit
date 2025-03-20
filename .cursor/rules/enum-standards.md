# Enum Standards

## Generated Specialized Enums

1. When working with specialized generated enums (marked with `[GenerateSpecializedEnums]`), follow these guidelines:

   ```csharp
   // Base enum definition
   [GenerateSpecializedEnums("Scrimmage", "Signup", "Team", "Tournament")]
   public enum GameType
   {
       [Display(Name = "1v1")]
       OneVOne,
       
       [Display(Name = "2v2")]
       TwoVTwo,
       
       // ...
   }
   ```

2. Use the specific specialized enum type directly in method signatures:

   ```csharp
   // Correct approach: Use the specialized enum type directly
   public TournamentSignup CreateSignup(
       string name,
       TournamentFormat format,
       DiscordUser creator,
       ulong signupChannelId,
       SignupGameType gameType = SignupGameType.OneVOne,
       DateTime? scheduledStartTime = null)
   {
       // Use the enum directly without conversion
       return _signupService.CreateSignup(name, format, creator, signupChannelId, gameType, scheduledStartTime);
   }
   ```

3. Avoid unnecessary enum conversions:

   ```csharp
   // Incorrect - unnecessary conversion
   var tournamentGameType = (TournamentGameType)(int)parsedGameType;
   
   // Correct - direct usage without conversion
   var signup = _signupService.CreateSignup(
       name,
       format,
       creator,
       channelId,
       parsedGameType,
       scheduledStartTime);
   ```

## Displaying Enum Values

1. Create extension methods for consistent display of enum values:

   ```csharp
   // Extension method to get display names for any specialized enum type
   public static class GameTypeExtensions 
   {
       public static string ToDisplayString<T>(this T gameType) where T : Enum => gameType switch
       {
           { } when gameType.ToString() == nameof(GameType.OneVOne) => "1v1",
           { } when gameType.ToString() == nameof(GameType.TwoVTwo) => "2v2",
           { } when gameType.ToString() == nameof(GameType.ThreeVThree) => "3v3",
           { } when gameType.ToString() == nameof(GameType.FourVFour) => "4v4",
           _ => gameType.ToString()
       };
   }
   ```

2. Use the extension method consistently:

   ```csharp
   // In UI display code
   builder.AddField("Game Type", signup.GameType.ToDisplayString(), true);
   ```

3. Alternative for local usage with switch expressions:

   ```csharp
   string gameType = signup.GameType switch
   {
       SignupGameType.OneVOne => "1v1",
       SignupGameType.TwoVsTwo => "2v2",
       SignupGameType.ThreeVsThree => "3v3",
       SignupGameType.FourVsFour => "4v4",
       _ => signup.GameType.ToString() // Fallback for any new types
   };
   ```

## Enum Parsing

1. Use `Enum.TryParse` with explicit enum type:

   ```csharp
   // Parse from user input string
   if (!Enum.TryParse<SignupGameType>(gameTypeString, out var parsedGameType))
   {
       _logger.LogError($"Invalid game type: {gameTypeString}");
       throw new InvalidOperationException($"Invalid game type: {gameTypeString}");
   }
   ```

2. For command choices, use exact enum names:

   ```csharp
   public class GameTypeChoiceProvider : IChoiceProvider
   {
       private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> gameTypes = new DiscordApplicationCommandOptionChoice[]
       {
           new("1v1", "OneVOne"),
           new("2v2", "TwoVsTwo"),
           new("3v3", "ThreeVsThree"),
           new("4v4", "FourVsFour"),
       };
   
       public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
       {
           return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(gameTypes);
       }
   }
   ```

## Best Practices

1. Use the appropriate specialized enum type directly in interfaces and implementations
2. Standardize on the most relevant enum type throughout the stack (e.g., use `SignupGameType` consistently if that's what the model uses)
3. Create extension methods for common operations like formatting display strings
4. Avoid unnecessary enum conversions between specialized types
5. Document the purpose of each specialized enum and when to use it
6. When updating one enum, ensure all related specialized enums are also updated consistently 