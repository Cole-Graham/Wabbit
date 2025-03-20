using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.BotClient.Config;
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing team state at runtime
    /// </summary>
    public class TeamStateService : ITeamStateService
    {
        private readonly ILogger<TeamStateService> _logger;
        private readonly ITeamRepositoryService _teamRepository;
        private readonly IPlayerRatingRepositoryService _playerRatingRepository;
        private readonly Dictionary<string, Team> _activeTeams;
        private readonly Dictionary<ulong, HashSet<string>> _playerTeamMemberships;
        private bool _isInitialized;
        private readonly DiscordClient _discordClient;
        private readonly ulong _guildId;

        /// <summary>
        /// Initializes a new instance of the TeamStateService class
        /// </summary>
        public TeamStateService(
            ILogger<TeamStateService> logger,
            ITeamRepositoryService teamRepository,
            IPlayerRatingRepositoryService playerRatingRepository,
            DiscordClient discordClient)
        {
            _logger = logger;
            _teamRepository = teamRepository;
            _playerRatingRepository = playerRatingRepository;
            _activeTeams = new Dictionary<string, Team>();
            _playerTeamMemberships = new Dictionary<ulong, HashSet<string>>();
            _isInitialized = false;
            _discordClient = discordClient;

            // Get the guild ID from config
            // Use the first server ID from the config if available
            var guildId = ConfigManager.Config?.Servers?.FirstOrDefault()?.ServerId ?? 0;
            if (guildId == 0)
            {
                _logger.LogWarning("No guild ID configured for team service. Server owner checks will not work.");
            }
            _guildId = guildId;
        }

        /// <summary>
        /// Initialize team state
        /// </summary>
        public async Task Initialize()
        {
            if (_isInitialized)
                return;

            try
            {
                _logger.LogInformation("Initializing team state...");

                // Load teams from repository
                var teams = await _teamRepository.GetAllTeamsAsync();
                foreach (var team in teams)
                {
                    _activeTeams[team.TeamId] = team;

                    // Update player-team membership lookup
                    UpdatePlayerTeamMemberships(team);
                }

                _isInitialized = true;
                _logger.LogInformation($"Team state initialized with {_activeTeams.Count} teams.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing team state");
                throw;
            }
        }

        /// <summary>
        /// Create a new team
        /// </summary>
        public async Task<Team> CreateTeamAsync(string name, Wabbit.Models.TeamGameType type, DiscordUser creator)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Team name cannot be empty", nameof(name));

            // Check if the user can create this type of team
            bool canCreate = await CanCreateTeamAsync(creator.Id, type);
            if (!canCreate)
                throw new InvalidOperationException($"User {creator.Username} cannot create another team of type {type}");

            // Check if the name is available
            bool nameAvailable = await IsTeamNameAvailableAsync(name);
            if (!nameAvailable)
                throw new InvalidOperationException($"Team name '{name}' is already taken");

            // Create the team
            var team = new Team(name, type)
            {
                Creator = creator,
                CreatorId = creator.Id,
                CreatorUsername = creator.Username
            };

            // Add the creator as a core player
            team.AddCorePlayer(creator);

            // Save to repository
            await _teamRepository.AddTeamAsync(team);

            // Update local state
            _activeTeams[team.TeamId] = team;
            UpdatePlayerTeamMemberships(team);

            _logger.LogInformation($"Team '{name}' ({type}) created by {creator.Username} (ID: {creator.Id})");
            return team;
        }

        /// <summary>
        /// Check if a user can create a new team
        /// </summary>
        public async Task<bool> CanCreateTeamAsync(ulong userId, Wabbit.Models.TeamGameType type)
        {
            if (!_isInitialized)
                await Initialize();

            // Admins can always create teams
            if (await HasTeamAdminPrivilegesAsync(userId))
                return true;

            // Check if the user already has teams of this type
            if (_playerTeamMemberships.TryGetValue(userId, out var teamIds))
            {
                int teamsOfTypeCount = 0;
                foreach (var teamId in teamIds)
                {
                    if (_activeTeams.TryGetValue(teamId, out var team) && team.GameType == type)
                    {
                        teamsOfTypeCount++;
                    }
                }

                // Limit users to 1 team per type
                // This could be configured differently based on business requirements
                return teamsOfTypeCount < 1;
            }

            return true;
        }

        /// <summary>
        /// Check if a team name is available
        /// </summary>
        public async Task<bool> IsTeamNameAvailableAsync(string name)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Check in-memory cache first
            bool nameExists = _activeTeams.Values.Any(t => t.TeamName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (nameExists)
                return false;

            // Double-check with repository
            var existingTeam = await _teamRepository.GetTeamByNameAsync(name);
            return existingTeam == null;
        }

        /// <summary>
        /// Get a team by ID
        /// </summary>
        public async Task<Team?> GetTeamByIdAsync(string teamId)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(teamId))
                return null;

            // Try to get from in-memory cache
            if (_activeTeams.TryGetValue(teamId, out var team))
                return team;

            // If not in cache, try to get from repository
            return await _teamRepository.GetTeamByIdAsync(teamId);
        }

        /// <summary>
        /// Get a team by name
        /// </summary>
        public async Task<Team?> GetTeamByNameAsync(string teamName)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(teamName))
                return null;

            // Look in in-memory cache first
            var team = _activeTeams.Values.FirstOrDefault(t =>
                t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase));

            if (team != null)
                return team;

            // If not in cache, try to get from repository
            return await _teamRepository.GetTeamByNameAsync(teamName);
        }

        /// <summary>
        /// Get all teams a player is a member of
        /// </summary>
        public async Task<List<Team>> GetPlayerTeamsAsync(ulong userId)
        {
            if (!_isInitialized)
                await Initialize();

            var teams = new List<Team>();

            // Check in-memory cache
            if (_playerTeamMemberships.TryGetValue(userId, out var teamIds))
            {
                foreach (var teamId in teamIds)
                {
                    if (_activeTeams.TryGetValue(teamId, out var team))
                    {
                        teams.Add(team);
                    }
                }
            }

            // If no teams found, check with repository
            if (teams.Count == 0)
            {
                var repoTeams = await _teamRepository.GetTeamsByPlayerIdAsync(userId);
                teams.AddRange(repoTeams);
            }

            return teams;
        }

        /// <summary>
        /// Get teams a player is a member of by type
        /// </summary>
        public async Task<List<Team>> GetPlayerTeamsByTypeAsync(ulong userId, TeamGameType type)
        {
            var allTeams = await GetPlayerTeamsAsync(userId);
            return allTeams.Where(t => t.GameType == type).ToList();
        }

        /// <summary>
        /// Add a player to a team
        /// </summary>
        public async Task<bool> AddPlayerToTeamAsync(string teamId, DiscordUser user, PlayerRole role, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
            if (!canModify && !await HasTeamAdminPrivilegesAsync(requester.Id))
                return false;

            // Verify player counts
            var (requiredCore, maxSecondary, maxSubstitute) = team.GetPlayerCounts();

            switch (role)
            {
                case PlayerRole.Core:
                    if (team.CorePlayers.Count >= requiredCore)
                        return false; // Core player slots filled
                    break;
                case PlayerRole.Secondary:
                    if (team.SecondaryPlayers.Count >= maxSecondary)
                        return false; // Secondary player slots filled

                    // Check cooldown for secondary player changes
                    var secondaryCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SecondaryPlayerChange);
                    if (secondaryCooldown > 0 && !await HasTeamAdminPrivilegesAsync(requester.Id))
                        return false; // Still on cooldown
                    break;
                case PlayerRole.Substitute:
                    if (team.SubstitutePlayers.Count >= maxSubstitute)
                        return false; // Substitute player slots filled

                    // Check cooldown for substitute player changes
                    var substituteCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SubstitutePlayerChange);
                    if (substituteCooldown > 0 && !await HasTeamAdminPrivilegesAsync(requester.Id))
                        return false; // Still on cooldown
                    break;
            }

            // Check if the player is already on the team
            if (team.HasPlayer(user.Id))
                return false;

            // Add the player to the team based on role
            switch (role)
            {
                case PlayerRole.Core:
                    team.AddCorePlayer(user);
                    break;
                case PlayerRole.Secondary:
                    team.AddSecondaryPlayer(user);
                    break;
                case PlayerRole.Substitute:
                    team.AddSubstitutePlayer(user);
                    break;
            }

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;
            UpdatePlayerTeamMemberships(team);

            _logger.LogInformation($"Player {user.Username} (ID: {user.Id}) added to team '{team.TeamName}' as {role} by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Remove a player from a team
        /// </summary>
        public async Task<bool> RemovePlayerFromTeamAsync(string teamId, ulong userId, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
            if (!canModify && !await HasTeamAdminPrivilegesAsync(requester.Id) && requester.Id != userId) // Player can remove themselves
                return false;

            // Check if the player is on the team
            bool isCore = team.CorePlayers.Any(p => p.Id == userId);
            bool isSecondary = team.SecondaryPlayers.Any(p => p.Id == userId);
            bool isSubstitute = team.SubstitutePlayers.Any(p => p.Id == userId);

            if (!isCore && !isSecondary && !isSubstitute)
                return false; // Player not on the team

            // Core players can only be removed by admins
            if (isCore && !await HasTeamAdminPrivilegesAsync(requester.Id) && requester.Id != team.CreatorId)
                return false;

            // Use reflection to get player info list for the role
            List<PlayerInfo>? playerInfoToUpdate = null;
            if (isCore)
                playerInfoToUpdate = team.CorePlayerInfo;
            else if (isSecondary)
                playerInfoToUpdate = team.SecondaryPlayerInfo;
            else if (isSubstitute)
                playerInfoToUpdate = team.SubstitutePlayerInfo;

            // Remove from the appropriate list in the team
            if (isCore)
                team.CorePlayers.RemoveAll(p => p.Id == userId);
            else if (isSecondary)
                team.SecondaryPlayers.RemoveAll(p => p.Id == userId);
            else if (isSubstitute)
                team.SubstitutePlayers.RemoveAll(p => p.Id == userId);

            // Remove from the player info list
            if (playerInfoToUpdate != null)
                playerInfoToUpdate.RemoveAll(p => p.Id == userId);

            // Update cooldown if needed
            if (isSecondary)
                team.LastSecondaryChangeDate = DateTimeOffset.UtcNow;
            else if (isSubstitute)
                team.LastSubstituteChangeDate = DateTimeOffset.UtcNow;

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;
            UpdatePlayerTeamMemberships(team);

            _logger.LogInformation($"Player (ID: {userId}) removed from team '{team.TeamName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Change a player's role in a team
        /// </summary>
        public async Task<bool> ChangePlayerRoleAsync(string teamId, ulong userId, PlayerRole newRole, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
            if (!canModify && !await HasTeamAdminPrivilegesAsync(requester.Id))
                return false;

            // Check if the player is on the team
            bool isCore = team.CorePlayers.Any(p => p.Id == userId);
            bool isSecondary = team.SecondaryPlayers.Any(p => p.Id == userId);
            bool isSubstitute = team.SubstitutePlayers.Any(p => p.Id == userId);

            if (!isCore && !isSecondary && !isSubstitute)
                return false; // Player not on the team

            // Get current role
            PlayerRole currentRole;
            if (isCore) currentRole = PlayerRole.Core;
            else if (isSecondary) currentRole = PlayerRole.Secondary;
            else currentRole = PlayerRole.Substitute;

            // If the role is the same, nothing to do
            if (currentRole == newRole)
                return true;

            // Core players can only be changed by admins
            if (isCore && !await HasTeamAdminPrivilegesAsync(requester.Id) && requester.Id != team.CreatorId)
                return false;

            // Check if there's space in the new role
            var (requiredCore, maxSecondary, maxSubstitute) = team.GetPlayerCounts();
            if ((newRole == PlayerRole.Core && team.CorePlayers.Count >= requiredCore) ||
                (newRole == PlayerRole.Secondary && team.SecondaryPlayers.Count >= maxSecondary) ||
                (newRole == PlayerRole.Substitute && team.SubstitutePlayers.Count >= maxSubstitute))
            {
                return false; // No space in the new role
            }

            // Check cooldowns if changing to/from Secondary or Substitute
            if ((currentRole == PlayerRole.Secondary || newRole == PlayerRole.Secondary) &&
                !await HasTeamAdminPrivilegesAsync(requester.Id))
            {
                var secondaryCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SecondaryPlayerChange);
                if (secondaryCooldown > 0)
                    return false; // Still on cooldown
            }

            if ((currentRole == PlayerRole.Substitute || newRole == PlayerRole.Substitute) &&
                !await HasTeamAdminPrivilegesAsync(requester.Id))
            {
                var substituteCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SubstitutePlayerChange);
                if (substituteCooldown > 0)
                    return false; // Still on cooldown
            }

            // Find the player in the current role
            DiscordUser? player = null;
            if (isCore) player = team.CorePlayers.First(p => p.Id == userId);
            else if (isSecondary) player = team.SecondaryPlayers.First(p => p.Id == userId);
            else player = team.SubstitutePlayers.First(p => p.Id == userId);

            // First remove from current role
            if (isCore)
            {
                team.CorePlayers.RemoveAll(p => p.Id == userId);
                team.CorePlayerInfo.RemoveAll(p => p.Id == userId);
            }
            else if (isSecondary)
            {
                team.SecondaryPlayers.RemoveAll(p => p.Id == userId);
                team.SecondaryPlayerInfo.RemoveAll(p => p.Id == userId);
                team.LastSecondaryChangeDate = DateTimeOffset.UtcNow;
            }
            else if (isSubstitute)
            {
                team.SubstitutePlayers.RemoveAll(p => p.Id == userId);
                team.SubstitutePlayerInfo.RemoveAll(p => p.Id == userId);
                team.LastSubstituteChangeDate = DateTimeOffset.UtcNow;
            }

            // Then add to new role
            switch (newRole)
            {
                case PlayerRole.Core:
                    team.AddCorePlayer(player);
                    break;
                case PlayerRole.Secondary:
                    team.AddSecondaryPlayer(player);
                    break;
                case PlayerRole.Substitute:
                    team.AddSubstitutePlayer(player);
                    break;
            }

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;

            _logger.LogInformation($"Player (ID: {userId}) changed from {currentRole} to {newRole} in team '{team.TeamName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Update a team's name
        /// </summary>
        public async Task<bool> UpdateTeamNameAsync(string teamId, string newName, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            if (string.IsNullOrWhiteSpace(newName))
                return false;

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
            if (!canModify && !await HasTeamAdminPrivilegesAsync(requester.Id))
                return false;

            // Check if the new name is available
            bool nameAvailable = await IsTeamNameAvailableAsync(newName);
            if (!nameAvailable)
                return false;

            // Check cooldown for name changes
            if (!await HasTeamAdminPrivilegesAsync(requester.Id))
            {
                var nameCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.NameChange);
                if (nameCooldown > 0)
                    return false; // Still on cooldown
            }

            // Update the name
            string oldName = team.TeamName;
            team.ChangeName(newName);

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;

            _logger.LogInformation($"Team name changed from '{oldName}' to '{newName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Check if a player can modify a team
        /// </summary>
        public async Task<bool> CanModifyTeamAsync(string teamId, ulong userId)
        {
            if (!_isInitialized)
                await Initialize();

            // Check if user has admin privileges first
            if (await HasTeamAdminPrivilegesAsync(userId))
                return true;

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Team creator can always modify
            if (team.CreatorId == userId)
                return true;

            // Core players can modify the team
            return team.CorePlayers.Any(p => p.Id == userId);
        }

        /// <summary>
        /// Check if a user has team admin privileges
        /// </summary>
        public async Task<bool> HasTeamAdminPrivilegesAsync(ulong userId)
        {
            if (!_isInitialized)
                await Initialize();

            try
            {
                // Check Discord's built-in permissions
                if (_guildId == 0)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                    return false;
                }

                var guild = await _discordClient.GetGuildAsync(_guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check Discord permissions for user {userId} - member not found");
                    return false;
                }

                // Check if user is the server owner or has Administrator permission
                // Also consider moderator permissions (ManageGuild) for team management
                return member.IsOwner ||
                       member.Permissions.HasFlag(DiscordPermission.Administrator) ||
                       member.Permissions.HasFlag(DiscordPermission.ManageGuild);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking team admin privileges for user {userId}");
                return false;
            }
        }

        /// <summary>
        /// Check if a player is on cooldown for making changes to a specific team
        /// </summary>
        public async Task<int> GetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return 0;

            // Determine cooldown based on change type
            DateTimeOffset? lastChangeDate = null;
            TimeSpan cooldownPeriod = TimeSpan.Zero;

            switch (changeType)
            {
                case TeamChangeType.NameChange:
                    lastChangeDate = team.LastNameChangeDate;
                    cooldownPeriod = TimeSpan.FromDays(7); // 1 week
                    break;
                case TeamChangeType.SecondaryPlayerChange:
                    lastChangeDate = team.LastSecondaryChangeDate;
                    cooldownPeriod = TimeSpan.FromDays(30); // 1 month
                    break;
                case TeamChangeType.SubstitutePlayerChange:
                    lastChangeDate = team.LastSubstituteChangeDate;
                    cooldownPeriod = TimeSpan.FromDays(7); // 1 week
                    break;
                default:
                    return 0; // No cooldown for other changes
            }

            // If no previous change, no cooldown
            if (lastChangeDate == null)
                return 0;

            // Calculate remaining cooldown time
            var timeSinceLastChange = DateTimeOffset.UtcNow - lastChangeDate.Value;
            if (timeSinceLastChange >= cooldownPeriod)
                return 0; // Cooldown expired

            // Return remaining minutes
            return (int)(cooldownPeriod - timeSinceLastChange).TotalMinutes;
        }

        /// <summary>
        /// Delete a team
        /// </summary>
        public async Task<bool> DeleteTeamAsync(string teamId, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Only admins or the creator can delete a team
            if (!await HasTeamAdminPrivilegesAsync(requester.Id) && team.CreatorId != requester.Id)
                return false;

            // Remove team from repository
            bool removed = await _teamRepository.DeleteTeamAsync(teamId);
            if (!removed)
                return false;

            // Update in-memory cache
            if (_activeTeams.ContainsKey(teamId))
                _activeTeams.Remove(teamId);

            // Update player memberships
            var playerIds = new List<ulong>();
            playerIds.AddRange(team.CorePlayers.Select(p => p.Id));
            playerIds.AddRange(team.SecondaryPlayers.Select(p => p.Id));
            playerIds.AddRange(team.SubstitutePlayers.Select(p => p.Id));

            foreach (var playerId in playerIds)
            {
                if (_playerTeamMemberships.TryGetValue(playerId, out var teamIds))
                {
                    teamIds.Remove(teamId);
                    if (teamIds.Count == 0)
                        _playerTeamMemberships.Remove(playerId);
                }
            }

            _logger.LogInformation($"Team '{team.TeamName}' (ID: {teamId}) deleted by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Transfer team ownership
        /// </summary>
        public async Task<bool> TransferTeamOwnershipAsync(string teamId, ulong newOwnerId, DiscordUser requester)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Only admins or the creator can transfer ownership
            if (!await HasTeamAdminPrivilegesAsync(requester.Id) && team.CreatorId != requester.Id)
                return false;

            // Check if the new owner is a core player
            bool isCore = team.CorePlayers.Any(p => p.Id == newOwnerId);
            if (!isCore)
                return false; // Only core players can be owners

            // Find the new owner
            var newOwner = team.CorePlayers.First(p => p.Id == newOwnerId);

            // Transfer ownership
            team.Creator = newOwner;
            team.CreatorId = newOwnerId;
            team.CreatorUsername = newOwner.Username;

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;

            _logger.LogInformation($"Team '{team.TeamName}' ownership transferred to {newOwner.Username} (ID: {newOwnerId}) by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Get the top teams by rating for a specific type
        /// </summary>
        public async Task<List<Team>> GetTopTeamsByRatingAsync(Wabbit.Models.TeamGameType type, int count = 10)
        {
            if (!_isInitialized)
                await Initialize();

            // Get teams from repository sorted by rating
            var teams = await _teamRepository.GetTopTeamsByRatingAsync(type, count);

            // Update in-memory cache with these teams if they're not already there
            foreach (var team in teams)
            {
                if (!_activeTeams.ContainsKey(team.TeamId))
                {
                    _activeTeams[team.TeamId] = team;
                    UpdatePlayerTeamMemberships(team);
                }
            }

            return teams;
        }

        /// <summary>
        /// Get the number of teams by type
        /// </summary>
        public async Task<Dictionary<Wabbit.Models.TeamGameType, int>> GetTeamCountsByTypeAsync()
        {
            if (!_isInitialized)
                await Initialize();

            return await _teamRepository.GetTeamCountsByTypeAsync();
        }

        /// <summary>
        /// Record a match result for a team
        /// </summary>
        public async Task<bool> RecordMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Get opponent name
            string opponentName = "Unknown";

            // Check if opponent is a team
            var opponentTeam = await GetTeamByIdAsync(opponentId);
            if (opponentTeam != null)
            {
                opponentName = opponentTeam.TeamName;
            }
            else
            {
                // Check if opponent is a player
                try
                {
                    var playerRating = await _playerRatingRepository.GetPlayerRatingAsync(ulong.Parse(opponentId));
                    if (playerRating != null)
                    {
                        opponentName = playerRating.Username;
                    }
                }
                catch { /* Ignore parsing errors */ }
            }

            // Update team rating
            team.UpdateRating(newRating, isWin, opponentName);

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;

            _logger.LogInformation($"Match result recorded for team '{team.TeamName}': {(isWin ? "Win" : "Loss")} against '{opponentName}', Rating change: {oldRating} -> {newRating} ({ratingChange:+#;-#;0})");
            return true;
        }

        /// <summary>
        /// Reset a team change cooldown (admin only)
        /// </summary>
        public async Task<bool> ResetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType)
        {
            if (!_isInitialized)
                await Initialize();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Reset the appropriate cooldown
            switch (changeType)
            {
                case TeamChangeType.NameChange:
                    team.LastNameChangeDate = null;
                    break;
                case TeamChangeType.SecondaryPlayerChange:
                    team.LastSecondaryChangeDate = null;
                    break;
                case TeamChangeType.SubstitutePlayerChange:
                    team.LastSubstituteChangeDate = null;
                    break;
                default:
                    return false;
            }

            // Update repository
            await _teamRepository.UpdateTeamAsync(team);

            // Update in-memory cache
            _activeTeams[team.TeamId] = team;

            _logger.LogInformation($"Cooldown reset for team '{team.TeamName}' (ID: {teamId}), change type: {changeType}");
            return true;
        }

        /// <summary>
        /// Helper method to update player team memberships
        /// </summary>
        private void UpdatePlayerTeamMemberships(Team team)
        {
            // Collect all players from this team
            var playerIds = new HashSet<ulong>();
            foreach (var player in team.CorePlayers)
                playerIds.Add(player.Id);
            foreach (var player in team.SecondaryPlayers)
                playerIds.Add(player.Id);
            foreach (var player in team.SubstitutePlayers)
                playerIds.Add(player.Id);

            // Update the membership lookup
            foreach (var playerId in playerIds)
            {
                if (!_playerTeamMemberships.TryGetValue(playerId, out var teamIds))
                {
                    teamIds = new HashSet<string>();
                    _playerTeamMemberships[playerId] = teamIds;
                }
                teamIds.Add(team.TeamId);
            }
        }
    }
}