# Code Standards

## Naming Conventions

1. Use PascalCase for:
   - Class names
   - Public properties and methods
   - Interface names (prefixed with 'I')
   - Enum names and values

2. Use camelCase for:
   - Private fields (prefixed with underscore: `_fieldName`)
   - Local variables and parameters

## Backward Compatibility

1. NEVER write code for backward compatibility unless explicitly instructed to do so.
2. Do not maintain legacy systems or patterns that are being replaced.
3. When updating code, focus on the new implementation pattern only.
4. Remove deprecated code and patterns when encountered.
5. Prioritize clean, modern implementations over maintaining compatibility with old patterns.

## Type Checking 

1. Use `is` and `is not` for type checking, never `==` for reference types:
   ```csharp
   // Correct
   if (obj is null) { }
   if (user is DiscordMember member) { }
   
   // Incorrect
   if (obj == null) { }
   ```

2. For strings, always specify comparison rules:
   ```csharp
   string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase)
   str1.Contains(str2, StringComparison.OrdinalIgnoreCase)
   ```

## Null Handling

1. Use null conditional operator (`?.`) liberally:
   ```csharp
   var name = user?.Username;
   ```

2. Use null coalescing operator (`??`) for defaults:
   ```csharp
   var name = user?.Username ?? "Unknown";
   ```
   
3. Check collections before accessing:
   ```csharp
   if (items?.Any() ?? false) { }
   ```

4. Always handle potential nulls from methods like ToString():
   ```csharp
   string? result = someObject?.ToString();
   return string.IsNullOrEmpty(result) ? "Default" : result;
   ```

## Async/Await

1. Always use await with async methods:
   ```csharp
   // Correct
   var result = await GetDataAsync(); 
   
   // Incorrect
   var result = GetDataAsync().Result;
   ```

2. Use proper exception handling in async methods:
   ```csharp
   try
   {
       await DoOperationAsync();
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, "Operation failed");
   }
   ```

## Collection Syntax

1. Always use trailing commas in collections and arrays:
   ```csharp
   var items = new[]
   {
       "Item1",
       "Item2",
       "Item3", // Note the trailing comma
   };
   ```

2. Use collection expressions when possible:
   ```csharp
   var items = ["Item1", "Item2", "Item3", ];
   ```

## String Formatting

1. Prefer string interpolation over string.Format:
   ```csharp
   // Preferred
   var message = $"Hello, {user.Username}!";
   
   // Instead of
   var message = string.Format("Hello, {0}!", user.Username);
   ```

## Error Handling

1. Always log exceptions with appropriate context:
   ```csharp
   try
   {
       // Code that might throw
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, $"Failed to process {operationName}: {ex.Message}");
       throw; // Rethrow if needed
   }
   ```

## Pattern Matching

1. Use pattern matching for more expressive code:
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

## Line Length and Formatting

1. Maximum line length should not exceed 160 characters
2. Break method chains before the dot:
   ```csharp
   var query = collection
       .Where(x => x.IsActive)
       .OrderBy(x => x.Name)
       .Select(x => new { x.Id, x.Name });
   ``` 