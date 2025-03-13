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
using Wabbit.Misc;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament data storage and retrieval
    /// </summary>
    public class TournamentRepositoryService : ITournamentRepositoryService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly string _dataDirectory;
        private readonly string _tournamentsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<TournamentRepositoryService> _logger;

        public TournamentRepositoryService(
            OngoingRounds ongoingRounds,
            ILogger<TournamentRepositoryService> logger)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _tournamentsFilePath = Path.Combine(_dataDirectory, "tournaments.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
                PropertyNamingPolicy = new CustomPropertyNamingPolicy()
            };
        }

        /// <summary>
        /// Initialize the repository by loading tournaments
        /// </summary>
        public void Initialize()
        {
            // Initialize collections if needed
            if (_ongoingRounds.Tournaments == null)
            {
                _ongoingRounds.Tournaments = new List<Tournament>();
            }

            // Load data from files
            LoadTournamentsFromFile();
        }

        /// <summary>
        /// Load tournaments from file
        /// </summary>
        private void LoadTournamentsFromFile()
        {
            try
            {
                if (File.Exists(_tournamentsFilePath))
                {
                    string json = File.ReadAllText(_tournamentsFilePath);
                    _logger.LogInformation($"Loading tournaments from {_tournamentsFilePath}");

                    if (!string.IsNullOrEmpty(json))
                    {
                        var options = new JsonSerializerOptions
                        {
                            ReferenceHandler = ReferenceHandler.Preserve
                        };

                        // Use JsonDocument to parse the structure and extract $values
                        using JsonDocument document = JsonDocument.Parse(json);

                        // Try to find $values at the top level
                        if (document.RootElement.TryGetProperty("$values", out JsonElement valuesElement))
                        {
                            // Direct $values at root level
                            var tournaments = JsonSerializer.Deserialize<List<Tournament>>(valuesElement.GetRawText(), options);
                            if (tournaments != null)
                            {
                                _ongoingRounds.Tournaments = tournaments;
                                _logger.LogInformation($"Loaded {tournaments.Count} tournaments from root $values");
                                return;
                            }
                        }

                        // Try nested pattern with $id/$values format
                        if (document.RootElement.TryGetProperty("$id", out _) &&
                            document.RootElement.TryGetProperty("$values", out JsonElement rootValuesElement))
                        {
                            // Handle direct array case
                            if (rootValuesElement.ValueKind == JsonValueKind.Array)
                            {
                                var tournaments = JsonSerializer.Deserialize<List<Tournament>>(rootValuesElement.GetRawText(), options);
                                if (tournaments != null)
                                {
                                    _ongoingRounds.Tournaments = tournaments;
                                    _logger.LogInformation($"Loaded {tournaments.Count} tournaments from standard $id/$values format");
                                    return;
                                }
                            }
                        }

                        // Try wrapper class approach (most robust)
                        try
                        {
                            var wrapper = JsonSerializer.Deserialize<TournamentListWrapper>(json, options);
                            if (wrapper != null && wrapper.Tournaments != null)
                            {
                                _ongoingRounds.Tournaments = wrapper.Tournaments;
                                _logger.LogInformation($"Loaded {wrapper.Tournaments.Count} tournaments from wrapper class");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to deserialize tournaments with wrapper: {ex.Message}");
                        }

                        // Last resort: try direct deserialization
                        try
                        {
                            var tournaments = JsonSerializer.Deserialize<List<Tournament>>(json, options);
                            if (tournaments != null)
                            {
                                _ongoingRounds.Tournaments = tournaments;
                                _logger.LogInformation($"Loaded {tournaments.Count} tournaments from direct deserialization");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to directly deserialize tournaments: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading tournaments from file: {ex.Message}");
            }

            // If we got here, either the file doesn't exist or we couldn't deserialize it
            _logger.LogWarning("Failed to load tournaments or file doesn't exist. Starting with empty list.");
            _ongoingRounds.Tournaments = new List<Tournament>();
        }

        /// <summary>
        /// Save tournaments to file
        /// </summary>
        public async Task SaveTournamentsAsync()
        {
            try
            {
                // Create a copy of the tournaments to clean for serialization
                var cleanedTournaments = _ongoingRounds.Tournaments.Select(t => CleanTournamentForSerialization(t)).ToList();

                // Wrap in a container to ensure proper $values formatting
                var wrapper = new TournamentListWrapper
                {
                    Tournaments = cleanedTournaments
                };

                string json = JsonSerializer.Serialize(wrapper, _serializerOptions);
                await File.WriteAllTextAsync(_tournamentsFilePath, json);
                _logger.LogInformation($"Saved {cleanedTournaments.Count} tournaments to {_tournamentsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving tournaments to file: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean a tournament for serialization by handling circular references and DiscordEntities
        /// </summary>
        private Tournament CleanTournamentForSerialization(Tournament tournament)
        {
            // Create a deep copy of the tournament to avoid modifying the original
            var cleanedTournament = new Tournament
            {
                Name = tournament.Name,
                Format = tournament.Format,
                CurrentStage = tournament.CurrentStage,
                GameType = tournament.GameType,
                MatchesPerPlayer = tournament.MatchesPerPlayer,
                IsComplete = tournament.IsComplete,
                CustomProperties = tournament.CustomProperties,
                RelatedMessages = tournament.RelatedMessages?.ToList() ?? new List<RelatedMessage>()
            };

            // Handle groups
            cleanedTournament.Groups = tournament.Groups.Select(g => new Tournament.Group
            {
                Name = g.Name,
                IsComplete = g.IsComplete,
                Participants = g.Participants.Select(p => new Tournament.GroupParticipant
                {
                    Player = CleanPlayerForSerialization(p.Player),
                    Wins = p.Wins,
                    Draws = p.Draws,
                    Losses = p.Losses,
                    Seed = p.Seed,
                    GamesWon = p.GamesWon,
                    GamesLost = p.GamesLost,
                    AdvancedToPlayoffs = p.AdvancedToPlayoffs,
                    QualificationInfo = p.QualificationInfo
                }).ToList()
            }).ToList();

            // Handle matches within groups
            foreach (var originalGroup in tournament.Groups)
            {
                var cleanedGroup = cleanedTournament.Groups.FirstOrDefault(g => g.Name == originalGroup.Name);
                if (cleanedGroup != null)
                {
                    cleanedGroup.Matches = originalGroup.Matches.Select(m => CleanMatchForSerialization(m)).ToList();
                }
            }

            // Handle playoff matches
            cleanedTournament.PlayoffMatches = tournament.PlayoffMatches.Select(m => CleanMatchForSerialization(m)).ToList();

            return cleanedTournament;
        }

        /// <summary>
        /// Clean a match for serialization
        /// </summary>
        private Tournament.Match CleanMatchForSerialization(Tournament.Match match)
        {
            var cleanedMatch = new Tournament.Match
            {
                Name = match.Name,
                Type = match.Type,
                BestOf = match.BestOf,
                DisplayPosition = match.DisplayPosition,
                // Convert participants for storage
                Participants = match.Participants.Select(p => new Tournament.MatchParticipant
                {
                    Player = CleanPlayerForSerialization(p.Player),
                    Score = p.Score,
                    IsWinner = p.IsWinner,
                    SourceGroupPosition = p.SourceGroupPosition
                }).ToList()
            };

            // Handle match result if it exists
            if (match.Result != null)
            {
                cleanedMatch.Result = new Tournament.MatchResult
                {
                    Winner = CleanPlayerForSerialization(match.Result.Winner),
                    MapResults = match.Result.MapResults?.ToList() ?? new List<string>(),
                    CompletedAt = match.Result.CompletedAt,
                    DeckCodes = match.Result.DeckCodes?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, Dictionary<string, string>>()
                };
            }

            return cleanedMatch;
        }

        /// <summary>
        /// Clean a player object for serialization
        /// </summary>
        private object? CleanPlayerForSerialization(object? player)
        {
            if (player == null)
                return null;

            // Handle DiscordMember by storing ID and username
            if (player is DiscordMember member)
            {
                return new
                {
                    Type = "DiscordMember",
                    Id = member.Id,
                    Username = member.Username
                };
            }

            // Handle DiscordUser by storing ID and username
            if (player is DiscordUser user)
            {
                return new
                {
                    Type = "DiscordUser",
                    Id = user.Id,
                    Username = user.Username
                };
            }

            // If already a serializable anonymous type with Type/Id/Username properties, return as is
            if (player.GetType().GetProperty("Type") != null &&
                player.GetType().GetProperty("Id") != null &&
                player.GetType().GetProperty("Username") != null)
            {
                return player;
            }

            // Fall back to string representation for anything else
            return player.ToString();
        }

        /// <summary>
        /// Get a tournament by name
        /// </summary>
        public Tournament? GetTournament(string name)
        {
            return _ongoingRounds.Tournaments.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all tournaments
        /// </summary>
        public List<Tournament> GetAllTournaments()
        {
            return _ongoingRounds.Tournaments.ToList();
        }

        /// <summary>
        /// Add a tournament to the repository
        /// </summary>
        public void AddTournament(Tournament tournament)
        {
            _ongoingRounds.Tournaments.Add(tournament);
        }

        /// <summary>
        /// Delete a tournament by name
        /// </summary>
        public async Task DeleteTournamentAsync(string name, DiscordClient? client = null)
        {
            var tournament = GetTournament(name);
            if (tournament != null)
            {
                // Delete related messages if the client is provided
                if (client != null && tournament.RelatedMessages != null)
                {
                    foreach (var relatedMessage in tournament.RelatedMessages)
                    {
                        try
                        {
                            var channel = await client.GetChannelAsync(relatedMessage.ChannelId);
                            var message = await channel.GetMessageAsync(relatedMessage.MessageId);
                            await message.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete message: {ex.Message}");
                        }
                    }
                }

                // Remove the tournament
                _ongoingRounds.Tournaments.Remove(tournament);
                await SaveTournamentsAsync();
            }
        }

        /// <summary>
        /// Archive tournament data
        /// </summary>
        public async Task ArchiveTournamentDataAsync(string tournamentName, DiscordClient? client = null)
        {
            // Get the tournament
            var tournament = GetTournament(tournamentName);
            if (tournament == null)
            {
                _logger.LogWarning($"Tournament {tournamentName} not found for archiving");
                return;
            }

            try
            {
                // Create archive directory if it doesn't exist
                var archiveDir = Path.Combine(_dataDirectory, "Archives");
                Directory.CreateDirectory(archiveDir);

                // Create tournament-specific archive directory
                var tournamentDir = Path.Combine(archiveDir, tournament.Name.Replace(" ", "_"));
                Directory.CreateDirectory(tournamentDir);

                // Archive the tournament data
                var cleanedTournament = CleanTournamentForSerialization(tournament);
                string json = JsonSerializer.Serialize(cleanedTournament, _serializerOptions);

                // Save to archive file with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var archiveFilePath = Path.Combine(tournamentDir, $"tournament_{timestamp}.json");
                await File.WriteAllTextAsync(archiveFilePath, json);

                _logger.LogInformation($"Archived tournament {tournamentName} to {archiveFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to archive tournament {tournamentName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Repair data files
        /// </summary>
        public async Task RepairDataFilesAsync(DiscordClient? client = null)
        {
            _logger.LogInformation("Starting data file repair process");

            try
            {
                // Create backup of current files
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupDir = Path.Combine(_dataDirectory, "Backups", timestamp);
                Directory.CreateDirectory(backupDir);

                if (File.Exists(_tournamentsFilePath))
                {
                    var backupPath = Path.Combine(backupDir, "tournaments.json");
                    File.Copy(_tournamentsFilePath, backupPath);
                    _logger.LogInformation($"Created backup of tournaments.json at {backupPath}");
                }

                // Save current data in clean format
                await SaveTournamentsAsync();

                _logger.LogInformation("Data file repair completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during data file repair: {ex.Message}");
            }
        }

        /// <summary>
        /// Wrapper class for tournament serialization
        /// </summary>
        private class TournamentListWrapper
        {
            [JsonPropertyName("$values")]
            public List<Tournament> Tournaments { get; set; } = [];
        }

        /// <summary>
        /// Custom property naming policy for JSON serialization
        /// </summary>
        private class CustomPropertyNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name)
            {
                // Keep property names as-is
                return name;
            }
        }
    }
}