# Discord Permission Handling

## Checking Permissions

1. Use Discord's built-in permission system instead of custom lists:

   ```csharp
   // Preferred approach - using Discord's permission system
   public async Task<bool> HasAdminPrivilegesAsync(ulong userId)
   {
       try
       {
           var guild = await _discordClient.GetGuildAsync(_guildId);
           var member = await guild.GetMemberAsync(userId);
           
           // Check if user has Administrator permission or is the server owner
           return member.IsOwner || member.Permissions.HasFlag(DiscordPermission.Administrator);
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, $"Failed to check admin privileges for user {userId}");
           return false;
       }
   }
   ```

2. For more granular permission control, check specific permissions:

   ```csharp
   // Check for specific Discord permissions
   bool canManageChannels = member.Permissions.HasFlag(DiscordPermission.ManageChannels);
   bool canManageMessages = member.Permissions.HasFlag(DiscordPermission.ManageMessages);
   ```

3. For role-based permissions:

   ```csharp
   // Check if user has a specific role
   bool hasModeratorRole = member.Roles.Any(r => 
       r.Name.Equals("Moderator", StringComparison.OrdinalIgnoreCase) || 
       r.Id == moderatorRoleId);
   ```

## Custom Permission Attributes

1. Create custom permission attributes by implementing `IContextCheck`:

   ```csharp
   [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
   public class RequireWhitelistedRoleAttribute : Attribute, IContextCheck
   {
       public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
       {
           try
           {
               // Get the permission service from DI
               var permissionService = context.ServiceProvider.GetRequiredService<IPermissionService>();

               // Check if the user has the required role
               bool hasRole = await permissionService.HasWhitelistedRoleAsync(context.User.Id);

               // Return null for success, error message for failure
               return hasRole ? null : "You need the Whitelisted role to use this command.";
           }
           catch (Exception ex)
           {
               return $"Error checking role permissions: {ex.Message}";
           }
       }
   }
   ```

2. For attributes that need command arguments, access them properly:

   ```csharp
   [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
   public class RequireTeamCaptainAttribute : Attribute, IContextCheck
   {
       private readonly string _teamParameterName;

       public RequireTeamCaptainAttribute(string teamParameterName = "teamName")
       {
           _teamParameterName = teamParameterName;
       }

       public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
       {
           try
           {
               // Get the parameter by name from Arguments dictionary
               var parameter = context.Arguments.Keys.FirstOrDefault(p => p.Name == _teamParameterName);
               if (parameter == null)
                   return "Team name parameter not found.";

               if (!context.Arguments.TryGetValue(parameter, out var teamParameterObj) || teamParameterObj == null)
                   return "Team name parameter not found.";

               string? teamParameter = teamParameterObj.ToString();
               
               // Continue with permission check using the parameter value
               // ...
           }
           catch (Exception ex)
           {
               return $"Error checking permissions: {ex.Message}";
           }
       }
   }
   ```

3. Apply permission attributes to commands:

   ```csharp
   [Command("team")]
   [RequireWhitelistedRole]
   public class TeamGroup
   {
       [Command("create")]
       [RequireWhitelistedRole]
       public async Task CreateTeamAsync(CommandContext context, string name)
       {
           // ...
       }

       [Command("rename")]
       [RequireTeamCaptain]
       public async Task RenameTeamAsync(CommandContext context, string teamName, string newName)
       {
           // ...
       }
   }
   ```

## Command Permissions

1. Use Discord's built-in permission system for slash commands:

   ```csharp
   [SlashCommand("admin", "Admin-only command")]
   [SlashCommandPermissions(DiscordPermission.Administrator)]
   public async Task AdminCommand(CommandContext context)
   {
       // Only users with Administrator permission can run this command
   }
   ```

2. For custom permission checks, use conditional logic:

   ```csharp
   [SlashCommand("manage_season", "Manage the current season")]
   public async Task ManageSeasonCommand(CommandContext context)
   {
       // Defer response so we have time to check permissions
       await context.DeferResponseAsync(true); // Ephemeral by default while checking
       
       // Check if user has required permissions
       bool hasPermission = await _seasonStateService.HasSeasonAdminPrivilegesAsync(context.User.Id);
       
       if (!hasPermission)
       {
           await context.EditResponseAsync(new DiscordWebhookBuilder()
               .WithContent("You don't have permission to manage seasons.")
               .AsEphemeral()); // Keep error message private
           return;
       }
       
       // Continue with command execution
   }
   ```

## Error Messages

1. Make permission error messages ephemeral (only visible to the user):

   ```csharp
   // Private error message for permission issues
   await context.CreateResponseAsync(
       DiscordInteractionResponseType.ChannelMessageWithSource,
       new DiscordInteractionResponseBuilder()
           .WithContent("You don't have permission to use this command.")
           .AsEphemeral()
   );
   ```

2. Be specific about what permissions are needed:

   ```csharp
   await context.CreateResponseAsync(
       DiscordInteractionResponseType.ChannelMessageWithSource,
       new DiscordInteractionResponseBuilder()
           .WithContent("This command requires the Manage Server permission or the Tournament Manager role.")
           .AsEphemeral()
   );
   ```

## Custom Permission Systems

If you need a custom permission system beyond Discord's built-in roles and permissions:

1. Store permissions in a database or configuration file:

   ```csharp
   // Example of how to load permissions from a database or config
   private async Task LoadCustomPermissionsAsync()
   {
       var permissions = await _databaseService.GetPermissionsAsync();
       _permissionCache = permissions.ToDictionary(
           p => p.UserId,
           p => p.PermissionLevel
       );
   }
   ```

2. Combine custom permissions with Discord's built-in system:

   ```csharp
   public async Task<bool> HasPermissionAsync(ulong userId, string permissionType)
   {
       // First check Discord's built-in permissions
       var guild = await _discordClient.GetGuildAsync(_guildId);
       var member = await guild.GetMemberAsync(userId);
       
       // Discord admins and owners always have all permissions
       if (member.IsOwner || member.Permissions.HasFlag(DiscordPermission.Administrator))
           return true;
           
       // Then check custom permissions
       if (_permissionCache.TryGetValue(userId, out var permLevel))
       {
           return permLevel >= GetRequiredPermissionLevel(permissionType);
       }
       
       return false;
   }
   ```

## Best Practices

1. Always use proper error handling when checking permissions
2. Make permission error messages private (ephemeral)
3. Provide clear messaging about required permissions
4. Use Discord's built-in permission system when possible
5. Cache permission results when appropriate to reduce API calls
6. Document permission requirements in command descriptions 