using System;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using Microsoft.Extensions.DependencyInjection;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Attributes
{
    /// <summary>
    /// Requires that a user has the whitelisted role to execute a command
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RequireWhitelistedRoleAttribute : Attribute, IContextCheck
    {
        public async ValueTask<string?> ExecuteCheckAsync(CommandContext context)
        {
            try
            {
                // Get the permission service from DI
                var permissionService = context.ServiceProvider.GetRequiredService<IPermissionService>();

                // Check if the user has the whitelisted role
                bool hasRole = await permissionService.HasWhitelistedRoleAsync(context.User.Id);

                // Return null if successful, error message otherwise
                return hasRole ? null : "You need the Whitelisted role to use this command.";
            }
            catch (Exception ex)
            {
                return $"Error checking role permissions: {ex.Message}";
            }
        }
    }
}