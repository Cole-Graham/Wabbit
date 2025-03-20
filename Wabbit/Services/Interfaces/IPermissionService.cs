using System.Threading.Tasks;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for checking user permissions throughout the bot
    /// </summary>
    public interface IPermissionService
    {
        /// <summary>
        /// Check if a user has admin privileges (Administrator permission or server owner)
        /// </summary>
        /// <param name="userId">The Discord user ID to check</param>
        /// <returns>True if the user has admin privileges</returns>
        Task<bool> HasAdminPrivilegesAsync(ulong userId);

        /// <summary>
        /// Check if a user has moderator privileges (ManageGuild, ModerateMembers, or admin)
        /// </summary>
        /// <param name="userId">The Discord user ID to check</param>
        /// <returns>True if the user has moderator privileges</returns>
        Task<bool> HasModeratorPrivilegesAsync(ulong userId);

        /// <summary>
        /// Check if a user has the Whitelisted role
        /// </summary>
        /// <param name="userId">The Discord user ID to check</param>
        /// <returns>True if the user has the Whitelisted role or is an admin</returns>
        Task<bool> HasWhitelistedRoleAsync(ulong userId);
    }
}