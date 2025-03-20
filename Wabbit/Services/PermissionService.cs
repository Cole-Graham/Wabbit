using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.BotClient.Config;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for checking user permissions throughout the bot
    /// </summary>
    public class PermissionService : IPermissionService
    {
        private readonly ILogger<PermissionService> _logger;
        private readonly DiscordClient _discordClient;
        private readonly ulong _guildId;

        /// <summary>
        /// Initializes a new instance of the PermissionService class
        /// </summary>
        public PermissionService(
            ILogger<PermissionService> logger,
            DiscordClient discordClient)
        {
            _logger = logger;
            _discordClient = discordClient;

            // Get the guild ID from config
            _guildId = ConfigManager.Config?.Servers?.FirstOrDefault()?.ServerId ?? 0;
            if (_guildId == 0)
            {
                _logger.LogWarning("No guild ID configured for permission service. Permission checks will not work correctly.");
            }
        }

        /// <inheritdoc/>
        public async Task<bool> HasAdminPrivilegesAsync(ulong userId)
        {
            try
            {
                // Check Discord's built-in permissions
                if (_guildId == 0)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                    return false;
                }

                var guild = await _discordClient.GetGuildAsync(_guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check Discord permissions for user {userId} - member not found");
                    return false;
                }

                // Check if user is the server owner or has Administrator permission
                return member.IsOwner ||
                       member.Permissions.HasFlag(DiscordPermission.Administrator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking admin privileges for user {userId}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> HasModeratorPrivilegesAsync(ulong userId)
        {
            try
            {
                // First check if the user has admin privileges
                if (await HasAdminPrivilegesAsync(userId))
                    return true;

                // Otherwise check for moderator-specific permissions
                if (_guildId == 0)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                    return false;
                }

                var guild = await _discordClient.GetGuildAsync(_guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check Discord permissions for user {userId} - member not found");
                    return false;
                }

                // Check for moderator-specific permissions
                return member.Permissions.HasFlag(DiscordPermission.ManageGuild) ||
                       member.Permissions.HasFlag(DiscordPermission.ModerateMembers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking moderator privileges for user {userId}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> HasWhitelistedRoleAsync(ulong userId)
        {
            try
            {
                // First check if the user has admin privileges (admins can bypass role requirements)
                if (await HasAdminPrivilegesAsync(userId))
                    return true;

                if (_guildId == 0)
                {
                    _logger.LogWarning($"Cannot check roles for user {userId} - no guild ID configured");
                    return false;
                }

                // Get the server config to find the whitelisted role ID
                var serverConfig = ConfigManager.Config?.Servers?.FirstOrDefault(s => s.ServerId == _guildId);
                if (serverConfig?.WhitelistedRoleId == null)
                {
                    _logger.LogWarning($"Whitelisted role ID not configured for guild {_guildId}");
                    // If not configured, default to allowing all users for now
                    // This maintains backwards compatibility until an admin configures the role
                    return true;
                }

                var whitelistedRoleId = serverConfig.WhitelistedRoleId.Value;

                var guild = await _discordClient.GetGuildAsync(_guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check roles for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check roles for user {userId} - member not found");
                    return false;
                }

                // Check if user has the whitelisted role
                return member.Roles.Any(role => role.Id == whitelistedRoleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking whitelisted role for user {userId}");
                return false;
            }
        }
    }
}