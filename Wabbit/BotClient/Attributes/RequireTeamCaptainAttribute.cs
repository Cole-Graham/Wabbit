using System;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using Wabbit.Models.Rating;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Requires that a user is a team captain (team creator) to execute a command.
    /// Can only be used on commands that include a teamId or teamName parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RequireTeamCaptainAttribute : TeamPermissionBaseAttribute
    {
        /// <summary>
        /// Creates a new instance of the RequireTeamCaptainAttribute
        /// </summary>
        /// <param name="teamParameterName">The name of the parameter that contains the team ID or name</param>
        public RequireTeamCaptainAttribute(string teamParameterName = "teamName")
            : base(teamParameterName)
        {
        }

        /// <summary>
        /// Check if the user is the team captain
        /// </summary>
        protected override Task<string?> PerformTeamPermissionCheckAsync(CommandContext context, Team team)
        {
            // Check if the user is the team creator
            return team.CreatorId == context.User.Id
                ? Task.FromResult<string?>(null) // Success 
                : Task.FromResult<string?>("You must be the team captain to use this command.");
        }
    }
}