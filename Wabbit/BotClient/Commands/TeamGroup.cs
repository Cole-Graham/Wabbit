using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Attributes;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Commands
{
    [Command("Team")]
    [RequireWhitelistedRole]
    public class TeamGroup
    {
        private readonly ILogger<TeamGroup> _logger;
        private readonly ITeamStateService _teamStateService;
        private readonly ITeamRepositoryService _teamRepositoryService;
        private readonly ISeasonStateService _seasonStateService;
        private readonly ILeaderboardService _leaderboardService;

        public TeamGroup(
            ILogger<TeamGroup> logger,
            ITeamStateService teamStateService,
            ITeamRepositoryService teamRepositoryService,
            ISeasonStateService seasonStateService,
            ILeaderboardService leaderboardService)
        {
            _logger = logger;
            _teamStateService = teamStateService;
            _teamRepositoryService = teamRepositoryService;
            _seasonStateService = seasonStateService;
            _leaderboardService = leaderboardService;
        }

        [Command("create")]
        [Description("Create a new team")]
        public async Task CreateTeamAsync(
            CommandContext context,
            [Description("Team name (must be unique)")] string name,
            [Description("Type of team (0=1v1, 1=2v2, 2=3v3, 3=4v4)")] int typeInt = 0)
        {
            await context.DeferResponseAsync();

            try
            {
                // Convert type parameter to enum
                var teamGameType = typeInt switch
                {
                    1 => Wabbit.Models.TeamGameType.TwoVTwo,
                    2 => Wabbit.Models.TeamGameType.ThreeVThree,
                    3 => Wabbit.Models.TeamGameType.FourVFour,
                    _ => Wabbit.Models.TeamGameType.OneVOne
                };

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
                if (!await _teamStateService.IsTeamNameAvailableAsync(name))
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ The team name '{name}' is already taken. Please choose a different name."));
                    return;
                }

                // Check if user can create a team of this type
                if (!await _teamStateService.CanCreateTeamAsync(context.User.Id, teamGameType))
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ You cannot create a {teamGameType} team. You may already be on the maximum number of teams for this type."));
                    return;
                }

                // Create the team
                var team = await _teamStateService.CreateTeamAsync(name, teamGameType, context.User);

                // Create embed response
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"✅ Team Created: {team.TeamName}")
                    .WithDescription($"You are now the owner of this team.")
                    .WithColor(DiscordColor.Green)
                    .AddField("Type", GetGameTypeDisplayName(team.GameType), true)
                    .AddField("Rating", team.Rating.ToString(), true)
                    .AddField("Team ID", team.TeamId, true)
                    .AddField("Created", DateTime.UtcNow.ToString("MMM d, yyyy"), true)
                    .WithFooter($"Team created by {context.User.Username}");

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while creating the team: {ex.Message}"));
            }
        }

        [Command("info")]
        [Description("View information about a team")]
        public async Task TeamInfoAsync(
            CommandContext context,
            [Description("Name of the team to view")] string teamName)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Create the embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Team: {team.TeamName}")
                    .WithColor(DiscordColor.Blue)
                    .AddField("Type", GetGameTypeDisplayName(team.GameType), true)
                    .AddField("Rating", team.Rating.ToString(), true)
                    .AddField("Created", team.CreatedAt.ToString("MMM d, yyyy"), true)
                    .AddField("Record", $"{team.Wins}-{team.Losses} ({GetWinRate(team.Wins, team.Losses)}%)", true);

                // Add core players
                string corePlayers = string.Join("\n", team.CorePlayerInfo?.Select(p => $"<@{p.Id}>") ?? Array.Empty<string>());
                if (string.IsNullOrEmpty(corePlayers)) corePlayers = "None";
                embed.AddField("Core Players", corePlayers, false);

                // Add secondary players if applicable
                if (team.GameType != Wabbit.Models.TeamGameType.OneVOne)
                {
                    string secondaryPlayers = string.Join("\n", team.SecondaryPlayerInfo?.Select(p => $"<@{p.Id}>") ?? Array.Empty<string>());
                    if (string.IsNullOrEmpty(secondaryPlayers)) secondaryPlayers = "None";
                    embed.AddField("Secondary Players", secondaryPlayers, false);
                }

                // Add substitute players if applicable
                if (team.GameType == Wabbit.Models.TeamGameType.ThreeVThree || team.GameType == Wabbit.Models.TeamGameType.FourVFour)
                {
                    string substitutePlayers = string.Join("\n", team.SubstitutePlayerInfo?.Select(p => $"<@{p.Id}>") ?? Array.Empty<string>());
                    if (string.IsNullOrEmpty(substitutePlayers)) substitutePlayers = "None";
                    embed.AddField("Substitute Players", substitutePlayers, false);
                }

                // Add cooldown information
                string cooldowns = "";
                if (team.LastNameChangeDate.HasValue)
                {
                    cooldowns += $"Name Change: {GetCooldownText(team.LastNameChangeDate.Value, TimeSpan.FromDays(7))}\n";
                }
                if (team.LastSecondaryChangeDate.HasValue)
                {
                    cooldowns += $"Secondary Player Change: {GetCooldownText(team.LastSecondaryChangeDate.Value, TimeSpan.FromDays(30))}\n";
                }
                if (team.LastSubstituteChangeDate.HasValue)
                {
                    cooldowns += $"Substitute Player Change: {GetCooldownText(team.LastSubstituteChangeDate.Value, TimeSpan.FromDays(7))}\n";
                }
                if (!string.IsNullOrEmpty(cooldowns))
                {
                    embed.AddField("Cooldowns", cooldowns, false);
                }

                // Get ranking if available
                var ranking = await _leaderboardService.GetTeamRankingAsync(team.TeamId, team.GameType);
                if (ranking != null)
                {
                    embed.AddField("Current Rank", $"#{ranking.Rank}", true);
                }

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving team info");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while retrieving team information: {ex.Message}"));
            }
        }

        [Command("join")]
        [Description("Join an existing team")]
        public async Task JoinTeamAsync(
            CommandContext context,
            [Description("Name of the team to join")] string teamName,
            [Description("Role in the team (0=Core, 1=Secondary, 2=Substitute)")] int roleInt = 0)
        {
            await context.DeferResponseAsync();

            try
            {
                // Convert role parameter to enum
                var role = roleInt switch
                {
                    1 => PlayerRole.Secondary,
                    2 => PlayerRole.Substitute,
                    _ => PlayerRole.Core
                };

                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Try to join the team with the specified role
                bool joined = await _teamStateService.AddPlayerToTeamAsync(team.TeamId, context.User, role, context.User);

                if (joined)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Joined Team: {team.TeamName}")
                        .WithDescription($"You have successfully joined as a {role} player.")
                        .WithColor(DiscordColor.Green)
                        .AddField("Type", GetGameTypeDisplayName(team.GameType), true)
                        .AddField("Rating", team.Rating.ToString(), true);

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ Unable to join team '{teamName}'. The team may be full for your selected role, or you might not have permission to join."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while joining the team: {ex.Message}"));
            }
        }

        [Command("leave")]
        [Description("Leave a team you're a member of")]
        [RequireTeamMember]
        public async Task LeaveTeamAsync(
            CommandContext context,
            [Description("Name of the team to leave")] string teamName)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Try to leave the team
                bool left = await _teamStateService.RemovePlayerFromTeamAsync(team.TeamId, context.User.Id, context.User);

                if (left)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"✅ You have successfully left team '{teamName}'."));
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ Unable to leave team '{teamName}'. You may be the owner or not a member of this team."));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while leaving the team: {ex.Message}"));
            }
        }

        [Command("myteams")]
        [Description("List all teams you are a member of")]
        public async Task MyTeamsAsync(CommandContext context)
        {
            await context.DeferResponseAsync();

            try
            {
                // Get all teams the user is in
                var teams = await _teamStateService.GetPlayerTeamsAsync(context.User.Id);

                if (teams.Count == 0)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "You are not a member of any teams."));
                    return;
                }

                // Create embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"{context.User.Username}'s Teams")
                    .WithColor(DiscordColor.Blue)
                    .WithDescription($"You are a member of {teams.Count} team(s).");

                // Group teams by game type
                var teamsByType = teams.GroupBy(t => t.GameType);

                foreach (var typeGroup in teamsByType)
                {
                    string teamsList = "";
                    foreach (var team in typeGroup)
                    {
                        // Determine role in this team
                        string role = "Unknown";
                        if (team.CorePlayerInfo?.Any(p => p.Id == context.User.Id) == true)
                            role = "Core";
                        else if (team.SecondaryPlayerInfo?.Any(p => p.Id == context.User.Id) == true)
                            role = "Secondary";
                        else if (team.SubstitutePlayerInfo?.Any(p => p.Id == context.User.Id) == true)
                            role = "Substitute";

                        teamsList += $"**{team.TeamName}** - Rating: {team.Rating} - Role: {role}\n";
                    }

                    embed.AddField($"{GetGameTypeDisplayName(typeGroup.Key)} Teams", teamsList, false);
                }

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing player teams");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while retrieving your teams: {ex.Message}"));
            }
        }

        [Command("list")]
        [Description("List all registered teams")]
        public async Task ListTeamsAsync(
            CommandContext context,
            [Description("Type of team (0=1v1, 1=2v2, 2=3v3, 3=4v4)")] int typeInt = 0,
            [Description("Number of teams to display")] int count = 10)
        {
            await context.DeferResponseAsync();

            try
            {
                // Convert type parameter to enum
                var teamGameType = typeInt switch
                {
                    1 => Wabbit.Models.TeamGameType.TwoVTwo,
                    2 => Wabbit.Models.TeamGameType.ThreeVThree,
                    3 => Wabbit.Models.TeamGameType.FourVFour,
                    _ => Wabbit.Models.TeamGameType.OneVOne
                };

                // Get top teams by rating for the specified type
                var teams = await _teamStateService.GetTopTeamsByRatingAsync(teamGameType, count);

                if (teams.Count == 0)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"No {GetGameTypeDisplayName(teamGameType)} teams registered."));
                    return;
                }

                // Create embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Top {GetGameTypeDisplayName(teamGameType)} Teams")
                    .WithColor(DiscordColor.Gold)
                    .WithDescription($"Showing top {teams.Count} teams by rating.");

                // Add teams to embed
                for (int i = 0; i < teams.Count; i++)
                {
                    var team = teams[i];
                    embed.AddField(
                        $"#{i + 1}: {team.TeamName}",
                        $"Rating: {team.Rating}\n" +
                        $"Record: {team.Wins}-{team.Losses} ({GetWinRate(team.Wins, team.Losses)}%)\n" +
                        $"Owner: <@{team.CreatorId}>\n" +
                        $"Players: {team.CorePlayerInfo?.Count ?? 0} core, " +
                        $"{team.SecondaryPlayerInfo?.Count ?? 0} secondary, " +
                        $"{team.SubstitutePlayerInfo?.Count ?? 0} substitute",
                        false
                    );
                }

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing teams");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while listing teams: {ex.Message}"));
            }
        }

        [Command("rename")]
        [Description("Change your team's name")]
        [RequireTeamCorePlayer]
        public async Task RenameTeamAsync(
            CommandContext context,
            [Description("Current name of your team")] string currentName,
            [Description("New name for your team")] string newName)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(currentName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{currentName}'."));
                    return;
                }

                // Check if the user is a team owner/admin or has admin privileges
                bool canModify = await _teamStateService.CanModifyTeamAsync(team.TeamId, context.User.Id);
                bool isAdmin = await _teamStateService.HasTeamAdminPrivilegesAsync(context.User.Id);

                if (!canModify && !isAdmin)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ You don't have permission to rename team '{currentName}'. Only team owners, core players, or administrators can do this."));
                    return;
                }

                // Check if the name is valid
                if (string.IsNullOrWhiteSpace(newName) || newName.Length < 3 || newName.Length > 32)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "❌ Team name must be between 3 and 32 characters long."));
                    return;
                }

                // Check if the new name is available
                if (!await _teamStateService.IsTeamNameAvailableAsync(newName))
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ The team name '{newName}' is already taken. Please choose a different name."));
                    return;
                }

                // Try to rename the team
                bool renamed = await _teamStateService.UpdateTeamNameAsync(team.TeamId, newName, context.User);

                if (renamed)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Team Renamed")
                        .WithDescription($"Team '{currentName}' has been renamed to '{newName}'.")
                        .WithColor(DiscordColor.Green);

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    // Get cooldown information
                    int nameCooldown = await _teamStateService.GetTeamChangeCooldownAsync(team.TeamId, TeamChangeType.NameChange);
                    if (nameCooldown > 0 && !isAdmin)
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ This team name was changed recently. You can change it again in {nameCooldown} minutes."));
                    }
                    else
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ Unable to rename team '{currentName}'. This could be due to a cooldown period or permission issue."));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming team");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while renaming the team: {ex.Message}"));
            }
        }

        [Command("change_secondary")]
        [Description("Change a secondary player in your team")]
        [RequireTeamCorePlayer]
        public async Task ChangeSecondaryPlayerAsync(
            CommandContext context,
            [Description("Name of your team")] string teamName,
            [Description("New secondary player")] DiscordUser player)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Check if the user is a team owner/admin or has admin privileges
                bool canModify = await _teamStateService.CanModifyTeamAsync(team.TeamId, context.User.Id);
                bool isAdmin = await _teamStateService.HasTeamAdminPrivilegesAsync(context.User.Id);

                if (!canModify && !isAdmin)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ You don't have permission to modify team '{teamName}'. Only team owners, core players, or administrators can do this."));
                    return;
                }

                // Check if the team is the right type
                if (team.GameType == Wabbit.Models.TeamGameType.OneVOne)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "❌ 1v1 teams don't have secondary players."));
                    return;
                }

                // Try to add the player
                bool added = await _teamStateService.AddPlayerToTeamAsync(team.TeamId, player, PlayerRole.Secondary, context.User);

                if (added)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Secondary Player Added")
                        .WithDescription($"{player.Mention} has been added as a secondary player to team '{teamName}'.")
                        .WithColor(DiscordColor.Green);

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    // Get cooldown information
                    int secondaryCooldown = await _teamStateService.GetTeamChangeCooldownAsync(team.TeamId, TeamChangeType.SecondaryPlayerChange);
                    if (secondaryCooldown > 0 && !isAdmin)
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ This team's secondary players were changed recently. You can change them again in {secondaryCooldown} minutes."));
                    }
                    else
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ Unable to add {player.Username} to team '{teamName}'. The secondary player slots may be full or the player may already be on the team."));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing secondary player");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while changing the secondary player: {ex.Message}"));
            }
        }

        [Command("change_substitute")]
        [Description("Change a substitute player in your team")]
        [RequireTeamCorePlayer]
        public async Task ChangeSubstitutePlayerAsync(
            CommandContext context,
            [Description("Name of your team")] string teamName,
            [Description("New substitute player")] DiscordUser player)
        {
            await context.DeferResponseAsync();

            try
            {
                // Find the team
                var team = await _teamStateService.GetTeamByNameAsync(teamName);
                if (team == null)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ No team found with the name '{teamName}'."));
                    return;
                }

                // Check if the user is a team owner/admin or has admin privileges
                bool canModify = await _teamStateService.CanModifyTeamAsync(team.TeamId, context.User.Id);
                bool isAdmin = await _teamStateService.HasTeamAdminPrivilegesAsync(context.User.Id);

                if (!canModify && !isAdmin)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"❌ You don't have permission to modify team '{teamName}'. Only team owners, core players, or administrators can do this."));
                    return;
                }

                // Check if the team is the right type
                if (team.GameType == Wabbit.Models.TeamGameType.OneVOne || team.GameType == Wabbit.Models.TeamGameType.TwoVTwo)
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "❌ 1v1 and 2v2 teams don't have substitute players."));
                    return;
                }

                // Try to add the player
                bool added = await _teamStateService.AddPlayerToTeamAsync(team.TeamId, player, PlayerRole.Substitute, context.User);

                if (added)
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"✅ Substitute Player Added")
                        .WithDescription($"{player.Mention} has been added as a substitute player to team '{teamName}'.")
                        .WithColor(DiscordColor.Green);

                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    // Get cooldown information
                    int substituteCooldown = await _teamStateService.GetTeamChangeCooldownAsync(team.TeamId, TeamChangeType.SubstitutePlayerChange);
                    if (substituteCooldown > 0 && !isAdmin)
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ This team's substitute players were changed recently. You can change them again in {substituteCooldown} minutes."));
                    }
                    else
                    {
                        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                            $"❌ Unable to add {player.Username} to team '{teamName}'. The substitute player slots may be full or the player may already be on the team."));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing substitute player");
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"❌ An error occurred while changing the substitute player: {ex.Message}"));
            }
        }

        #region Helper Methods

        private string GetGameTypeDisplayName(Wabbit.Models.TeamGameType gameType)
        {
            // Convert TeamGameType to GameType before calling the helper
            return GameTypeHelpers.GetDisplayName((Wabbit.Models.GameType)(int)gameType);
        }

        private int GetWinRate(int wins, int losses)
        {
            if (wins + losses == 0) return 0;
            return (int)Math.Round((double)wins / (wins + losses) * 100);
        }

        private string GetCooldownText(DateTimeOffset lastChangeDate, TimeSpan cooldownPeriod)
        {
            var now = DateTimeOffset.UtcNow;
            var cooldownEnds = lastChangeDate.Add(cooldownPeriod);

            if (now >= cooldownEnds)
                return "Available";

            var timeLeft = cooldownEnds - now;
            if (timeLeft.TotalDays >= 1)
                return $"{(int)timeLeft.TotalDays} days left";
            else if (timeLeft.TotalHours >= 1)
                return $"{(int)timeLeft.TotalHours} hours left";
            else
                return $"{(int)timeLeft.TotalMinutes} minutes left";
        }

        #endregion
    }
}