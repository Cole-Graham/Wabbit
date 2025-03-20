using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using Wabbit.Models.Rating;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Requires that a user is a core player of the team to execute a command.
    /// Can only be used on commands that include a teamId or teamName parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RequireTeamCorePlayerAttribute : TeamPermissionBaseAttribute
    {
        /// <summary>
        /// Creates a new instance of the RequireTeamCorePlayerAttribute
        /// </summary>
        /// <param name="teamParameterName">The name of the parameter that contains the team ID or name</param>
        public RequireTeamCorePlayerAttribute(string teamParameterName = "teamName")
            : base(teamParameterName)
        {
        }

        /// <summary>
        /// Check if the user is a core player of the team
        /// </summary>
        protected override Task<string?> PerformTeamPermissionCheckAsync(CommandContext context, Team team)
        {
            // Check if the user is a core player of the team
            bool isCorePlayer = team.CorePlayers != null &&
                               team.CorePlayers.Any(p => p.Id == context.User.Id);

            return isCorePlayer
                ? Task.FromResult<string?>(null) // Success 
                : Task.FromResult<string?>("You must be a core player of this team to use this command.");
        }
    }
}