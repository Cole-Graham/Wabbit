using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Commands
{
    [Command("team_admin")]
    [RequirePermissions(DiscordPermission.Administrator)]
    [Description("Administrative commands for team management")]
    public class TeamManagementGroup
    {
        private readonly ILogger<TeamManagementGroup> _logger;
        private readonly ITeamService _teamService;
        private readonly ISeasonStateService _seasonStateService;
        private readonly ILeaderboardService _leaderboardService;

        public TeamManagementGroup(
            ILogger<TeamManagementGroup> logger,
            ITeamService teamService,
            ISeasonStateService seasonStateService,
            ILeaderboardService leaderboardService)
        {
            _logger = logger;
            _teamService = teamService;
            _seasonStateService = seasonStateService;
            _leaderboardService = leaderboardService;
        }

        [Command("force_create")]
        [Description("Force create a new team")]
        public async Task ForceCreateTeamAsync(
            CommandContext context,
            [Description("Team name (must be unique)")] string name,
            [Description("Discord user to be team captain")] DiscordUser owner,
            [Description("Team format")] TeamGameType gameType = TeamGameType.OneVOne)
        {
            await context.DeferResponseAsync();

            try
            {
                // Check if there's an active season
                var currentSeason = await _seasonStateService.GetCurrentSeasonAsync();
                if (currentSeason == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "❌ There is no active season. Teams can only be created during an active season."));
                    return;
                }

                // Validate team name
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3 || name.Length > 32)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "❌ Team name must be between 3 and 32 characters long."));
                    return;
                }

                // Check if name is available
                if (!await _teamService.IsTeamNameAvailableAsync(name))
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ The team name '{name}' is already taken. Please choose a different name."));
                    return;
                }

                // Create the team (using admin override)
                var team = await _teamService.CreateTeamAsync(name, gameType, owner, true);

                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"✅ Team Created: {team.TeamName}")
                    .WithDescription($"Team forcefully created by administrator.")
                    .WithColor(DiscordColor.Green)
                    .AddField("Format", gameType.ToDisplayString(), true)
                    .AddField("Rating", team.Rating.ToString(), true)
                    .AddField("ID", team.TeamId, true)
                    .AddField("Owner", owner.Mention, true)
                    .AddField("Created", DateTime.UtcNow.ToString("MMM d, yyyy"), true)
                    .WithFooter($"Created by admin {context.User.Username}");

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error force creating team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred: {ex.Message}"));
            }
        }

        [Command("delete")]
        [Description("Delete a team")]
        public async Task DeleteTeamAsync(
            CommandContext context,
            [Description("Name of the team to delete")] string teamName)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Delete the team directly
                bool deleted = await _teamService.DeleteTeamAsync(team.TeamId, context.User);
                if (deleted)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Team Deleted")
                        .WithDescription($"The team '{team.TeamName}' has been permanently deleted.")
                        .WithColor(DiscordColor.Red)
                        .AddField("Format", team.GameType.ToDisplayString(), true)
                        .AddField("Rating", team.Rating.ToString(), true)
                        .AddField("Record", $"{team.Wins}-{team.Losses}", true);

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ Failed to delete team '{team.TeamName}'. Please try again later."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while deleting the team: {ex.Message}"));
            }
        }

        [Command("add_player")]
        [Description("Add a player to a team")]
        public async Task AddPlayerToTeamAsync(
            CommandContext context,
            [Description("Name of the team")] string teamName,
            [Description("User to add to the team")] DiscordUser user,
            [Description("Role in the team (Core, Secondary, Substitute)")] PlayerRole role = PlayerRole.Core)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Try to add the player (with admin override)
                bool added = await _teamService.AddPlayerToTeamAsync(team.TeamId, user, role, context.User, true);

                if (added)
                {
                    // Get updated team info
                    team = await _teamService.GetTeamByIdAsync(team.TeamId);
                    if (team == null)
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ Team not found after adding player."));
                        return;
                    }

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Player Added to Team")
                        .WithDescription($"{user.Mention} has been added to team '{team.TeamName}' as a {role} player.")
                        .WithColor(DiscordColor.Green)
                        .AddField("Format", team.GameType.ToDisplayString(), true)
                        .AddField("Team", team.TeamName, true)
                        .AddField("Role", role.ToString(), true)
                        .WithFooter($"Added by admin {context.User.Username}");

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ Failed to add {user.Mention} to team '{teamName}'. The team may be full for this role."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding player to team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred: {ex.Message}"));
            }
        }

        [Command("remove_player")]
        [Description("Remove a player from a team")]
        public async Task RemovePlayerFromTeamAsync(
            CommandContext context,
            [Description("Name of the team")] string teamName,
            [Description("User to remove from the team")] DiscordUser user)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Try to remove the player (with admin override)
                bool removed = await _teamService.RemovePlayerFromTeamAsync(team.TeamId, user.Id, context.User, true);

                if (removed)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Player Removed from Team")
                        .WithDescription($"{user.Mention} has been removed from team '{team.TeamName}'.")
                        .WithColor(DiscordColor.Red)
                        .AddField("Team", team.TeamName, true)
                        .WithFooter($"Removed by admin {context.User.Username}");

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ Failed to remove {user.Mention} from team '{teamName}'. The player may not be a member of this team."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing player from team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred: {ex.Message}"));
            }
        }

        [Command("reset_rating")]
        [Description("Reset a team's rating")]
        public async Task ResetTeamRatingAsync(
            CommandContext context,
            [Description("Name of the team")] string teamName,
            [Description("New rating (default is 1200)")] int rating = 1200)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Store old rating for display
                int oldRating = team.Rating;

                // Update the team's rating
                team.Rating = rating;
                await _teamService.UpdateTeamAsync(team);

                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"✅ Team Rating Reset")
                    .WithDescription($"Rating for team '{team.TeamName}' has been reset.")
                    .WithColor(DiscordColor.Green)
                    .AddField("Old Rating", oldRating.ToString(), true)
                    .AddField("New Rating", rating.ToString(), true)
                    .WithFooter($"Reset by admin {context.User.Username}");

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting team rating");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred: {ex.Message}"));
            }
        }

        [Command("list")]
        [Description("List all teams by type")]
        public async Task ListTeamsAsync(
            CommandContext context,
            [Description("Team format to filter by (leave empty for all teams)")] TeamGameType? gameType = null)
        {
            await context.DeferResponseAsync();

            try
            {
                List<Team> teams;

                // Get teams based on type filter
                if (gameType.HasValue)
                {
                    teams = await _teamService.GetTeamsByTypeAsync(gameType.Value);
                }
                else
                {
                    teams = await _teamService.GetAllTeamsAsync();
                }

                if (teams.Count == 0)
                {
                    string message = gameType.HasValue
                        ? $"No teams found for {gameType.Value.ToDisplayString()} format."
                        : "No teams found.";

                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
                    return;
                }

                // Group teams by type for display
                var teamsByType = teams.GroupBy(t => t.GameType);

                // Create embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Team Listing")
                    .WithColor(DiscordColor.Blue)
                    .WithDescription($"Found {teams.Count} team(s) total.");

                foreach (var typeGroup in teamsByType)
                {
                    // Create team list for this type
                    var sb = new System.Text.StringBuilder();
                    foreach (var team in typeGroup.OrderByDescending(t => t.Rating))
                    {
                        // Get owner info
                        string ownerInfo = "";
                        if (team.CorePlayerInfo?.Count > 0)
                        {
                            var owner = team.CorePlayerInfo[0];
                            ownerInfo = $"(Owner: <@{owner.Id}>)";
                        }

                        sb.AppendLine($"**{team.TeamName}** - Rating: {team.Rating} {ownerInfo}");
                    }

                    // Add field for this type
                    embed.AddField($"{typeGroup.Key.ToDisplayString()} Teams ({typeGroup.Count()})", sb.ToString(), false);
                }

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing teams");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred: {ex.Message}"));
            }
        }
    }
}