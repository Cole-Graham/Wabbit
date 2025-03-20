using System;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using Wabbit.Models.Rating;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Requires that a user is a member of the team (in any role) to execute a command.
    /// Can only be used on commands that include a teamId or teamName parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RequireTeamMemberAttribute : TeamPermissionBaseAttribute
    {
        /// <summary>
        /// Creates a new instance of the RequireTeamMemberAttribute
        /// </summary>
        /// <param name="teamParameterName">The name of the parameter that contains the team ID or name</param>
        public RequireTeamMemberAttribute(string teamParameterName = "teamName")
            : base(teamParameterName)
        {
        }

        /// <summary>
        /// Check if the user is a member of the team
        /// </summary>
        protected override Task<string?> PerformTeamPermissionCheckAsync(CommandContext context, Team team)
        {
            // Check if the user is a member of the team in any role
            return team.HasPlayer(context.User.Id)
                ? Task.FromResult<string?>(null) // Success 
                : Task.FromResult<string?>("You must be a member of this team to use this command.");
        }
    }
}