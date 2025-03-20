using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;

namespace Wabbit.BotClient.Commands.Attributes
{
    /// <summary>
    /// Attribute that restricts command access to specific users by Discord ID
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RequireUserAttribute : Attribute, IContextCheck
    {
        private readonly ulong[] _allowedUserIds;

        /// <summary>
        /// Creates a new RequireUserAttribute with the specified list of allowed user IDs
        /// </summary>
        /// <param name="allowedUserIds">Array of Discord user IDs that are allowed to use the command</param>
        public RequireUserAttribute(params ulong[] allowedUserIds)
        {
            _allowedUserIds = allowedUserIds;
        }

        /// <summary>
        /// Checks if the user executing the command is in the allowed list
        /// </summary>
        public ValueTask<string?> ExecuteCheckAsync(CommandContext context)
        {
            try
            {
                // If no IDs are specified, no users can use the command
                if (_allowedUserIds == null || _allowedUserIds.Length == 0)
                    return ValueTask.FromResult<string?>("This command is not available to any users.");

                // Check if the user's ID is in the allowed list
                if (_allowedUserIds.Contains(context.User.Id))
                    return ValueTask.FromResult<string?>(null); // Success

                return ValueTask.FromResult<string?>("You are not authorized to use this command.");
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult<string?>($"Error checking user permissions: {ex.Message}");
            }
        }
    }
}