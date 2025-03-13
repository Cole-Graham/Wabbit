using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;

namespace Wabbit.Services
{
    /// <summary>
    /// Utility methods for Discord-related operations
    /// </summary>
    public static class DiscordUtilities
    {
        /// <summary>
        /// Creates a thread with standardized error handling and consistent parameters
        /// </summary>
        /// <param name="channel">The channel to create the thread in</param>
        /// <param name="threadName">The name of the thread</param>
        /// <param name="logger">Logger for error reporting</param>
        /// <param name="threadType">The type of thread to create, defaults to private thread</param>
        /// <param name="archiveDuration">How long the thread should stay active before auto-archiving</param>
        /// <returns>The created thread channel, or null if creation failed</returns>
        public static async Task<DiscordThreadChannel?> CreateThreadAsync(
            DiscordChannel channel,
            string threadName,
            ILogger logger,
            DiscordChannelType threadType = DiscordChannelType.PrivateThread,
            DiscordAutoArchiveDuration archiveDuration = DiscordAutoArchiveDuration.Day)
        {
            try
            {
                // Use fully qualified names to avoid any namespace conflicts
                return await channel.CreateThreadAsync(
                    threadName,
                    DSharpPlus.Entities.DiscordAutoArchiveDuration.Day,
                    threadType);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error creating thread '{threadName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Adds members to a thread with error handling
        /// </summary>
        /// <param name="thread">The thread to add members to</param>
        /// <param name="members">The members to add</param>
        /// <param name="logger">Logger for error reporting</param>
        /// <returns>True if all members were added successfully, false otherwise</returns>
        public static async Task<bool> AddMembersToThreadAsync(
            DiscordThreadChannel thread,
            DiscordMember[] members,
            ILogger logger)
        {
            try
            {
                foreach (var member in members)
                {
                    if (member is not null)
                    {
                        await thread.AddThreadMemberAsync(member);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error adding members to thread '{thread.Name}': {ex.Message}");
                return false;
            }
        }
    }
}