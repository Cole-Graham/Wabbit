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
using System.Text;

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

                                // Initialize all collections for each signup
                                foreach (var signup in _ongoingRounds.TournamentSignups)
                                {
                                    InitializeCollections(signup);

                                    // Log the participants to verify they're properly loaded
                                    _logger.LogInformation($"Signup '{signup.Name}' loaded with {signup.ParticipantInfo.Count} participants in ParticipantInfo");
                                    foreach (var participant in signup.ParticipantInfo)
                                    {
                                        _logger.LogInformation($"Loaded participant {participant.Username} (ID: {participant.Id})");
                                    }
                                }

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

                                // Initialize all collections for each signup
                                foreach (var signup in _ongoingRounds.TournamentSignups)
                                {
                                    InitializeCollections(signup);

                                    // Log the participants to verify they're properly loaded
                                    _logger.LogInformation($"Signup '{signup.Name}' loaded with {signup.ParticipantInfo.Count} participants in ParticipantInfo");
                                    foreach (var participant in signup.ParticipantInfo)
                                    {
                                        _logger.LogInformation($"Loaded participant {participant.Username} (ID: {participant.Id})");
                                    }
                                }

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

                                // Initialize all collections for each signup
                                foreach (var signup in _ongoingRounds.TournamentSignups)
                                {
                                    InitializeCollections(signup);

                                    // Log the participants to verify they're properly loaded
                                    _logger.LogInformation($"Signup '{signup.Name}' loaded with {signup.ParticipantInfo.Count} participants in ParticipantInfo");
                                    foreach (var participant in signup.ParticipantInfo)
                                    {
                                        _logger.LogInformation($"Loaded participant {participant.Username} (ID: {participant.Id})");
                                    }
                                }

                                _logger.LogInformation($"Loaded {signups.Count} signups from direct deserialization");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to directly deserialize signups: {ex.Message}");
                        }

                        // If standard methods fail, try manually extracting participant info
                        try
                        {
                            // Check if we already have signups loaded
                            if (_ongoingRounds.TournamentSignups.Count > 0)
                            {
                                _logger.LogInformation("Attempting to manually parse participant info from JSON");

                                // Try to manually extract participants from the JSON
                                foreach (var signup in _ongoingRounds.TournamentSignups)
                                {
                                    // Create empty collections if missing
                                    if (signup.ParticipantInfo == null)
                                    {
                                        signup.ParticipantInfo = new List<ParticipantInfo>();
                                    }

                                    // Search for this signup in the JSON by name
                                    try
                                    {
                                        var signupElement = FindSignupElementByName(document.RootElement, signup.Name);
                                        if (signupElement.ValueKind != JsonValueKind.Undefined)
                                        {
                                            if (TryExtractParticipantInfo(signupElement, out var participantInfoList) && participantInfoList.Count > 0)
                                            {
                                                signup.ParticipantInfo = participantInfoList;
                                                _logger.LogInformation($"Manually extracted {participantInfoList.Count} participants for signup '{signup.Name}'");
                                            }
                                        }
                                    }
                                    catch (Exception extractEx)
                                    {
                                        _logger.LogError($"Error manually extracting participants for '{signup.Name}': {extractEx.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception manualEx)
                        {
                            _logger.LogError($"Failed to manually extract participant info: {manualEx.Message}");
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
        /// Find a signup element by name in the JSON
        /// </summary>
        private JsonElement FindSignupElementByName(JsonElement root, string name)
        {
            try
            {
                // Navigate through the nested structure
                if (root.TryGetProperty("$values", out var values1))
                {
                    if (values1.TryGetProperty("$values", out var values2))
                    {
                        // Array of signups
                        foreach (var signupElement in values2.EnumerateArray())
                        {
                            if (signupElement.TryGetProperty("Name", out var nameElement) &&
                                nameElement.GetString() == name)
                            {
                                return signupElement;
                            }
                        }
                    }
                    else
                    {
                        // Direct array of signups
                        foreach (var signupElement in values1.EnumerateArray())
                        {
                            if (signupElement.TryGetProperty("Name", out var nameElement) &&
                                nameElement.GetString() == name)
                            {
                                return signupElement;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error finding signup by name: {ex.Message}");
            }

            return default;
        }

        /// <summary>
        /// Extract ParticipantInfo from a signup element
        /// </summary>
        private bool TryExtractParticipantInfo(JsonElement signupElement, out List<ParticipantInfo> participants)
        {
            participants = new List<ParticipantInfo>();

            try
            {
                if (signupElement.TryGetProperty("ParticipantInfo", out var participantInfoElement))
                {
                    // Navigate through the nested structure to find the actual array
                    JsonElement valuesArray = participantInfoElement;

                    // Try first level of nesting
                    if (participantInfoElement.TryGetProperty("$values", out var values1))
                    {
                        valuesArray = values1;

                        // Try second level of nesting
                        if (values1.TryGetProperty("$values", out var values2))
                        {
                            valuesArray = values2;
                        }
                    }

                    // Enumerate the participants array
                    foreach (var participantElement in valuesArray.EnumerateArray())
                    {
                        if (participantElement.TryGetProperty("Id", out var idElement) &&
                            participantElement.TryGetProperty("Username", out var usernameElement))
                        {
                            participants.Add(new ParticipantInfo
                            {
                                Id = idElement.GetUInt64(),
                                Username = usernameElement.GetString() ?? ""
                            });
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error extracting participant info: {ex.Message}");
            }

            return false;
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
                RelatedMessages = signup.RelatedMessages?.ToList() ?? new List<RelatedMessage>(),
                // Set empty collections for properties that will be ignored during serialization
                Participants = new(),
                Seeds = new(),
                SignupListMessage = null,
                CreatedBy = null!
            };

            // Create a set of all participant IDs from both lists
            HashSet<ulong> allParticipantIds = new HashSet<ulong>();

            // Add IDs from ParticipantInfo
            if (signup.ParticipantInfo != null)
            {
                foreach (var info in signup.ParticipantInfo)
                {
                    allParticipantIds.Add(info.Id);
                }
            }

            // Add IDs from Participants
            foreach (var participant in signup.Participants)
            {
                allParticipantIds.Add(participant.Id);
            }

            _logger.LogInformation($"Cleaning signup '{signup.Name}' for serialization. Combined total of {allParticipantIds.Count} unique participants.");

            // Create a comprehensive ParticipantInfo list
            var mergedParticipantInfo = new List<ParticipantInfo>();

            foreach (var id in allParticipantIds)
            {
                // Try to find in ParticipantInfo first
                var existingInfo = signup.ParticipantInfo?.FirstOrDefault(p => p.Id == id);
                if (existingInfo != null)
                {
                    mergedParticipantInfo.Add(existingInfo);
                    continue;
                }

                // If not found, check Participants
                var participant = signup.Participants.FirstOrDefault(p => p.Id == id);
                if (participant is not null)
                {
                    mergedParticipantInfo.Add(new ParticipantInfo { Id = participant.Id, Username = participant.Username });
                }
            }

            // Store the merged list
            cleanedSignup.ParticipantInfo = mergedParticipantInfo;

            _logger.LogInformation($"Created ParticipantInfo list with {mergedParticipantInfo.Count} entries for signup '{signup.Name}'");

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
            int participantsCount = signup.Participants?.Count ?? 0;
            int participantInfoCount = signup.ParticipantInfo?.Count ?? 0;

            // Log warning if there's a discrepancy
            if (participantsCount != participantInfoCount)
            {
                _logger.LogWarning($"Participant count discrepancy for '{signup.Name}': {participantsCount} in Participants list, {participantInfoCount} in ParticipantInfo list");
            }

            // Always use ParticipantInfo count as it's more reliable and persisted
            return participantInfoCount;
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
            // Initialize all collections to prevent null reference exceptions
            InitializeCollections(signup);

            // Clear existing participants (if any)
            signup.Participants.Clear();

            // Log the number of participants we're going to load
            _logger.LogInformation($"Loading {signup.ParticipantInfo.Count} participants for signup {signup.Name}");

            // Check if client has any guilds
            if (client.Guilds == null || client.Guilds.Count == 0)
            {
                _logger.LogWarning($"Client has no guilds available to lookup members for signup '{signup.Name}'");
                return;
            }

            if (verbose)
            {
                _logger.LogInformation($"Client has {client.Guilds.Count} guilds available for member lookup");
            }

            foreach (var participantInfo in signup.ParticipantInfo)
            {
                try
                {
                    if (verbose)
                    {
                        _logger.LogInformation($"Looking up participant: {participantInfo.Username} (ID: {participantInfo.Id})");
                    }

                    // Try to find the member in any of the client's guilds
                    foreach (var guild in client.Guilds.Values)
                    {
                        try
                        {
                            var member = await guild.GetMemberAsync(participantInfo.Id);
                            if (member is not null)
                            {
                                signup.Participants.Add(member);
                                if (verbose)
                                {
                                    _logger.LogInformation($"Found member {participantInfo.Username} in guild {guild.Name}");
                                }
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (verbose)
                            {
                                _logger.LogWarning($"Could not find member {participantInfo.Username} in guild {guild.Name}: {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    _logger.LogWarning($"Inner exception: {ex.InnerException.Message}");
                                }
                            }
                        }
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

            if (verbose)
            {
                _logger.LogInformation($"Loaded {signup.Participants.Count} participants and have {signup.ParticipantInfo.Count} entries in ParticipantInfo");
            }

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

        /// <summary>
        /// Loads all participants for all signups
        /// </summary>
        public async Task LoadAllParticipantsAsync(DiscordClient client)
        {
            foreach (var signup in GetAllSignups())
            {
                await LoadParticipantsAsync(signup, client);
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

        /// <summary>
        /// Initialize all collections in a TournamentSignup object to prevent null reference exceptions
        /// </summary>
        private void InitializeCollections(TournamentSignup signup)
        {
            if (signup.Participants == null)
            {
                signup.Participants = new List<DiscordMember>();
            }

            if (signup.Seeds == null)
            {
                signup.Seeds = new List<ParticipantSeed>();
            }

            if (signup.ParticipantInfo == null)
            {
                signup.ParticipantInfo = new List<ParticipantInfo>();
            }

            if (signup.SeedInfo == null)
            {
                signup.SeedInfo = new List<SeedInfo>();
            }

            if (signup.RelatedMessages == null)
            {
                signup.RelatedMessages = new List<RelatedMessage>();
            }
        }

        /// <summary>
        /// Creates a standardized signup embed with the required format
        /// </summary>
        /// <param name="signup">The tournament signup</param>
        /// <returns>A Discord embed for the signup</returns>
        public DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
        {
            var builder = new DiscordEmbedBuilder()
                .WithTitle($"🏆 Tournament Signup: {signup.Name}")
                .WithColor(new DiscordColor(75, 181, 67));

            // Add Format field
            builder.AddField("Format", signup.Format.ToString(), true);

            // Add Game Type field - show 1v1 or 2v2
            string gameType = signup.Type == GameType.OneVsOne ? "1v1" : "2v2";
            builder.AddField("Game Type", gameType, true);

            // Add Scheduled Start Time field if available
            if (signup.ScheduledStartTime.HasValue)
            {
                string formattedTime = $"<t:{((DateTimeOffset)signup.ScheduledStartTime).ToUnixTimeSeconds()}:F>";
                builder.AddField("Scheduled Start Time", formattedTime, false);
            }

            // Process the participants list in the standardized format
            List<object> sortedParticipants;
            int participantsCount = signup.Participants?.Count ?? 0;
            int participantInfoCount = signup.ParticipantInfo?.Count ?? 0;

            // Use the list with more participants (usually Participants, but fall back to ParticipantInfo)
            if (participantsCount >= participantInfoCount && participantsCount > 0)
            {
                // Use Discord Members
                sortedParticipants = (signup.Participants ?? new List<DiscordMember>())
                    .Select(p => new
                    {
                        Username = p.Username,
                        Id = p.Id,
                        Seed = signup.Seeds?.FirstOrDefault(s => s.PlayerId == p.Id)?.Seed ?? 0
                    })
                    .OrderBy(p => p.Seed == 0) // Seeded players first
                    .ThenBy(p => p.Seed) // Then by seed value
                    .ThenBy(p => p.Username) // Then alphabetically
                    .Cast<object>()
                    .ToList();
            }
            else if (participantInfoCount > 0)
            {
                // Use ParticipantInfo
                sortedParticipants = (signup.ParticipantInfo ?? new List<ParticipantInfo>())
                    .Select(p => new
                    {
                        Username = p.Username,
                        Id = p.Id,
                        Seed = signup.Seeds?.FirstOrDefault(s => s.PlayerId == p.Id)?.Seed ?? 0
                    })
                    .OrderBy(p => p.Seed == 0)
                    .ThenBy(p => p.Seed)
                    .ThenBy(p => p.Username)
                    .Cast<object>()
                    .ToList();
            }
            else
            {
                // No participants
                builder.AddField("Participants (0)", "No participants yet", false);
                builder.WithDescription("Sign up for this tournament by clicking the button below.")
                       .WithTimestamp(signup.CreatedAt)
                       .WithFooter($"Created by @{signup.CreatedBy?.Username ?? signup.CreatorUsername}");
                return builder.Build();
            }

            // Format participants into columns
            StringBuilder participantsText = new StringBuilder();
            int count = sortedParticipants.Count;

            // Add two participants per row
            for (int i = 0; i < count; i += 2)
            {
                // Left column - always present
                var leftParticipant = sortedParticipants[i];
                string leftSeed = GetSeedDisplay(leftParticipant);
                participantsText.Append($"{i + 1}. <@{GetParticipantId(leftParticipant)}> {leftSeed}");

                // Right column - may not be present for odd number of participants
                if (i + 1 < count)
                {
                    var rightParticipant = sortedParticipants[i + 1];
                    string rightSeed = GetSeedDisplay(rightParticipant);
                    participantsText.Append($"     {i + 2}. <@{GetParticipantId(rightParticipant)}> {rightSeed}");
                }

                if (i + 2 < count) // Add newline if not the last row
                {
                    participantsText.AppendLine();
                }
            }

            // Add the participants field
            builder.AddField($"Participants ({count})", participantsText.ToString(), false);

            // Add status and creator info
            string statusText = signup.IsOpen ? "Sign up for this tournament by clicking the button below." : "This signup is now closed.";
            builder.WithDescription(statusText)
                   .WithFooter($"Created by @{signup.CreatedBy?.Username ?? signup.CreatorUsername}");

            if (signup.CreatedAt != default)
            {
                builder.WithTimestamp(signup.CreatedAt);
            }

            return builder.Build();
        }

        private string GetSeedDisplay(object participant)
        {
            if (participant is null) return "";

            var seed = participant.GetType().GetProperty("Seed")?.GetValue(participant);
            return seed is int seedValue && seedValue > 0 ? $"[Seed #{seedValue}]" : "";
        }

        private ulong GetParticipantId(object participant)
        {
            if (participant is null) return 0;

            var id = participant.GetType().GetProperty("Id")?.GetValue(participant);
            return id is ulong userId ? userId : 0;
        }
    }
}