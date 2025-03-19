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
        public ValueTask<bool> CheckAsync(CommandContext ctx)
        {
            // If no IDs are specified, no users can use the command
            if (_allowedUserIds == null || _allowedUserIds.Length == 0)
                return ValueTask.FromResult(false);

            // Check if the user's ID is in the allowed list
            return ValueTask.FromResult(_allowedUserIds.Contains(ctx.User.Id));
        }
    }
}