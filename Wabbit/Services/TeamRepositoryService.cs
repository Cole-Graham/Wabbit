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
using Wabbit.Models;
using Wabbit.Models.Rating;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for team data storage and retrieval
    /// </summary>
    public class TeamRepositoryService : ITeamRepositoryService
    {
        private readonly string _dataDirectory;
        private readonly string _teamsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<TeamRepositoryService> _logger;
        private readonly DiscordClient _client;

        private List<Team> _teams = new List<Team>();
        private readonly object _lockObject = new object();

        public TeamRepositoryService(
            ILogger<TeamRepositoryService> logger,
            DiscordClient client)
        {
            _logger = logger;
            _client = client;

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
        }

        /// <summary>
        /// Initialize the repository by loading teams
        /// </summary>
        public async Task Initialize()
        {
            await LoadTeamsAsync();
        }

        /// <inheritdoc/>
        public async Task<Team> CreateTeamAsync(string name, TeamGameType type, DiscordUser creator)
        {
            // Check for duplicate team name
            if (!await IsTeamNameAvailableAsync(name))
            {
                throw new ArgumentException($"Team name '{name}' is already taken.");
            }

            var team = new Team(name, type)
            {
                Creator = creator,
                CreatorId = creator.Id,
                CreatorUsername = creator.Username
            };

            // For 1v1 teams, automatically add the creator as a core player
            if (type == TeamGameType.OneVOne)
            {
                team.AddCorePlayer(creator);
            }

            lock (_lockObject)
            {
                _teams.Add(team);
            }

            await SaveTeamsAsync();
            return team;
        }

        /// <inheritdoc/>
        public Task<Team?> GetTeamByIdAsync(string teamId)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.FirstOrDefault(t => t.TeamId == teamId));
            }
        }

        /// <inheritdoc/>
        public Task<Team?> GetTeamByNameAsync(string teamName)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.FirstOrDefault(t =>
                    t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetTeamsByTypeAsync(TeamGameType type)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.Where(t => t.TeamGameType == type).ToList());
            }
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetTeamsByPlayerAsync(ulong userId)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.Where(t => t.HasPlayer(userId)).ToList());
            }
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetTeamsByPlayerRoleAsync(ulong userId, PlayerRole role)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.Where(t => t.HasPlayerInRole(userId, role)).ToList());
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateTeamAsync(Team team)
        {
            lock (_lockObject)
            {
                var index = _teams.FindIndex(t => t.TeamId == team.TeamId);
                if (index < 0)
                {
                    return Task.FromResult(false).Result;
                }

                _teams[index] = team;
            }

            await SaveTeamsAsync();
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteTeamAsync(string teamId)
        {
            lock (_lockObject)
            {
                var index = _teams.FindIndex(t => t.TeamId == teamId);
                if (index < 0)
                {
                    return Task.FromResult(false).Result;
                }

                _teams.RemoveAt(index);
            }

            await SaveTeamsAsync();
            return true;
        }

        /// <inheritdoc/>
        public Task<bool> IsTeamNameAvailableAsync(string name)
        {
            lock (_lockObject)
            {
                var exists = _teams.Any(t =>
                    t.TeamName.Equals(name, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(!exists);
            }
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetTopTeamsByRatingAsync(TeamGameType type, int count = 10)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams
                    .Where(t => t.TeamGameType == type)
                    .OrderByDescending(t => t.Rating)
                    .Take(count)
                    .ToList());
            }
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetAllTeamsAsync()
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.ToList());
            }
        }

        /// <inheritdoc/>
        public async Task<bool> SaveTeamsAsync()
        {
            try
            {
                var teamsToSave = new List<Team>();

                lock (_lockObject)
                {
                    teamsToSave = _teams.ToList();
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

        /// <inheritdoc/>
        public async Task<bool> LoadTeamsAsync()
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

                if (teamWrapper == null || teamWrapper.Teams == null)
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
                    _teams = loadedTeams;
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
            var cleanTeam = new Team(team.TeamName, team.TeamGameType)
            {
                TeamId = team.TeamId,
                Rating = team.Rating,
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
        /// Wrapper class for serializing teams
        /// </summary>
        private class TeamListWrapper
        {
            public List<Team> Teams { get; set; } = new List<Team>();
        }

        /// <inheritdoc/>
        public Task<List<Team>> GetTeamsByPlayerIdAsync(ulong playerId)
        {
            lock (_lockObject)
            {
                return Task.FromResult(_teams.Where(t => t.HasPlayer(playerId)).ToList());
            }
        }

        /// <inheritdoc/>
        public Task<Dictionary<TeamGameType, int>> GetTeamCountsByTypeAsync()
        {
            lock (_lockObject)
            {
                var counts = new Dictionary<TeamGameType, int>();
                foreach (TeamGameType type in Enum.GetValues(typeof(TeamGameType)))
                {
                    counts[type] = _teams.Count(t => t.TeamGameType == type);
                }

                return Task.FromResult(counts);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> AddTeamAsync(Team team)
        {
            lock (_lockObject)
            {
                if (_teams.Any(t => t.TeamId == team.TeamId))
                {
                    return Task.FromResult(false).Result;
                }

                _teams.Add(team);
            }

            await SaveTeamsAsync();
            return true;
        }
    }
}