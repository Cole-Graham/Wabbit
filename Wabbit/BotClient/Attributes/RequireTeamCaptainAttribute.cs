using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Trees;
using Microsoft.Extensions.DependencyInjection;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Requires that a user is a team captain (team creator) to execute a command.
    /// Can only be used on commands that include a teamId or teamName parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RequireTeamCaptainAttribute : Attribute, IContextCheck
    {
        private readonly string _teamParameterName;

        /// <summary>
        /// Creates a new instance of the RequireTeamCaptainAttribute
        /// </summary>
        /// <param name="teamParameterName">The name of the parameter that contains the team ID or name</param>
        public RequireTeamCaptainAttribute(string teamParameterName = "teamName")
        {
            _teamParameterName = teamParameterName;
        }

        public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
        {
            try
            {
                // First make sure the user has the whitelisted role
                var permissionService = context.ServiceProvider.GetRequiredService<IPermissionService>();
                if (!await permissionService.HasWhitelistedRoleAsync(context.User.Id))
                    return "You need the Whitelisted role to use this command.";

                // If it's an admin, they can bypass team captain check
                if (await permissionService.HasAdminPrivilegesAsync(context.User.Id))
                    return null; // Success

                // Find the team parameter from arguments
                var parameter = context.Arguments.Keys.FirstOrDefault(p => p.Name == _teamParameterName);
                if (parameter == null)
                    return "Team name parameter not found.";

                if (!context.Arguments.TryGetValue(parameter, out var teamParameterObj) || teamParameterObj == null)
                    return "Team name parameter not found.";

                string? teamParameter = teamParameterObj.ToString();
                if (string.IsNullOrEmpty(teamParameter))
                    return "Team name parameter not found or empty.";

                // Get the team service
                var teamService = context.ServiceProvider.GetRequiredService<ITeamStateService>();

                // Get the team (could be by ID or name)
                var team = teamParameter.StartsWith("team-")
                    ? await teamService.GetTeamByIdAsync(teamParameter)
                    : await teamService.GetTeamByNameAsync(teamParameter);

                // If team not found, fail
                if (team == null)
                    return "Team not found.";

                // Check if the user is the team creator
                return team.CreatorId == context.User.Id
                    ? null // Success 
                    : "You must be the team captain to use this command.";
            }
            catch (Exception ex)
            {
                return $"Error checking team captain status: {ex.Message}";
            }
        }
    }
}