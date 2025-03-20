using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// Unified service for team management, combining data persistence and runtime state
    /// </summary>
    public class TeamService : ITeamService
    {
        private readonly ILogger<TeamService> _logger;
        private readonly DiscordClient _client;
        private readonly IPermissionService _permissionService;
        private readonly IPlayerRatingRepositoryService _playerRatingRepository;

        // File storage paths
        private readonly string _dataDirectory;
        private readonly string _teamsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;

        // Runtime state
        private readonly Dictionary<string, Team> _teams = new();
        private readonly Dictionary<ulong, HashSet<string>> _playerTeamMemberships = new();
        private readonly object _lockObject = new();
        private bool _isInitialized;
        private readonly ulong _guildId;

        /// <summary>
        /// Initializes a new instance of the TeamService
        /// </summary>
        public TeamService(
            ILogger<TeamService> logger,
            DiscordClient client,
            IPermissionService permissionService,
            IPlayerRatingRepositoryService playerRatingRepository)
        {
            _logger = logger;
            _client = client;
            _permissionService = permissionService;
            _playerRatingRepository = playerRatingRepository;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);
            _teamsFilePath = Path.Combine(_dataDirectory, "teams.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };

            // Get the guild ID from config
            var guildId = ConfigManager.Config?.Servers?.FirstOrDefault()?.ServerId ?? 0;
            if (guildId == 0)
            {
                _logger.LogWarning("No guild ID configured for team service. Server management permission checks will not work.");
            }
            _guildId = guildId;
        }

        /// <summary>
        /// Initialize the team service
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _logger.LogInformation("Initializing team service...");
                await LoadTeamsAsync();
                _isInitialized = true;
                _logger.LogInformation($"Team service initialized with {_teams.Count} teams");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing team service");
                throw;
            }
        }

        #region Team Creation and Management

        /// <summary>
        /// Create a new team
        /// </summary>
        public async Task<Team> CreateTeamAsync(string name, TeamGameType type, DiscordUser creator, bool managementOverride = false)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Check if the user can create this type of team (skip check if management override)
            if (!managementOverride && !await CanCreateTeamAsync(creator.Id, type))
                throw new InvalidOperationException($"User {creator.Username} cannot create another team of type {type}");

            // Check if the name is available
            if (!await IsTeamNameAvailableAsync(name))
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

            // Add to local state
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
                UpdatePlayerTeamMemberships(team);
            }

            // Save to storage
            await SaveTeamsAsync();

            _logger.LogInformation($"Team '{name}' ({type}) created by {creator.Username} (ID: {creator.Id})");
            return team;
        }

        /// <summary>
        /// Delete a team
        /// </summary>
        public async Task<bool> DeleteTeamAsync(string teamId, DiscordUser requester)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Only admins or the creator can delete a team
            if (!await HasServerManagementPrivilegesAsync(requester.Id) && team.CreatorId != requester.Id)
                return false;

            // Get all players before removing
            var playerIds = new List<ulong>();
            playerIds.AddRange(team.CorePlayers.Select(p => p.Id));
            playerIds.AddRange(team.SecondaryPlayers.Select(p => p.Id));
            playerIds.AddRange(team.SubstitutePlayers.Select(p => p.Id));

            // Remove from local state
            lock (_lockObject)
            {
                if (!_teams.Remove(teamId))
                    return false;

                // Clean up player memberships
                foreach (var playerId in playerIds)
                {
                    RemovePlayerTeamMembership(playerId, teamId);
                }
            }

            // Save changes
            await SaveTeamsAsync();

            _logger.LogInformation($"Team '{team.TeamName}' (ID: {teamId}) deleted by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Update a team's name
        /// </summary>
        public async Task<bool> UpdateTeamNameAsync(string teamId, string newName, DiscordUser requester)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(newName))
                return false;

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            if (!await CanModifyTeamAsync(teamId, requester.Id))
                return false;

            // Check if the new name is available
            if (!await IsTeamNameAvailableAsync(newName))
                return false;

            // Check cooldown for name changes if not management
            if (!await HasServerManagementPrivilegesAsync(requester.Id))
            {
                var nameCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.NameChange);
                if (nameCooldown > 0)
                    return false; // Still on cooldown
            }

            // Update the name
            string oldName = team.TeamName;
            team.ChangeName(newName);

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Team name changed from '{oldName}' to '{newName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Transfer team ownership
        /// </summary>
        public async Task<bool> TransferTeamOwnershipAsync(string teamId, ulong newOwnerId, DiscordUser requester)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Only users with management privileges or the creator can transfer ownership
            if (!await HasServerManagementPrivilegesAsync(requester.Id) && team.CreatorId != requester.Id)
                return false;

            // Check if the new owner is a core player
            if (!team.CorePlayers.Any(p => p.Id == newOwnerId))
                return false; // Only core players can be owners

            // Find the new owner
            var newOwner = team.CorePlayers.First(p => p.Id == newOwnerId);

            // Transfer ownership
            team.Creator = newOwner;
            team.CreatorId = newOwnerId;
            team.CreatorUsername = newOwner.Username;

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Team '{team.TeamName}' ownership transferred to {newOwner.Username} (ID: {newOwnerId}) by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Check if a user can create a new team
        /// </summary>
        public async Task<bool> CanCreateTeamAsync(ulong userId, TeamGameType type)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Users with server management privileges can always create teams
            if (await HasServerManagementPrivilegesAsync(userId))
                return true;

            // Check if the user already has teams of this type
            lock (_lockObject)
            {
                if (_playerTeamMemberships.TryGetValue(userId, out var teamIds))
                {
                    int teamsOfTypeCount = 0;
                    foreach (var teamId in teamIds)
                    {
                        if (_teams.TryGetValue(teamId, out var team) && team.GameType == type)
                        {
                            teamsOfTypeCount++;
                        }
                    }

                    // Limit users to 1 team per type
                    return teamsOfTypeCount < 1;
                }
            }

            return true;
        }

        /// <summary>
        /// Update a team's information
        /// </summary>
        public async Task<bool> UpdateTeamAsync(Team team)
        {
            if (!_isInitialized)
                await InitializeAsync();

            lock (_lockObject)
            {
                var index = _teams.ContainsKey(team.TeamId);
                if (!index)
                {
                    return Task.FromResult(false).Result;
                }

                _teams[team.TeamId] = team;
                UpdatePlayerTeamMemberships(team);
            }

            await SaveTeamsAsync();
            return true;
        }
        #endregion

        #region Team Queries

        /// <summary>
        /// Check if a team name is available
        /// </summary>
        public async Task<bool> IsTeamNameAvailableAsync(string name)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Check in-memory cache
            lock (_lockObject)
            {
                return !_teams.Values.Any(t => t.TeamName.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Get a team by ID
        /// </summary>
        public async Task<Team?> GetTeamByIdAsync(string teamId)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(teamId))
                return null;

            // Try to get from in-memory cache
            lock (_lockObject)
            {
                return _teams.TryGetValue(teamId, out var team) ? team : null;
            }
        }

        /// <summary>
        /// Get a team by name
        /// </summary>
        public async Task<Team?> GetTeamByNameAsync(string teamName)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (string.IsNullOrWhiteSpace(teamName))
                return null;

            // Look in in-memory cache
            lock (_lockObject)
            {
                return _teams.Values.FirstOrDefault(t =>
                    t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Get all teams
        /// </summary>
        public async Task<List<Team>> GetAllTeamsAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            lock (_lockObject)
            {
                return _teams.Values.ToList();
            }
        }

        /// <summary>
        /// Get teams by type
        /// </summary>
        public async Task<List<Team>> GetTeamsByTypeAsync(TeamGameType type)
        {
            if (!_isInitialized)
                await InitializeAsync();

            lock (_lockObject)
            {
                return _teams.Values.Where(t => t.GameType == type).ToList();
            }
        }

        /// <summary>
        /// Get the top teams by rating for a specific type
        /// </summary>
        public async Task<List<Team>> GetTopTeamsByRatingAsync(TeamGameType type, int count = 10)
        {
            if (!_isInitialized)
                await InitializeAsync();

            lock (_lockObject)
            {
                return _teams.Values
                    .Where(t => t.GameType == type)
                    .OrderByDescending(t => t.Rating)
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// Get the number of teams by type
        /// </summary>
        public async Task<Dictionary<TeamGameType, int>> GetTeamCountsByTypeAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();

            lock (_lockObject)
            {
                var counts = new Dictionary<TeamGameType, int>();
                foreach (TeamGameType type in Enum.GetValues(typeof(TeamGameType)))
                {
                    counts[type] = _teams.Values.Count(t => t.GameType == type);
                }
                return counts;
            }
        }

        /// <summary>
        /// Get all teams a player is a member of
        /// </summary>
        public async Task<List<Team>> GetPlayerTeamsAsync(ulong userId)
        {
            if (!_isInitialized)
                await InitializeAsync();

            var teams = new List<Team>();

            // Check in-memory cache
            lock (_lockObject)
            {
                if (_playerTeamMemberships.TryGetValue(userId, out var teamIds))
                {
                    foreach (var teamId in teamIds)
                    {
                        if (_teams.TryGetValue(teamId, out var team))
                        {
                            teams.Add(team);
                        }
                    }
                }
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

        #endregion

        #region Player Management

        /// <summary>
        /// Add a player to a team
        /// </summary>
        public async Task<bool> AddPlayerToTeamAsync(string teamId, DiscordUser user, PlayerRole role, DiscordUser requester, bool managementOverride = false)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission (skip check if management override)
            bool hasPermission = managementOverride || await CanModifyTeamAsync(teamId, requester.Id);
            if (!hasPermission)
                return false;

            // Verify player counts (skip if management override)
            if (!managementOverride)
            {
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
                        if (secondaryCooldown > 0 && !await HasServerManagementPrivilegesAsync(requester.Id))
                            return false; // Still on cooldown
                        break;
                    case PlayerRole.Substitute:
                        if (team.SubstitutePlayers.Count >= maxSubstitute)
                            return false; // Substitute player slots filled

                        // Check cooldown for substitute player changes
                        var substituteCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SubstitutePlayerChange);
                        if (substituteCooldown > 0 && !await HasServerManagementPrivilegesAsync(requester.Id))
                            return false; // Still on cooldown
                        break;
                }
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

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
                UpdatePlayerTeamMemberships(team);
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Player {user.Username} (ID: {user.Id}) added to team '{team.TeamName}' as {role} by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Remove a player from a team
        /// </summary>
        public async Task<bool> RemovePlayerFromTeamAsync(string teamId, ulong userId, DiscordUser requester, bool managementOverride = false)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // If not management override, check permissions
            if (!managementOverride)
            {
                // Check if requester can modify team or is the player being removed (can remove self)
                bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
                bool isSelf = requester.Id == userId;

                if (!canModify && !isSelf)
                    return false;

                // Can't remove the creator unless it's a management override
                if (team.CreatorId == userId)
                    return false;
            }

            // Check if the player is on the team
            bool isCore = team.CorePlayers.Any(p => p.Id == userId);
            bool isSecondary = team.SecondaryPlayers.Any(p => p.Id == userId);
            bool isSubstitute = team.SubstitutePlayers.Any(p => p.Id == userId);

            if (!isCore && !isSecondary && !isSubstitute)
                return false; // Player not on the team

            // Use reflection to get player info list for the role
            List<PlayerInfo>? playerInfoToUpdate = null;
            if (isCore)
                playerInfoToUpdate = team.CorePlayerInfo;
            else if (isSecondary)
                playerInfoToUpdate = team.SecondaryPlayerInfo;
            else if (isSubstitute)
                playerInfoToUpdate = team.SubstitutePlayerInfo;

            if (playerInfoToUpdate == null)
                return false;

            // Remove from the appropriate list in the team
            if (isCore)
                team.CorePlayers.RemoveAll(p => p.Id == userId);
            else if (isSecondary)
                team.SecondaryPlayers.RemoveAll(p => p.Id == userId);
            else if (isSubstitute)
                team.SubstitutePlayers.RemoveAll(p => p.Id == userId);

            // Remove from the player info list
            playerInfoToUpdate.RemoveAll(p => p.Id == userId);

            // Update cooldown if needed
            if (isSecondary)
                team.LastSecondaryChangeDate = DateTimeOffset.UtcNow;
            else if (isSubstitute)
                team.LastSubstituteChangeDate = DateTimeOffset.UtcNow;

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
                RemovePlayerTeamMembership(userId, team.TeamId);
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Player (ID: {userId}) removed from team '{team.TeamName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        /// <summary>
        /// Change a player's role in a team
        /// </summary>
        public async Task<bool> ChangePlayerRoleAsync(string teamId, ulong userId, PlayerRole newRole, DiscordUser requester)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Check if the requester has permission
            bool canModify = await CanModifyTeamAsync(teamId, requester.Id);
            if (!canModify)
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

            // Core players can only be changed by management or the creator
            if (isCore && !await HasServerManagementPrivilegesAsync(requester.Id) && requester.Id != team.CreatorId)
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
            // Only if requester doesn't have management privileges
            if (!await HasServerManagementPrivilegesAsync(requester.Id))
            {
                if ((currentRole == PlayerRole.Secondary || newRole == PlayerRole.Secondary))
                {
                    var secondaryCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SecondaryPlayerChange);
                    if (secondaryCooldown > 0)
                        return false; // Still on cooldown
                }

                if ((currentRole == PlayerRole.Substitute || newRole == PlayerRole.Substitute))
                {
                    var substituteCooldown = await GetTeamChangeCooldownAsync(teamId, TeamChangeType.SubstitutePlayerChange);
                    if (substituteCooldown > 0)
                        return false; // Still on cooldown
                }
            }

            // Find the player in the current role
            DiscordUser? player = null;
            if (isCore) player = team.CorePlayers.First(p => p.Id == userId);
            else if (isSecondary) player = team.SecondaryPlayers.First(p => p.Id == userId);
            else player = team.SubstitutePlayers.First(p => p.Id == userId);

            if (player == null)
                return false;

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

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Player (ID: {userId}) changed from {currentRole} to {newRole} in team '{team.TeamName}' by {requester.Username} (ID: {requester.Id})");
            return true;
        }

        #endregion

        #region Match Results and Rating

        /// <summary>
        /// Record a match result for a team
        /// </summary>
        public async Task<bool> RecordMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange)
        {
            if (!_isInitialized)
                await InitializeAsync();

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

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Match result recorded for team '{team.TeamName}': {(isWin ? "Win" : "Loss")} against '{opponentName}', Rating change: {oldRating} -> {newRating} ({ratingChange:+#;-#;0})");
            return true;
        }

        /// <summary>
        /// Record a tournament match result for a team
        /// </summary>
        public async Task<bool> RecordTournamentMatchResultAsync(string teamId, string opponentId, bool isWin, int oldRating, int newRating, int ratingChange, string tournamentName)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Get the team
            var team = await GetTeamByIdAsync(teamId);
            if (team == null)
                return false;

            // Get opponent name
            string opponentName = "Unknown";
            var opponentTeam = await GetTeamByIdAsync(opponentId);
            if (opponentTeam != null)
            {
                opponentName = opponentTeam.TeamName;
            }

            // Update team's tournament rating
            team.UpdateTournamentRating(newRating, isWin, opponentName, tournamentName);

            // Update local state and save
            lock (_lockObject)
            {
                _teams[team.TeamId] = team;
            }
            await SaveTeamsAsync();

            _logger.LogInformation($"Tournament match result recorded for team '{team.TeamName}' in '{tournamentName}': {(isWin ? "Win" : "Loss")} against '{opponentName}', Rating change: {oldRating} -> {newRating} ({ratingChange:+#;-#;0})");
            return true;
        }

        #endregion

        #region Permissions and Cooldowns

        /// <summary>
        /// Check if a player can modify a team
        /// </summary>
        public async Task<bool> CanModifyTeamAsync(string teamId, ulong userId)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Check if user has server management privileges first (server owner, admin, etc.)
            if (await HasServerManagementPrivilegesAsync(userId))
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
        /// Check if a user has special Discord server privileges for team management
        /// </summary>
        public async Task<bool> HasServerManagementPrivilegesAsync(ulong userId)
        {
            if (_guildId == 0)
            {
                _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                return false;
            }

            try
            {
                // First check if they have permission through our permission service
                if (_permissionService != null && await _permissionService.HasAdminPrivilegesAsync(userId))
                    return true;

                // Check Discord's built-in permissions
                var guild = await _client.GetGuildAsync(_guildId);
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
                _logger.LogError(ex, $"Error checking team management privileges for user {userId}");
                return false;
            }
        }

        /// <summary>
        /// Check if a player is on cooldown for making changes to a specific team
        /// </summary>
        public async Task<int> GetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType)
        {
            if (!_isInitialized)
                await InitializeAsync();

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
        /// Reset a team change cooldown (requires management privileges)
        /// </summary>
        public async Task<bool> ResetTeamChangeCooldownAsync(string teamId, TeamChangeType changeType, DiscordUser requester)
        {
            if (!_isInitialized)
                await InitializeAsync();

            // Only users with management privileges can reset cooldowns
            if (!await HasServerManagementPrivilegesAsync(requester.Id))
                return false;

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
            await UpdateTeamAsync(team);

            _logger.LogInformation($"Cooldown reset for team '{team.TeamName}' (ID: {teamId}), change type: {changeType}");
            return true;
        }

        #endregion

        #region Data Persistence

        /// <summary>
        /// Save all teams to persistent storage
        /// </summary>
        public async Task<bool> SaveTeamsAsync()
        {
            try
            {
                List<Team> teamsToSave;

                lock (_lockObject)
                {
                    teamsToSave = _teams.Values.ToList();
                }

                var cleanedTeams = teamsToSave.Select(CleanTeamForSerialization).ToList();
                var teamWrapper = new TeamListWrapper { Teams = cleanedTeams };

                string json = JsonSerializer.Serialize(teamWrapper, _serializerOptions);
                await File.WriteAllTextAsync(_teamsFilePath, json);

                _logger.LogInformation("Saved {Count} teams to {FilePath}", cleanedTeams.Count, _teamsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving teams to {FilePath}: {Message}", _teamsFilePath, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Load teams from persistent storage
        /// </summary>
        private async Task<bool> LoadTeamsAsync()
        {
            if (!File.Exists(_teamsFilePath))
            {
                _logger.LogInformation("Teams file {FilePath} doesn't exist yet, starting with empty list", _teamsFilePath);
                return true;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_teamsFilePath);

                // Check if the file is empty
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogInformation("Teams file {FilePath} is empty, starting with empty list", _teamsFilePath);
                    return true;
                }

                var teamWrapper = JsonSerializer.Deserialize<TeamListWrapper>(json, _serializerOptions);

                if (teamWrapper?.Teams == null)
                {
                    _logger.LogWarning("Failed to deserialize teams from {FilePath}", _teamsFilePath);
                    return false;
                }

                var loadedTeams = teamWrapper.Teams;

                // Reconnect serialized Discord users
                foreach (var team in loadedTeams)
                {
                    await ReconnectDiscordObjectsAsync(team);
                }

                lock (_lockObject)
                {
                    _teams.Clear();
                    _playerTeamMemberships.Clear();

                    foreach (var team in loadedTeams)
                    {
                        _teams[team.TeamId] = team;
                        UpdatePlayerTeamMemberships(team);
                    }
                }

                _logger.LogInformation("Loaded {Count} teams from {FilePath}", _teams.Count, _teamsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading teams from {FilePath}: {Message}", _teamsFilePath, ex.Message);
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Wrapper class for serializing teams
        /// </summary>
        private class TeamListWrapper
        {
            public List<Team> Teams { get; set; } = new();
        }

        /// <summary>
        /// Reconnect Discord user objects from stored IDs
        /// </summary>
        private async Task ReconnectDiscordObjectsAsync(Team team)
        {
            try
            {
                // Reconnect creator
                if (team.CreatorId != 0)
                {
                    try
                    {
                        team.Creator = await _client.GetUserAsync(team.CreatorId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect creator {CreatorId} for team {TeamName}",
                            team.CreatorId, team.TeamName);
                    }
                }

                // Reconnect core players
                team.CorePlayers.Clear();
                foreach (var playerInfo in team.CorePlayerInfo)
                {
                    try
                    {
                        var user = await _client.GetUserAsync(playerInfo.Id);
                        if (user != null)
                        {
                            team.CorePlayers.Add(user);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect core player {PlayerId} for team {TeamName}",
                            playerInfo.Id, team.TeamName);
                    }
                }

                // Reconnect secondary players
                team.SecondaryPlayers.Clear();
                foreach (var playerInfo in team.SecondaryPlayerInfo)
                {
                    try
                    {
                        var user = await _client.GetUserAsync(playerInfo.Id);
                        if (user != null)
                        {
                            team.SecondaryPlayers.Add(user);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect secondary player {PlayerId} for team {TeamName}",
                            playerInfo.Id, team.TeamName);
                    }
                }

                // Reconnect substitute players
                team.SubstitutePlayers.Clear();
                foreach (var playerInfo in team.SubstitutePlayerInfo)
                {
                    try
                    {
                        var user = await _client.GetUserAsync(playerInfo.Id);
                        if (user != null)
                        {
                            team.SubstitutePlayers.Add(user);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reconnect substitute player {PlayerId} for team {TeamName}",
                            playerInfo.Id, team.TeamName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reconnecting Discord objects for team {TeamName}: {Message}",
                    team.TeamName, ex.Message);
            }
        }

        /// <summary>
        /// Clean a team object for serialization by ensuring Discord objects are properly handled
        /// </summary>
        private Team CleanTeamForSerialization(Team team)
        {
            // Create a clean copy
            var cleanTeam = new Team(team.TeamName, team.GameType)
            {
                TeamId = team.TeamId,
                Rating = team.Rating,
                TournamentRating = team.TournamentRating,
                CreatedAt = team.CreatedAt,
                LastNameChangeDate = team.LastNameChangeDate,
                LastSecondaryChangeDate = team.LastSecondaryChangeDate,
                LastSubstituteChangeDate = team.LastSubstituteChangeDate,
                Wins = team.Wins,
                Losses = team.Losses,
                CreatorId = team.CreatorId,
                CreatorUsername = team.CreatorUsername,
                RatingHistory = team.RatingHistory.ToList()
            };

            // Ensure player info collections are populated correctly
            cleanTeam.CorePlayerInfo.Clear();
            cleanTeam.SecondaryPlayerInfo.Clear();
            cleanTeam.SubstitutePlayerInfo.Clear();

            // Copy core player info
            foreach (var player in team.CorePlayers)
            {
                cleanTeam.CorePlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
            }

            // Copy secondary player info
            foreach (var player in team.SecondaryPlayers)
            {
                cleanTeam.SecondaryPlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
            }

            // Copy substitute player info
            foreach (var player in team.SubstitutePlayers)
            {
                cleanTeam.SubstitutePlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
            }

            return cleanTeam;
        }

        /// <summary>
        /// Update player-team membership lookups
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

        /// <summary>
        /// Remove a player's membership from a team in the lookup
        /// </summary>
        private void RemovePlayerTeamMembership(ulong userId, string teamId)
        {
            if (_playerTeamMemberships.TryGetValue(userId, out var teamIds))
            {
                teamIds.Remove(teamId);
                if (teamIds.Count == 0)
                    _playerTeamMemberships.Remove(userId);
            }
        }

        #endregion
    }
}