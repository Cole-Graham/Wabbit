using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Trees;
using Microsoft.Extensions.DependencyInjection;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Base class for team-related permission attributes
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public abstract class TeamPermissionBaseAttribute : Attribute, IContextCheck
    {
        /// <summary>
        /// The name of the parameter that contains the team ID or name
        /// </summary>
        protected readonly string TeamParameterName;

        /// <summary>
        /// Creates a new instance of a team permission attribute
        /// </summary>
        /// <param name="teamParameterName">The name of the parameter that contains the team ID or name</param>
        public TeamPermissionBaseAttribute(string teamParameterName = "teamName")
        {
            TeamParameterName = teamParameterName;
        }

        /// <summary>
        /// Executes the permission check
        /// </summary>
        public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
        {
            try
            {
                // First make sure the user has the whitelisted role
                var permissionService = context.ServiceProvider.GetRequiredService<IPermissionService>();
                if (!await permissionService.HasWhitelistedRoleAsync(context.User.Id))
                    return "You need the Whitelisted role to use this command.";

                // If it's an admin, they can bypass team permission checks
                if (await permissionService.HasAdminPrivilegesAsync(context.User.Id))
                    return null; // Success - Admin bypass

                // Find the team parameter from arguments
                var parameter = context.Arguments.Keys.FirstOrDefault(p => p.Name == TeamParameterName);
                if (parameter == null)
                    return $"Parameter '{TeamParameterName}' not found.";

                if (!context.Arguments.TryGetValue(parameter, out var teamParameterObj) || teamParameterObj == null)
                    return $"Parameter '{TeamParameterName}' has no value.";

                string? teamParameter = teamParameterObj.ToString();
                if (string.IsNullOrEmpty(teamParameter))
                    return $"Parameter '{TeamParameterName}' cannot be empty.";

                // Get the team service
                var teamService = context.ServiceProvider.GetRequiredService<ITeamService>();

                // Get the team (could be by ID or name)
                var team = teamParameter.StartsWith("team-")
                    ? await teamService.GetTeamByIdAsync(teamParameter)
                    : await teamService.GetTeamByNameAsync(teamParameter);

                // If team not found, fail
                if (team == null)
                    return $"Team '{teamParameter}' not found.";

                // Perform the specific permission check for this attribute
                return await PerformTeamPermissionCheckAsync(context, team);
            }
            catch (Exception ex)
            {
                return $"Error checking team permissions: {ex.Message}";
            }
        }

        /// <summary>
        /// Perform the specific permission check for this attribute
        /// </summary>
        /// <param name="context">The command context</param>
        /// <param name="team">The team being accessed</param>
        /// <returns>Null if check passes, an error message otherwise</returns>
        protected abstract Task<string?> PerformTeamPermissionCheckAsync(CommandContext context, Team team);
    }
}