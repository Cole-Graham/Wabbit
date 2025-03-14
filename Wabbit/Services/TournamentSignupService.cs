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
    /// Service for managing tournament signups
    /// </summary>
    public class TournamentSignupService : ITournamentSignupService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly string _dataDirectory;
        private readonly string _signupsFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<TournamentSignupService> _logger;

        public TournamentSignupService(
            OngoingRounds ongoingRounds,
            ILogger<TournamentSignupService> logger)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _signupsFilePath = Path.Combine(_dataDirectory, "signups.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true,
                PropertyNamingPolicy = new CustomPropertyNamingPolicy()
            };

            // Initialize collections if needed
            if (_ongoingRounds.TournamentSignups == null)
            {
                _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
            }

            // Load signups from file
            LoadSignupsFromFile();
        }

        /// <summary>
        /// Load signups from file
        /// </summary>
        private void LoadSignupsFromFile()
        {
            try
            {
                if (File.Exists(_signupsFilePath))
                {
                    string json = File.ReadAllText(_signupsFilePath);
                    _logger.LogInformation($"Loading signups from {_signupsFilePath}");

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
                            var signups = JsonSerializer.Deserialize<List<TournamentSignup>>(valuesElement.GetRawText(), options);
                            if (signups != null)
                            {
                                _ongoingRounds.TournamentSignups = signups;
                                _logger.LogInformation($"Loaded {signups.Count} signups from root $values");
                                return;
                            }
                        }

                        // Try wrapper class approach (most robust)
                        try
                        {
                            var wrapper = JsonSerializer.Deserialize<SignupListWrapper>(json, options);
                            if (wrapper != null && wrapper.Signups != null)
                            {
                                _ongoingRounds.TournamentSignups = wrapper.Signups;
                                _logger.LogInformation($"Loaded {wrapper.Signups.Count} signups from wrapper class");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to deserialize signups with wrapper: {ex.Message}");
                        }

                        // Last resort: try direct deserialization
                        try
                        {
                            var signups = JsonSerializer.Deserialize<List<TournamentSignup>>(json, options);
                            if (signups != null)
                            {
                                _ongoingRounds.TournamentSignups = signups;
                                _logger.LogInformation($"Loaded {signups.Count} signups from direct deserialization");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to directly deserialize signups: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading signups from file: {ex.Message}");
            }

            // If we got here, either the file doesn't exist or we couldn't deserialize it
            _logger.LogWarning("Failed to load signups or file doesn't exist. Starting with empty list.");
            _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
        }

        /// <summary>
        /// Save signups to file
        /// </summary>
        public async Task SaveSignupsAsync()
        {
            try
            {
                // Clean signups for serialization by copying necessary data
                var cleanedSignups = _ongoingRounds.TournamentSignups.Select(s => CleanSignupForSerialization(s)).ToList();

                // Wrap in a container to ensure proper $values formatting
                var wrapper = new SignupListWrapper
                {
                    Signups = cleanedSignups
                };

                string json = JsonSerializer.Serialize(wrapper, _serializerOptions);
                await File.WriteAllTextAsync(_signupsFilePath, json);
                _logger.LogInformation($"Saved {cleanedSignups.Count} signups to {_signupsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving signups to file: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean a signup for serialization
        /// </summary>
        private TournamentSignup CleanSignupForSerialization(TournamentSignup signup)
        {
            var cleanedSignup = new TournamentSignup
            {
                Name = signup.Name,
                IsOpen = signup.IsOpen,
                CreatedAt = signup.CreatedAt,
                Format = signup.Format,
                Type = signup.Type,
                ScheduledStartTime = signup.ScheduledStartTime,
                CreatorId = signup.CreatorId,
                CreatorUsername = signup.CreatorUsername,
                SignupChannelId = signup.SignupChannelId,
                MessageId = signup.MessageId,
                RelatedMessages = signup.RelatedMessages?.ToList() ?? new List<RelatedMessage>()
            };

            // Store participant IDs and usernames for later reconstruction
            cleanedSignup.ParticipantInfo = signup.Participants
                .Select(p => new ParticipantInfo { Id = p.Id, Username = p.Username })
                .ToList();

            // Store seed info for later reconstruction
            cleanedSignup.SeedInfo = signup.Seeds
                .Select(s => new SeedInfo { Id = s.PlayerId, Seed = s.Seed })
                .ToList();

            return cleanedSignup;
        }

        /// <summary>
        /// Creates a new tournament signup
        /// </summary>
        public TournamentSignup CreateSignup(
            string name,
            TournamentFormat format,
            DiscordUser creator,
            ulong signupChannelId,
            GameType gameType = GameType.OneVsOne,
            DateTime? scheduledStartTime = null)
        {
            // Check if signup already exists
            if (_ongoingRounds.TournamentSignups.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A tournament signup with the name '{name}' already exists.");
            }

            var signup = new TournamentSignup
            {
                Name = name,
                Format = format,
                Type = gameType,
                CreatedBy = creator,
                CreatorId = creator.Id,
                CreatorUsername = creator.Username,
                SignupChannelId = signupChannelId,
                ScheduledStartTime = scheduledStartTime
            };

            _ongoingRounds.TournamentSignups.Add(signup);
            SaveSignupsAsync().GetAwaiter().GetResult();

            return signup;
        }

        /// <summary>
        /// Gets a signup by name
        /// </summary>
        public TournamentSignup? GetSignup(string name)
        {
            return _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all signups
        /// </summary>
        public List<TournamentSignup> GetAllSignups()
        {
            return _ongoingRounds.TournamentSignups.ToList();
        }

        /// <summary>
        /// Gets the number of participants in a signup
        /// </summary>
        public int GetParticipantCount(TournamentSignup signup)
        {
            return signup.Participants?.Count ?? 0;
        }

        /// <summary>
        /// Deletes a signup
        /// </summary>
        public async Task DeleteSignupAsync(string name, DiscordClient? client = null, bool preserveData = false)
        {
            var signup = GetSignup(name);
            if (signup != null)
            {
                // Delete related messages if the client is provided
                if (client != null && signup.RelatedMessages != null)
                {
                    foreach (var relatedMessage in signup.RelatedMessages)
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

                // If preserveData is false, remove the signup
                if (!preserveData)
                {
                    _ongoingRounds.TournamentSignups.Remove(signup);
                    await SaveSignupsAsync();
                }
            }
        }

        /// <summary>
        /// Updates a signup
        /// </summary>
        public void UpdateSignup(TournamentSignup signup)
        {
            // Find and update the signup in the list
            var existingSignup = GetSignup(signup.Name);
            if (existingSignup != null)
            {
                int index = _ongoingRounds.TournamentSignups.IndexOf(existingSignup);
                _ongoingRounds.TournamentSignups[index] = signup;
                SaveSignupsAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Loads participant information for a signup
        /// </summary>
        public async Task LoadParticipantsAsync(TournamentSignup signup, DiscordClient client, bool verbose = true)
        {
            try
            {
                // Clear the existing participants list
                signup.Participants.Clear();

                _logger.LogInformation($"Loading {signup.ParticipantInfo.Count} participants for signup '{signup.Name}'");

                // Check if client has any guilds
                if (client.Guilds == null || client.Guilds.Count == 0)
                {
                    _logger.LogWarning("Client has no guilds available - cannot load Discord members");
                }
                else
                {
                    _logger.LogInformation($"Client has {client.Guilds.Count} guilds available for member lookup");
                }

                // Load participants from stored info
                foreach (var participantInfo in signup.ParticipantInfo)
                {
                    try
                    {
                        _logger.LogInformation($"Attempting to load participant {participantInfo.Username} (ID: {participantInfo.Id})");
                        bool foundMember = false;

                        // Try to find the guild member from the stored ID
                        foreach (var guild in client.Guilds?.Values ?? [])
                        {
                            try
                            {
                                _logger.LogInformation($"Trying to get member from guild {guild.Name} (ID: {guild.Id})");
                                var member = await guild.GetMemberAsync(participantInfo.Id);
                                if (member is not null)
                                {
                                    signup.Participants.Add(member);
                                    _logger.LogInformation($"Successfully added {member.Username} (ID: {member.Id}) to participants list");
                                    foundMember = true;
                                    break;
                                }
                            }
                            catch (Exception guildEx)
                            {
                                _logger.LogWarning($"Failed to get member {participantInfo.Username} (ID: {participantInfo.Id}) from guild {guild.Name}: {guildEx.Message}");
                                // Continue to next guild
                            }
                        }

                        if (!foundMember)
                        {
                            _logger.LogWarning($"Could not find member {participantInfo.Username} (ID: {participantInfo.Id}) in any guild");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose)
                        {
                            _logger.LogWarning($"Failed to load participant {participantInfo.Username}: {ex.Message}");
                        }
                    }
                }

                _logger.LogInformation($"Loaded {signup.Participants.Count} participants for signup '{signup.Name}'");

                // Load seed information
                signup.Seeds.Clear();
                foreach (var seedInfo in signup.SeedInfo)
                {
                    var member = signup.Participants.FirstOrDefault(p => p.Id == seedInfo.Id);
                    if (member is not null)
                    {
                        var seed = new ParticipantSeed { Seed = seedInfo.Seed };
                        seed.SetPlayer(member);
                        signup.Seeds.Add(seed);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading participants for signup {signup.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads all participants for all signups
        /// </summary>
        public async Task LoadAllParticipantsAsync(DiscordClient client)
        {
            foreach (var signup in GetAllSignups())
            {
                await LoadParticipantsAsync(signup, client, false);
            }
        }

        /// <summary>
        /// Gets a signup with fully loaded participants
        /// </summary>
        public async Task<TournamentSignup?> GetSignupWithParticipantsAsync(string name, DiscordClient client)
        {
            var signup = GetSignup(name);
            if (signup != null)
            {
                await LoadParticipantsAsync(signup, client);
                return signup;
            }
            return null;
        }

        /// <summary>
        /// Wrapper class for signup serialization
        /// </summary>
        private class SignupListWrapper
        {
            [JsonPropertyName("$values")]
            public List<TournamentSignup> Signups { get; set; } = [];
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