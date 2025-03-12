using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Wabbit.Models;
using DSharpPlus;
using DSharpPlus.Entities;
using MatchType = Wabbit.Models.MatchType;
using Wabbit.Services;
using Wabbit.Data;

namespace Wabbit.Misc
{
    public class TournamentManager
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly string _dataDirectory;
        private readonly string _tournamentsFilePath;
        private readonly string _signupsFilePath;
        private readonly string _tournamentStateFilePath;

        // Constructor
        public TournamentManager(OngoingRounds ongoingRounds)
        {
            _ongoingRounds = ongoingRounds;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _tournamentsFilePath = Path.Combine(_dataDirectory, "tournaments.json");
            _signupsFilePath = Path.Combine(_dataDirectory, "signups.json");
            _tournamentStateFilePath = Path.Combine(_dataDirectory, "tournament_state.json");

            // Initialize collections if needed
            if (_ongoingRounds.Tournaments == null)
            {
                _ongoingRounds.Tournaments = new List<Tournament>();
            }

            if (_ongoingRounds.TournamentSignups == null)
            {
                _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
            }

            // Load data from files
            LoadTournamentsFromFile();
            LoadSignupsFromFile();
        }

        // Load tournaments from file
        private void LoadTournamentsFromFile()
        {
            try
            {
                if (File.Exists(_tournamentsFilePath))
                {
                    string json = File.ReadAllText(_tournamentsFilePath);
                    Console.WriteLine($"Loading tournaments from {_tournamentsFilePath}");

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
                                Console.WriteLine($"Loaded {tournaments.Count} tournaments from root $values");
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
                                    Console.WriteLine($"Loaded {tournaments.Count} tournaments from standard $id/$values format");
                                    return;
                                }
                            }

                            // Handle nested object case (double nesting pattern)
                            if (rootValuesElement.ValueKind == JsonValueKind.Object &&
                                rootValuesElement.TryGetProperty("$id", out _) &&
                                rootValuesElement.TryGetProperty("$values", out JsonElement nestedValuesElement))
                            {
                                var tournaments = JsonSerializer.Deserialize<List<Tournament>>(nestedValuesElement.GetRawText(), options);
                                if (tournaments != null)
                                {
                                    _ongoingRounds.Tournaments = tournaments;
                                    Console.WriteLine($"Loaded {tournaments.Count} tournaments from nested $values format");
                                    return;
                                }
                            }
                        }

                        // Try the wrapper class approach as fallback
                        try
                        {
                            var wrapper = JsonSerializer.Deserialize<TournamentListWrapper>(json, options);
                            if (wrapper?.Values != null)
                            {
                                _ongoingRounds.Tournaments = wrapper.Values;
                                Console.WriteLine($"Loaded {wrapper.Values.Count} tournaments using wrapper class");
                                return;
                            }
                        }
                        catch (Exception wrapperEx)
                        {
                            Console.WriteLine($"Wrapper deserialization failed: {wrapperEx.Message}");
                        }

                        // One last attempt with direct list
                        try
                        {
                            var directTournaments = JsonSerializer.Deserialize<List<Tournament>>(json, options);
                            if (directTournaments != null)
                            {
                                _ongoingRounds.Tournaments = directTournaments;
                                Console.WriteLine($"Loaded {directTournaments.Count} tournaments with direct deserialization");
                                return;
                            }
                        }
                        catch (Exception directEx)
                        {
                            Console.WriteLine($"Direct deserialization failed: {directEx.Message}");
                        }

                        Console.WriteLine("Could not parse tournaments JSON with any known format");
                    }
                    else
                    {
                        Console.WriteLine("Tournaments file exists but is empty");
                    }
                }
                else
                {
                    Console.WriteLine("No tournaments file found, starting with empty list");
                }

                // If we get here, deserialization failed
                _ongoingRounds.Tournaments = new List<Tournament>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading tournaments from file: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _ongoingRounds.Tournaments = new List<Tournament>();
            }
        }

        // Helper class for deserializing wrapped lists
        private class TournamentListWrapper
        {
            [JsonPropertyName("$values")]
            public List<Tournament> Values { get; set; } = new List<Tournament>();
        }

        // Load signups from file
        private void LoadSignupsFromFile()
        {
            try
            {
                if (File.Exists(_signupsFilePath))
                {
                    string json = File.ReadAllText(_signupsFilePath);

                    try
                    {
                        // Parse the JSON document
                        using JsonDocument doc = JsonDocument.Parse(json);

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            List<TournamentSignup> signups = new();

                            foreach (JsonElement element in doc.RootElement.EnumerateArray())
                            {
                                try
                                {
                                    var signup = new TournamentSignup();

                                    // Parse name - required field
                                    if (element.TryGetProperty("Name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String)
                                    {
                                        signup.Name = nameElem.GetString() ?? string.Empty;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Skipping signup with missing or invalid Name property");
                                        continue;
                                    }

                                    // Try to extract IsOpen (boolean)
                                    if (element.TryGetProperty("IsOpen", out var isOpenElem) && isOpenElem.ValueKind == JsonValueKind.True || isOpenElem.ValueKind == JsonValueKind.False)
                                    {
                                        signup.IsOpen = isOpenElem.GetBoolean();
                                    }

                                    // Try to extract Format (string)
                                    if (element.TryGetProperty("Format", out var formatElem) && formatElem.ValueKind == JsonValueKind.String)
                                    {
                                        string formatStr = formatElem.GetString() ?? "GroupStageWithPlayoffs";
                                        if (Enum.TryParse<TournamentFormat>(formatStr, out var format))
                                        {
                                            signup.Format = format;
                                        }
                                    }

                                    // Try to extract CreatedAt (datetime)
                                    if (element.TryGetProperty("CreatedAt", out var createdAtElem) &&
                                        (createdAtElem.ValueKind == JsonValueKind.String || createdAtElem.ValueKind == JsonValueKind.Number))
                                    {
                                        try
                                        {
                                            signup.CreatedAt = createdAtElem.GetDateTime();
                                        }
                                        catch
                                        {
                                            // Use default if parsing fails
                                            Console.WriteLine($"Failed to parse CreatedAt for signup '{signup.Name}'");
                                        }
                                    }

                                    // Try to extract ScheduledStartTime (datetime)
                                    if (element.TryGetProperty("ScheduledStartTime", out var startTimeElem) &&
                                        startTimeElem.ValueKind != JsonValueKind.Null &&
                                        (startTimeElem.ValueKind == JsonValueKind.String || startTimeElem.ValueKind == JsonValueKind.Number))
                                    {
                                        try
                                        {
                                            signup.ScheduledStartTime = startTimeElem.GetDateTime();
                                        }
                                        catch
                                        {
                                            // Leave as null if parsing fails
                                            Console.WriteLine($"Failed to parse ScheduledStartTime for signup '{signup.Name}'");
                                        }
                                    }

                                    // Try to extract SignupChannelId (ulong)
                                    if (element.TryGetProperty("SignupChannelId", out var channelIdElem))
                                    {
                                        try
                                        {
                                            if (channelIdElem.ValueKind == JsonValueKind.Number)
                                            {
                                                signup.SignupChannelId = channelIdElem.GetUInt64();
                                            }
                                            else if (channelIdElem.ValueKind == JsonValueKind.String)
                                            {
                                                string channelIdStr = channelIdElem.GetString() ?? "0";
                                                if (ulong.TryParse(channelIdStr, out ulong channelId))
                                                {
                                                    signup.SignupChannelId = channelId;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            Console.WriteLine($"Failed to parse SignupChannelId for signup '{signup.Name}'");
                                        }
                                    }

                                    // Try to extract MessageId (ulong)
                                    if (element.TryGetProperty("MessageId", out var messageIdElem))
                                    {
                                        try
                                        {
                                            if (messageIdElem.ValueKind == JsonValueKind.Number)
                                            {
                                                signup.MessageId = messageIdElem.GetUInt64();
                                            }
                                            else if (messageIdElem.ValueKind == JsonValueKind.String)
                                            {
                                                string messageIdStr = messageIdElem.GetString() ?? "0";
                                                if (ulong.TryParse(messageIdStr, out ulong messageId))
                                                {
                                                    signup.MessageId = messageId;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            Console.WriteLine($"Failed to parse MessageId for signup '{signup.Name}'");
                                        }
                                    }

                                    // Try to extract Creator info
                                    if (element.TryGetProperty("CreatedById", out var createdByIdElem))
                                    {
                                        try
                                        {
                                            if (createdByIdElem.ValueKind == JsonValueKind.Number)
                                            {
                                                signup.CreatorId = createdByIdElem.GetUInt64();
                                            }
                                            else if (createdByIdElem.ValueKind == JsonValueKind.String)
                                            {
                                                string creatorIdStr = createdByIdElem.GetString() ?? "0";
                                                if (ulong.TryParse(creatorIdStr, out ulong creatorId))
                                                {
                                                    signup.CreatorId = creatorId;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            Console.WriteLine($"Failed to parse CreatedById for signup '{signup.Name}'");
                                        }
                                    }

                                    if (element.TryGetProperty("CreatedByUsername", out var createdByUsernameElem) &&
                                        createdByUsernameElem.ValueKind == JsonValueKind.String)
                                    {
                                        signup.CreatorUsername = createdByUsernameElem.GetString() ?? "Unknown";
                                    }

                                    // Handle participants - use normal arrays now
                                    if (element.TryGetProperty("Participants", out var participantsElement) &&
                                        participantsElement.ValueKind == JsonValueKind.Array)
                                    {
                                        // Create a list to store participant info until we can convert them to DiscordMembers
                                        var participantInfos = new List<(ulong Id, string Username)>();

                                        foreach (var participant in participantsElement.EnumerateArray())
                                        {
                                            try
                                            {
                                                ulong id = 0;
                                                string username = "Unknown";

                                                // Get ID - can be number or string
                                                if (participant.TryGetProperty("Id", out var idElement))
                                                {
                                                    if (idElement.ValueKind == JsonValueKind.Number)
                                                    {
                                                        id = idElement.GetUInt64();
                                                    }
                                                    else if (idElement.ValueKind == JsonValueKind.String)
                                                    {
                                                        string idStr = idElement.GetString() ?? "0";
                                                        if (!ulong.TryParse(idStr, out id))
                                                        {
                                                            Console.WriteLine($"Failed to parse participant ID '{idStr}' for signup '{signup.Name}'");
                                                        }
                                                    }
                                                }

                                                // Get Username - should be string
                                                if (participant.TryGetProperty("Username", out var usernameElement) &&
                                                    usernameElement.ValueKind == JsonValueKind.String)
                                                {
                                                    username = usernameElement.GetString() ?? "Unknown";
                                                }

                                                if (id > 0)
                                                {
                                                    participantInfos.Add((id, username));
                                                }
                                            }
                                            catch (Exception partEx)
                                            {
                                                Console.WriteLine($"Error parsing participant: {partEx.Message}");
                                            }
                                        }

                                        // Store the participant info for later conversion
                                        signup.ParticipantInfo = participantInfos;
                                        Console.WriteLine($"Loaded {participantInfos.Count} participant infos for signup '{signup.Name}'");
                                    }

                                    signups.Add(signup);
                                    Console.WriteLine($"Successfully loaded signup '{signup.Name}'");
                                }
                                catch (Exception signupEx)
                                {
                                    Console.WriteLine($"Error parsing signup: {signupEx.Message}");
                                }
                            }

                            Console.WriteLine($"Loaded {signups.Count} signups from file");
                            _ongoingRounds.TournamentSignups = signups;
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Console.WriteLine($"Error parsing signups file: {parseEx.Message}");
                        throw;
                    }
                }
                else
                {
                    Console.WriteLine("No signups file found, creating new empty list");
                    _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
                    SaveSignupsToFile();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading signups from file: {ex.Message}");
                // If we can't load, create a new empty collection
                _ongoingRounds.TournamentSignups = new List<TournamentSignup>();
                SaveSignupsToFile();
            }
        }

        // Save tournaments to file
        private void SaveTournamentsToFile()
        {
            try
            {
                // Ensure the data directory exists
                Directory.CreateDirectory(_dataDirectory);

                // Create sanitized copies of tournaments without Discord objects
                List<Tournament> sanitizedTournaments = new List<Tournament>();
                foreach (var tournament in _ongoingRounds.Tournaments)
                {
                    // Create a clean copy without Discord objects
                    Tournament cleanTournament = CleanTournamentForSerialization(tournament);
                    sanitizedTournaments.Add(cleanTournament);
                }

                // Option 1: Create a specific format with proper JSON structure
                var jsonObject = new
                {
                    Id = "1",
                    Values = sanitizedTournaments
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve
                };

                // Manually construct the JSON to ensure exact format
                // This ensures a single level of $values 
                var json = JsonSerializer.Serialize(jsonObject, options);

                // Replace the property name to match expected format
                json = json.Replace("\"Id\"", "\"$id\"").Replace("\"Values\"", "\"$values\"");

                // Write to the file
                File.WriteAllText(_tournamentsFilePath, json);
                Console.WriteLine($"Saved {_ongoingRounds.Tournaments.Count} tournaments to {_tournamentsFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tournaments to file: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Creates a clean copy of a tournament without Discord objects for serialization
        /// </summary>
        private Tournament CleanTournamentForSerialization(Tournament tournament)
        {
            var cleanTournament = new Tournament
            {
                Name = tournament.Name,
                Format = tournament.Format,
                CurrentStage = tournament.CurrentStage,
                MatchesPerPlayer = tournament.MatchesPerPlayer,
                IsComplete = tournament.IsComplete,
                // Don't include AnnouncementChannel which is a Discord object
            };

            // Copy groups without Discord objects
            cleanTournament.Groups = new List<Tournament.Group>();
            foreach (var group in tournament.Groups)
            {
                var cleanGroup = new Tournament.Group
                {
                    Name = group.Name,
                    IsComplete = group.IsComplete
                };

                // Copy participants
                cleanGroup.Participants = new List<Tournament.GroupParticipant>();
                foreach (var participant in group.Participants)
                {
                    var cleanParticipant = new Tournament.GroupParticipant
                    {
                        Wins = participant.Wins,
                        Draws = participant.Draws,
                        Losses = participant.Losses,
                        GamesWon = participant.GamesWon,
                        GamesLost = participant.GamesLost,
                        AdvancedToPlayoffs = participant.AdvancedToPlayoffs
                    };

                    // Convert Discord player to simple string representation
                    if (participant.Player is DSharpPlus.Entities.DiscordMember discordMember)
                    {
                        // Store player data in a simple serializable format
                        cleanParticipant.Player = new
                        {
                            Id = discordMember.Id,
                            Username = discordMember.Username,
                            DisplayName = discordMember.DisplayName
                        };
                    }
                    else if (participant.Player != null)
                    {
                        // For other player types, store as string
                        cleanParticipant.Player = participant.Player.ToString();
                    }

                    cleanGroup.Participants.Add(cleanParticipant);
                }

                // Copy matches
                cleanGroup.Matches = new List<Tournament.Match>();
                foreach (var match in group.Matches)
                {
                    cleanGroup.Matches.Add(CleanMatchForSerialization(match));
                }

                cleanTournament.Groups.Add(cleanGroup);
            }

            // Copy playoff matches
            cleanTournament.PlayoffMatches = new List<Tournament.Match>();
            foreach (var match in tournament.PlayoffMatches)
            {
                cleanTournament.PlayoffMatches.Add(CleanMatchForSerialization(match));
            }

            // Copy related messages without Discord references
            if (tournament.RelatedMessages != null)
            {
                cleanTournament.RelatedMessages = new List<RelatedMessage>();
                foreach (var msg in tournament.RelatedMessages)
                {
                    cleanTournament.RelatedMessages.Add(new RelatedMessage
                    {
                        ChannelId = msg.ChannelId,
                        MessageId = msg.MessageId,
                        Type = msg.Type
                    });
                }
            }

            return cleanTournament;
        }

        /// <summary>
        /// Creates a clean copy of a match without Discord objects for serialization
        /// </summary>
        private Tournament.Match CleanMatchForSerialization(Tournament.Match match)
        {
            var cleanMatch = new Tournament.Match
            {
                Name = match.Name,
                Type = match.Type,
                BestOf = match.BestOf,
                DisplayPosition = match.DisplayPosition
            };

            // Copy participants
            cleanMatch.Participants = new List<Tournament.MatchParticipant>();
            foreach (var participant in match.Participants)
            {
                var cleanParticipant = new Tournament.MatchParticipant
                {
                    Score = participant.Score,
                    IsWinner = participant.IsWinner,
                    SourceGroupPosition = participant.SourceGroupPosition
                };

                // Handle SourceGroup reference if not null
                if (participant.SourceGroup != null)
                {
                    cleanParticipant.SourceGroup = new Tournament.Group
                    {
                        Name = participant.SourceGroup.Name,
                        IsComplete = participant.SourceGroup.IsComplete
                    };
                }

                // Convert Discord player to simple string representation
                if (participant.Player is DSharpPlus.Entities.DiscordMember discordMember)
                {
                    // Store player data in a simple serializable format
                    cleanParticipant.Player = new
                    {
                        Id = discordMember.Id,
                        Username = discordMember.Username,
                        DisplayName = discordMember.DisplayName
                    };
                }
                else if (participant.Player != null)
                {
                    // For other player types, store as string
                    cleanParticipant.Player = participant.Player.ToString();
                }

                cleanMatch.Participants.Add(cleanParticipant);
            }

            // Handle result if exists
            if (match.Result != null)
            {
                cleanMatch.Result = new Tournament.MatchResult
                {
                    MapResults = match.Result.MapResults != null ? new List<string>(match.Result.MapResults) : new List<string>(),
                    CompletedAt = match.Result.CompletedAt,
                    DeckCodes = match.Result.DeckCodes != null
                        ? new Dictionary<string, Dictionary<string, string>>(match.Result.DeckCodes)
                        : new Dictionary<string, Dictionary<string, string>>()
                };

                // Convert Discord winner to simple string representation
                if (match.Result.Winner is DSharpPlus.Entities.DiscordMember discordMember)
                {
                    // Store winner data in a simple serializable format
                    cleanMatch.Result.Winner = new
                    {
                        Id = discordMember.Id,
                        Username = discordMember.Username,
                        DisplayName = discordMember.DisplayName
                    };
                }
                else if (match.Result.Winner != null)
                {
                    // For other winner types, store as string
                    cleanMatch.Result.Winner = match.Result.Winner.ToString();
                }
            }

            // We don't copy LinkedRound or NextMatch as those will be handled separately
            // in the tournament state serialization

            return cleanMatch;
        }

        // Save signups to file
        private void SaveSignupsToFile()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                // Create a list to store simplified signup objects
                var signupsToSave = new List<object>();

                foreach (var signup in _ongoingRounds.TournamentSignups)
                {
                    // Create a list of simplified participant data
                    var participantsList = signup.Participants.Select(p => new
                    {
                        Id = p.Id,
                        Username = p.Username
                    }).ToList();

                    // Prepare seed information for serialization
                    var seedList = new List<(ulong Id, int Seed)>();
                    if (signup.Seeds != null && signup.Seeds.Any())
                    {
                        foreach (var seed in signup.Seeds)
                        {
                            ulong playerId = seed.PlayerId != 0 ? seed.PlayerId : seed.Player?.Id ?? 0;
                            if (playerId != 0)
                            {
                                seedList.Add((playerId, seed.Seed));
                            }
                        }
                    }

                    // Create a simplified object for serialization
                    var signupData = new
                    {
                        signup.Name,
                        signup.IsOpen,
                        signup.CreatedAt,
                        signup.Format,
                        signup.Type,
                        signup.ScheduledStartTime,
                        signup.SignupChannelId,
                        signup.MessageId,
                        CreatedById = signup.CreatedBy?.Id ?? signup.CreatorId,
                        CreatedByUsername = signup.CreatedBy?.Username ?? signup.CreatorUsername,
                        Participants = participantsList,
                        Seeds = seedList,
                        signup.RelatedMessages
                    };

                    signupsToSave.Add(signupData);

                    // Update the participant info for when we load
                    signup.ParticipantInfo = participantsList.Select(p => (p.Id, p.Username)).ToList();
                }

                // Serialize the list directly
                string json = JsonSerializer.Serialize(signupsToSave, options);
                File.WriteAllText(_signupsFilePath, json);
                Console.WriteLine($"Saved {_ongoingRounds.TournamentSignups.Count} signups to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving signups to file: {ex.Message}");
            }
        }

        // Helper methods for handling object/DiscordMember properties
        private string GetPlayerDisplayName(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.DisplayName;
            return player?.ToString() ?? "Unknown";
        }

        private ulong? GetPlayerId(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.Id;
            return null;
        }

        private string GetPlayerMention(object? player)
        {
            if (player is DSharpPlus.Entities.DiscordMember member)
                return member.Mention;
            return GetPlayerDisplayName(player);
        }

        private bool ComparePlayerIds(object? player1, object? player2)
        {
            // If both are DiscordMember, compare IDs
            if (player1 is DSharpPlus.Entities.DiscordMember member1 &&
                player2 is DSharpPlus.Entities.DiscordMember member2)
                return member1.Id == member2.Id;

            // Otherwise compare by reference or ToString
            if (ReferenceEquals(player1, player2))
                return true;

            return player1?.ToString() == player2?.ToString();
        }

        private DSharpPlus.Entities.DiscordMember? ConvertToDiscordMember(object? player)
        {
            return player as DSharpPlus.Entities.DiscordMember;
        }

        public async Task<Tournament> CreateTournament(string name, List<DiscordMember> players, TournamentFormat format, DiscordChannel announcementChannel, GameType gameType = GameType.OneVsOne, Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            // Create a new tournament
            var tournament = new Tournament
            {
                Name = name,
                Format = format,
                GameType = gameType,
                AnnouncementChannel = announcementChannel
            };

            // Determine group count based on player count and format
            int groupCount = DetermineGroupCount(players.Count, format);

            // Create groups and distribute players with seeding if available
            CreateGroups(tournament, players, groupCount, playerSeeds);

            // Add to ongoing tournaments
            _ongoingRounds.Tournaments.Add(tournament);

            // Save all data immediately
            await SaveAllData();

            return tournament;
        }

        private void CreateGroups(Tournament tournament, List<DiscordMember> players, int groupCount, Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            // Create the groups
            for (int i = 0; i < groupCount; i++)
            {
                var group = new Tournament.Group { Name = $"Group {(char)('A' + i)}" };
                tournament.Groups.Add(group);
            }

            // Check if any players have predefined seeds
            var effectivePlayerSeeds = playerSeeds ?? new Dictionary<DiscordMember, int>();

            // Create a list to hold players with their seeds
            var playersWithSeeds = players
                .Select(p => new
                {
                    Player = p,
                    Seed = effectivePlayerSeeds.ContainsKey(p) ? effectivePlayerSeeds[p] : 0
                })
                .ToList();

            // Distribute players based on seeding
            if (playersWithSeeds.Any(p => p.Seed > 0))
            {
                // Sort players: seeded first (by seed), then rest randomly
                var seededPlayers = playersWithSeeds
                    .Where(p => p.Seed > 0)
                    .OrderBy(p => p.Seed)
                    .ToList();

                var unseededPlayers = playersWithSeeds
                    .Where(p => p.Seed == 0)
                    .OrderBy(_ => Guid.NewGuid())
                    .ToList();

                Console.WriteLine($"Creating tournament with {seededPlayers.Count} seeded players and {unseededPlayers.Count} unseeded players");

                // Use snake pattern for seeded players
                bool reverseDirection = false;
                int currentGroup = 0;

                // Add seeded players using snake draft
                foreach (var player in seededPlayers)
                {
                    var group = tournament.Groups[currentGroup];
                    var participant = new Tournament.GroupParticipant
                    {
                        Player = player.Player,
                        Seed = player.Seed
                    };
                    group.Participants.Add(participant);

                    // Move to next group using snake draft pattern
                    if (!reverseDirection)
                    {
                        currentGroup++;
                        if (currentGroup >= groupCount)
                        {
                            currentGroup = groupCount - 1;
                            reverseDirection = true;
                        }
                    }
                    else
                    {
                        currentGroup--;
                        if (currentGroup < 0)
                        {
                            currentGroup = 0;
                            reverseDirection = false;
                        }
                    }
                }

                // Distribute unseeded players evenly to balance group sizes
                foreach (var player in unseededPlayers)
                {
                    // Find the group with the fewest players
                    var group = tournament.Groups.OrderBy(g => g.Participants.Count).First();
                    var participant = new Tournament.GroupParticipant
                    {
                        Player = player.Player,
                        Seed = player.Seed
                    };
                    group.Participants.Add(participant);
                }
            }
            else
            {
                // No seeds, distribute players randomly
                var shuffledPlayers = players.OrderBy(_ => Guid.NewGuid()).ToList();
                for (int i = 0; i < shuffledPlayers.Count; i++)
                {
                    var groupIndex = i % groupCount;
                    var group = tournament.Groups[groupIndex];

                    var participant = new Tournament.GroupParticipant { Player = shuffledPlayers[i] };
                    group.Participants.Add(participant);
                }
            }

            // Create the matches within each group
            foreach (var group in tournament.Groups)
            {
                // Create round-robin matches
                for (int i = 0; i < group.Participants.Count; i++)
                {
                    for (int j = i + 1; j < group.Participants.Count; j++)
                    {
                        var match = new Tournament.Match
                        {
                            Name = $"{GetPlayerDisplayName(group.Participants[i].Player)} vs {GetPlayerDisplayName(group.Participants[j].Player)}",
                            Type = MatchType.GroupStage,
                            Participants = new List<Tournament.MatchParticipant>
                            {
                                new() { Player = group.Participants[i].Player },
                                new() { Player = group.Participants[j].Player }
                            }
                        };
                        group.Matches.Add(match);
                    }
                }
            }
        }

        public Tournament? GetTournament(string name)
        {
            return _ongoingRounds.Tournaments.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public List<Tournament> GetAllTournaments()
        {
            return _ongoingRounds.Tournaments;
        }

        public async Task DeleteTournament(string name, DiscordClient? client = null)
        {
            var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (tournament != null)
            {
                // First delete all related messages if a client is provided
                if (client != null && tournament.RelatedMessages != null)
                {
                    foreach (var relatedMessage in tournament.RelatedMessages)
                    {
                        try
                        {
                            var channel = await client.GetChannelAsync(relatedMessage.ChannelId);
                            if (channel is not null)
                            {
                                var message = await channel.GetMessageAsync(relatedMessage.MessageId);
                                if (message is not null)
                                {
                                    await message.DeleteAsync();
                                    Console.WriteLine($"Deleted related message of type {relatedMessage.Type} for tournament {name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting related message: {ex.Message}");
                        }
                    }
                }

                // Remove from active tournaments
                if (_ongoingRounds.Tournaments != null)
                {
                    _ongoingRounds.Tournaments.Remove(tournament);
                }

                // Save tournaments to file
                SaveTournamentsToFile();
                Console.WriteLine($"Tournament '{name}' deleted");
            }
            else
            {
                Console.WriteLine($"Tournament '{name}' not found");
            }
        }

        public void UpdateMatchResult(Tournament tournament, Tournament.Match match, DiscordMember winner, int winnerScore, int loserScore)
        {
            // Early return if any parameter is null
            if (tournament is null || match is null || winner is null || match.Participants is null)
                return;

            // Use simple reference equality on IDs
            ulong mainWinnerId = winner.Id;  // Store ID once

            Tournament.MatchParticipant? winnerParticipant = null;
            Tournament.MatchParticipant? loserParticipant = null;

            // Find the winner first
            foreach (var p in match.Participants)
            {
                if (p is null || p.Player is null)
                    continue;

                var playerId = GetPlayerId(p.Player);
                if (playerId.HasValue && playerId.Value == mainWinnerId)
                {
                    winnerParticipant = p;
                    break;
                }
            }

            // Then find the non-winner
            if (winnerParticipant != null)
            {
                foreach (var p in match.Participants)
                {
                    if (p is null || p.Player is null || ReferenceEquals(p, winnerParticipant))
                        continue;

                    loserParticipant = p;
                    break;
                }
            }

            if (winnerParticipant == null || loserParticipant == null)
                return;

            winnerParticipant.Score = winnerScore;
            winnerParticipant.IsWinner = true;
            loserParticipant.Score = loserScore;

            match.Result = new Tournament.MatchResult
            {
                Winner = winner,
                CompletedAt = DateTime.Now,
                DeckCodes = new Dictionary<string, Dictionary<string, string>>()
            };

            // Store deck codes from the linked round if available
            if (match.LinkedRound != null && match.LinkedRound.Teams != null)
            {
                // Get maps from the round
                var maps = match.LinkedRound.Maps;

                // For each team and participant
                foreach (var team in match.LinkedRound.Teams)
                {
                    if (team?.Participants != null)
                    {
                        foreach (var participant in team.Participants)
                        {
                            if (participant?.Player is not null)
                            {
                                var playerId = GetPlayerId(participant.Player);
                                if (playerId.HasValue)
                                {
                                    string playerIdStr = playerId.Value.ToString();

                                    // Initialize the player's deck code dictionary if needed
                                    if (!match.Result.DeckCodes.ContainsKey(playerIdStr))
                                    {
                                        match.Result.DeckCodes[playerIdStr] = new Dictionary<string, string>();
                                    }

                                    // If we have a current deck code, associate it with the current map
                                    if (!string.IsNullOrEmpty(participant.Deck) && maps != null && maps.Count > 0)
                                    {
                                        // Get the current map based on the cycle
                                        int mapIndex = Math.Min(match.LinkedRound.Cycle, maps.Count - 1);
                                        if (mapIndex >= 0 && mapIndex < maps.Count)
                                        {
                                            string currentMap = maps[mapIndex];
                                            match.Result.DeckCodes[playerIdStr][currentMap] = participant.Deck;
                                        }
                                    }

                                    // If we have deck history, add those as well
                                    if (participant.DeckHistory != null && participant.DeckHistory.Count > 0)
                                    {
                                        foreach (var deckEntry in participant.DeckHistory)
                                        {
                                            if (deckEntry.Key != null && !string.IsNullOrEmpty(deckEntry.Value))
                                            {
                                                match.Result.DeckCodes[playerIdStr][deckEntry.Key] = deckEntry.Value;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Update group standings if it's a group stage match
            if (match.Type == MatchType.GroupStage && tournament.Groups != null)
            {
                // Store ID once
                ulong? groupWinnerIdNullable = GetPlayerId(winner);
                if (!groupWinnerIdNullable.HasValue)
                {
                    Console.WriteLine("Error: Winner has no valid ID");
                    return;
                }
                ulong groupWinnerId = groupWinnerIdNullable.Value;
                ulong? groupLoserId = loserParticipant?.Player != null ? GetPlayerId(loserParticipant.Player) : null;

                foreach (var group in tournament.Groups)
                {
                    if (group?.Matches == null)
                        continue;

                    bool containsMatch = false;
                    foreach (var m in group.Matches)
                    {
                        if (m == match)
                        {
                            containsMatch = true;
                            break;
                        }
                    }

                    if (containsMatch && group.Participants != null)
                    {
                        // Update participant stats without using DiscordMember comparisons
                        Tournament.GroupParticipant? groupWinner = null;
                        Tournament.GroupParticipant? groupLoser = null;

                        // Find the winner
                        foreach (var p in group.Participants)
                        {
                            if (p is null || p.Player is null)
                                continue;

                            if (p.Player is null)
                                continue;

                            var pPlayerId = GetPlayerId(p.Player);
                            if (pPlayerId.HasValue && pPlayerId.Value == groupWinnerId)
                            {
                                groupWinner = p;
                                break;
                            }
                        }

                        // Find the loser
                        if (groupLoserId.HasValue)
                        {
                            foreach (var p in group.Participants)
                            {
                                if (p is null || p.Player is null)
                                    continue;

                                if (p.Player is null)
                                    continue;

                                var pPlayerId = GetPlayerId(p.Player);
                                if (pPlayerId.HasValue && pPlayerId.Value == groupLoserId.Value)
                                {
                                    groupLoser = p;
                                    break;
                                }
                            }
                        }

                        if (groupWinner != null)
                        {
                            groupWinner.Wins++;
                            groupWinner.GamesWon += winnerScore;
                            groupWinner.GamesLost += loserScore;
                        }

                        if (groupLoser != null)
                        {
                            groupLoser.Losses++;
                            groupLoser.GamesWon += loserScore;
                            groupLoser.GamesLost += winnerScore;
                        }

                        // Check if group is complete
                        CheckGroupCompletion(group);
                        break;
                    }
                }
            }

            // Check if all groups are complete to begin playoffs
            bool allGroupsComplete = true;
            if (tournament.Groups != null)
            {
                foreach (var g in tournament.Groups)
                {
                    if (g != null && !g.IsComplete)
                    {
                        allGroupsComplete = false;
                        break;
                    }
                }

                if (tournament.CurrentStage == TournamentStage.Groups && allGroupsComplete)
                {
                    SetupPlayoffs(tournament);
                    tournament.CurrentStage = TournamentStage.Playoffs;
                }
            }

            // Check if tournament is complete
            bool allPlayoffsComplete = true;
            if (tournament.PlayoffMatches != null)
            {
                foreach (var m in tournament.PlayoffMatches)
                {
                    if (m != null && !m.IsComplete)
                    {
                        allPlayoffsComplete = false;
                        break;
                    }
                }

                if (tournament.CurrentStage == TournamentStage.Playoffs && allPlayoffsComplete)
                {
                    tournament.CurrentStage = TournamentStage.Complete;
                    tournament.IsComplete = true;
                }
            }

            // Save tournaments to file
            SaveTournamentsToFile();

            // NOTE: Tournament state should be saved in an async context
            // If this method is made async in the future, use: await SaveTournamentState();
            // For now, we rely on the periodic state saves elsewhere in the code
        }

        private void CheckGroupCompletion(Tournament.Group group)
        {
            if (group.Matches?.All(m => m?.IsComplete == true) == true)
            {
                group.IsComplete = true;
            }
        }

        private void SetupPlayoffs(Tournament tournament)
        {
            // Sort participants in each group by points (and tiebreakers if needed)
            foreach (var group in tournament.Groups)
            {
                var sortedParticipants = group.Participants?
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GamesWon)
                    .ThenBy(p => p.GamesLost)
                    .ToList() ?? [];

                // Mark top 2 as advancing to playoffs
                for (int i = 0; i < Math.Min(2, sortedParticipants.Count); i++)
                {
                    sortedParticipants[i].AdvancedToPlayoffs = true;
                }
            }

            // Create semifinal matches
            if (tournament.Groups.Count >= 2)
            {
                var groupA = tournament.Groups[0];
                var groupB = tournament.Groups[1];

                // Special case for 9 players (3 groups of 3) - only best second-place advances
                Tournament.GroupParticipant? bestSecondPlace = null;
                if (tournament.Groups.Count == 3)
                {
                    var secondPlaceFinishers = tournament.Groups
                        .Select(g => g.Participants?.OrderByDescending(p => p.Points)
                                                 .ThenByDescending(p => p.GamesWon)
                                                 .ThenBy(p => p.GamesLost)
                                                 .Skip(1)
                                                 .FirstOrDefault())
                        .Where(p => p != null)
                        .ToList();

                    bestSecondPlace = secondPlaceFinishers
                        .OrderByDescending(p => p!.Points)
                        .ThenByDescending(p => p!.GamesWon)
                        .ThenBy(p => p!.GamesLost)
                        .FirstOrDefault();
                }

                // Create semifinal 1: Group A #1 vs Group B #2 (or best second place)
                var semi1 = new Tournament.Match
                {
                    Name = "Semifinal 1",
                    Type = MatchType.Semifinal,
                    DisplayPosition = "Semifinal 1",
                    BestOf = 3,
                    Participants = new List<Tournament.MatchParticipant>()
                };

                // Group A #1
                var groupA1 = groupA.Participants?.OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GamesWon)
                    .FirstOrDefault();

                if (groupA1 != null)
                {
                    semi1.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = groupA1.Player,
                        SourceGroup = groupA,
                        SourceGroupPosition = 1
                    });
                }

                // Group B #2 or best second place
                if (tournament.Groups.Count == 2)
                {
                    var groupB2 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .Skip(1)
                        .FirstOrDefault();

                    if (groupB2 != null)
                    {
                        semi1.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB2.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 2
                        });
                    }
                }
                else if (bestSecondPlace != null)
                {
                    // Find the group this participant belongs to
                    var sourceGroup = tournament.Groups.FirstOrDefault(g => g.Participants?.Contains(bestSecondPlace) == true);

                    semi1.Participants.Add(new Tournament.MatchParticipant
                    {
                        Player = bestSecondPlace.Player,
                        SourceGroup = sourceGroup,
                        SourceGroupPosition = 2
                    });
                }

                tournament.PlayoffMatches.Add(semi1);

                // Create semifinal 2: Group B #1 vs Group A #2 (for 2 groups)
                if (tournament.Groups.Count == 2)
                {
                    var semi2 = new Tournament.Match
                    {
                        Name = "Semifinal 2",
                        Type = MatchType.Semifinal,
                        DisplayPosition = "Semifinal 2",
                        BestOf = 3,
                        Participants = new List<Tournament.MatchParticipant>()
                    };

                    // Group B #1
                    var groupB1 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupB1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB1.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 1
                        });
                    }

                    // Group A #2
                    var groupA2 = groupA.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .Skip(1)
                        .FirstOrDefault();

                    if (groupA2 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupA2.Player,
                            SourceGroup = groupA,
                            SourceGroupPosition = 2
                        });
                    }

                    tournament.PlayoffMatches.Add(semi2);
                }
                else if (tournament.Groups.Count >= 3)
                {
                    var groupC = tournament.Groups[2];

                    // In 3 groups case, top from Group B vs top from Group C
                    var semi2 = new Tournament.Match
                    {
                        Name = "Semifinal 2",
                        Type = MatchType.Semifinal,
                        DisplayPosition = "Semifinal 2",
                        BestOf = 3,
                        Participants = new List<Tournament.MatchParticipant>()
                    };

                    // Group B #1
                    var groupB1 = groupB.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupB1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupB1.Player,
                            SourceGroup = groupB,
                            SourceGroupPosition = 1
                        });
                    }

                    // Group C #1
                    var groupC1 = groupC.Participants?.OrderByDescending(p => p.Points)
                        .ThenByDescending(p => p.GamesWon)
                        .FirstOrDefault();

                    if (groupC1 != null)
                    {
                        semi2.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = groupC1.Player,
                            SourceGroup = groupC,
                            SourceGroupPosition = 1
                        });
                    }

                    tournament.PlayoffMatches.Add(semi2);
                }

                // Create final match
                var final = new Tournament.Match
                {
                    Name = "Final",
                    Type = MatchType.Final,
                    DisplayPosition = "Final",
                    BestOf = 3,
                    Participants = new List<Tournament.MatchParticipant>()
                };

                // Link semifinals to finals
                if (tournament.PlayoffMatches.Count >= 2)
                {
                    tournament.PlayoffMatches[0].NextMatch = final;
                    tournament.PlayoffMatches[1].NextMatch = final;
                }

                tournament.PlayoffMatches.Add(final);
            }
        }

        public Task StartMatchRound(Tournament tournament, Tournament.Match match, DiscordChannel channel)
        {
            // Early return if any parameter is null
            if (tournament is null || match is null || channel is null)
                return Task.CompletedTask;

            // Check that we have exactly 2 participants with valid players
            if (match.Participants?.Count != 2)
                return Task.CompletedTask;

            // Safe access to player objects
            var player1 = match.Participants[0]?.Player;
            var player2 = match.Participants[1]?.Player;

            if (player1 is null || player2 is null)
                return Task.CompletedTask;

            var round = new Round
            {
                Name = match.Name,
                Length = match.BestOf,
                OneVOne = true,
                Teams = new List<Round.Team>(),
                Pings = $"{GetPlayerMention(player1)} {GetPlayerMention(player2)}"
            };

            // Create teams (in this case individual players)
            foreach (var participant in match.Participants)
            {
                if (participant is not null && participant.Player is not null)
                {
                    var team = new Round.Team { Name = GetPlayerDisplayName(participant.Player) };
                    var discordMember = ConvertToDiscordMember(participant.Player);
                    if (discordMember is not null)
                    {
                        var roundParticipant = new Round.Participant { Player = discordMember };
                        team.Participants.Add(roundParticipant);
                    }
                    else
                    {
                        // Fallback for non-DiscordMember players in test scenarios
                        Console.WriteLine("[DEBUG] Skipping non-DiscordMember player in round creation");
                    }
                    round.Teams.Add(team);
                }
            }

            match.LinkedRound = round;
            _ongoingRounds.TourneyRounds.Add(round);

            // Return completed task since no async operations are performed
            return Task.CompletedTask;
        }

        public void UpdateTournamentFromRound(Tournament tournament)
        {
            // Look for completed rounds that are linked to tournament matches
            if (tournament.PlayoffMatches == null)
                return;

            foreach (var playoffMatch in tournament.PlayoffMatches)
            {
                if (playoffMatch?.LinkedRound == null || playoffMatch.IsComplete)
                    continue;

                var round = playoffMatch.LinkedRound;

                if (round.Teams == null || round.Teams.Count < 2)
                    continue;

                // Check if we have a winner
                var winningTeam = round.Teams.FirstOrDefault(t => t.Wins > round.Length / 2);
                if (winningTeam is null)
                    continue;

                var losingTeam = round.Teams.FirstOrDefault(t => !ReferenceEquals(t, winningTeam));

                // Get the winner member
                var winner = winningTeam.Participants?.FirstOrDefault()?.Player;
                if (winner is null)
                    continue;

                // Store ID once
                ulong playoffWinnerId = winner.Id;

                // Update match result    
                UpdateMatchResult(
                    tournament,
                    playoffMatch,
                    winner,
                    winningTeam.Wins,
                    losingTeam?.Wins ?? 0);

                // If this match has a next match (e.g., semifinal -> final)
                if (playoffMatch.NextMatch != null && playoffMatch.Participants != null)
                {
                    var nextMatch = playoffMatch.NextMatch;

                    // Find the winner's participant without using complex comparisons
                    Tournament.MatchParticipant? winnerParticipant = null;
                    foreach (var p in playoffMatch.Participants)
                    {
                        if (p is null || p.Player is null)
                            continue;

                        var playerIdValue = GetPlayerId(p.Player);
                        if (playerIdValue.HasValue && playerIdValue.Value == playoffWinnerId)
                        {
                            winnerParticipant = p;
                            break;
                        }
                    }

                    if (winnerParticipant != null && nextMatch.Participants != null)
                    {
                        nextMatch.Participants.Add(new Tournament.MatchParticipant
                        {
                            Player = winner,
                            SourceGroup = winnerParticipant.SourceGroup,
                            SourceGroupPosition = winnerParticipant.SourceGroupPosition
                        });
                    }
                }
            }
        }

        private int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // Determine appropriate group count based on player count and format
            if (format != TournamentFormat.GroupStageWithPlayoffs)
            {
                return 1; // Single group for non-group formats
            }

            switch (playerCount)
            {
                case <= 8:
                    // Create 2 groups of 4 or less
                    return 2;
                case 9:
                    // Create 3 groups of 3
                    return 3;
                case <= 10:
                    // Create 2 groups of 5
                    return 2;
                default:
                    // For 11+ players, adjust as needed
                    return (playerCount / 4) + (playerCount % 4 > 0 ? 1 : 0);
            }
        }

        // Add a tournament signup management method
        public TournamentSignup CreateSignup(string name, TournamentFormat format, DiscordUser creator, ulong signupChannelId, GameType gameType = GameType.OneVsOne, DateTime? scheduledStartTime = null)
        {
            var signup = new TournamentSignup
            {
                Name = name,
                Format = format,
                Type = gameType,
                CreatedAt = DateTime.Now,
                CreatedBy = creator,
                CreatorId = creator?.Id ?? 0,
                CreatorUsername = creator?.Username ?? "Unknown",
                SignupChannelId = signupChannelId,
                ScheduledStartTime = scheduledStartTime,
                IsOpen = true
            };

            _ongoingRounds.TournamentSignups.Add(signup);
            SaveSignupsToFile();

            Console.WriteLine($"Created tournament signup '{name}' with game type {gameType}");
            return signup;
        }

        // Methods for managing signups
        public TournamentSignup? GetSignup(string name)
        {
            var signup = _ongoingRounds.TournamentSignups.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            return signup;
        }

        public List<TournamentSignup> GetAllSignups()
        {
            // No need to modify the Participants collection
            // Just ensure the TournamentSignup objects have their ParticipantInfo loaded
            return _ongoingRounds.TournamentSignups;
        }

        // Add a helper method to get the effective participant count
        public int GetParticipantCount(TournamentSignup signup)
        {
            if (signup.Participants != null && signup.Participants.Count > 0)
                return signup.Participants.Count;

            if (signup.ParticipantInfo != null && signup.ParticipantInfo.Count > 0)
                return signup.ParticipantInfo.Count;

            return 0;
        }

        public async Task DeleteSignup(string name, DiscordClient? client = null)
        {
            // Load signups if not already loaded
            if (_ongoingRounds.TournamentSignups == null)
            {
                LoadSignupsFromFile();
            }

            var signup = _ongoingRounds.TournamentSignups?.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (signup != null)
            {
                // First delete the main signup message
                if (client != null && signup.SignupChannelId != 0 && signup.MessageId != 0)
                {
                    try
                    {
                        var channel = await client.GetChannelAsync(signup.SignupChannelId);
                        if (channel is not null)
                        {
                            var message = await channel.GetMessageAsync(signup.MessageId);
                            if (message is not null)
                            {
                                await message.DeleteAsync();
                                Console.WriteLine($"Deleted main signup message for {name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting main signup message: {ex.Message}");
                    }
                }

                // Then delete all related messages
                if (client != null && signup.RelatedMessages != null)
                {
                    foreach (var relatedMessage in signup.RelatedMessages)
                    {
                        try
                        {
                            var channel = await client.GetChannelAsync(relatedMessage.ChannelId);
                            if (channel is not null)
                            {
                                var message = await channel.GetMessageAsync(relatedMessage.MessageId);
                                if (message is not null)
                                {
                                    await message.DeleteAsync();
                                    Console.WriteLine($"Deleted related message of type {relatedMessage.Type} for signup {name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting related signup message: {ex.Message}");
                        }
                    }
                }

                // Remove from signups collection
                if (_ongoingRounds.TournamentSignups != null)
                {
                    _ongoingRounds.TournamentSignups.Remove(signup);
                }

                // Save changes
                SaveSignupsToFile();
                Console.WriteLine($"Signup '{name}' deleted");
            }
            else
            {
                Console.WriteLine($"Signup '{name}' not found");
            }
        }

        public void UpdateSignup(TournamentSignup signup)
        {
            // Keep minimal logging
            Console.WriteLine($"Updating signup '{signup.Name}' with {signup.Participants.Count} participants");

            // Update the signup in the collection
            var existingIndex = _ongoingRounds.TournamentSignups.FindIndex(s =>
                s.Name.Equals(signup.Name, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                // Get the existing signup
                var existingSignup = _ongoingRounds.TournamentSignups[existingIndex];

                // Update properties but preserve participant list
                existingSignup.IsOpen = signup.IsOpen;
                existingSignup.SignupChannelId = signup.SignupChannelId;
                existingSignup.MessageId = signup.MessageId;
                existingSignup.ScheduledStartTime = signup.ScheduledStartTime;

                // Set creator info if it's not already set
                if (existingSignup.CreatorId == 0 && signup.CreatorId != 0)
                {
                    existingSignup.CreatorId = signup.CreatorId;
                }
                if (string.IsNullOrEmpty(existingSignup.CreatorUsername) && !string.IsNullOrEmpty(signup.CreatorUsername))
                {
                    existingSignup.CreatorUsername = signup.CreatorUsername;
                }

                // Create a merged participant list
                var allParticipants = new List<DiscordMember>(existingSignup.Participants);

                // Add any new participants not in the existing list
                foreach (var participant in signup.Participants)
                {
                    if (!allParticipants.Any(p => p.Id == participant.Id))
                    {
                        allParticipants.Add(participant);
                    }
                }

                // Update the participant list
                existingSignup.Participants = allParticipants;

                // Update the ParticipantInfo with the current state of Participants
                existingSignup.ParticipantInfo = existingSignup.Participants.Select(p => (p.Id, p.Username)).ToList();
            }
            else
            {
                // Shouldn't happen, but just in case
                Console.WriteLine($"WARNING: Signup '{signup.Name}' not found in collection, adding it");

                // Update the ParticipantInfo with the current state of Participants
                signup.ParticipantInfo = signup.Participants.Select(p => (p.Id, p.Username)).ToList();

                _ongoingRounds.TournamentSignups.Add(signup);
            }

            // Save the current state of signups
            SaveSignupsToFile();
        }

        // Save both data files whenever there's a significant change
        public async Task SaveAllData()
        {
            SaveTournamentsToFile();
            SaveSignupsToFile();
            await SaveTournamentState();
        }

        /// <summary>
        /// Repairs data files by checking for inconsistencies and fixing them
        /// </summary>
        public async Task RepairDataFiles(DiscordClient? client = null)
        {
            Console.WriteLine("Starting data file repair process...");

            try
            {
                // 0. First, make sure ParticipantInfo is preserved for all signups
                foreach (var signup in _ongoingRounds.TournamentSignups)
                {
                    // If Participants is empty but ParticipantInfo has data, restore from ParticipantInfo
                    if ((signup.Participants == null || signup.Participants.Count == 0) &&
                        signup.ParticipantInfo != null && signup.ParticipantInfo.Count > 0)
                    {
                        Console.WriteLine($"Signup '{signup.Name}' has {signup.ParticipantInfo.Count} participants in ParticipantInfo but no loaded Participants");

                        if (client != null)
                        {
                            // Force load participants from ParticipantInfo
                            await LoadParticipantsAsync(signup, client);
                            // Save immediately to preserve this data
                            UpdateSignup(signup);
                            Console.WriteLine($"Restored and saved {signup.Participants?.Count ?? 0} participants for signup '{signup.Name}'");
                        }
                    }
                    else
                    {
                        // Make sure ParticipantInfo is in sync with Participants
                        if (signup.Participants != null && signup.Participants.Count > 0)
                        {
                            if (signup.ParticipantInfo == null)
                                signup.ParticipantInfo = new List<(ulong Id, string Username)>();

                            // Check if any Participants aren't in ParticipantInfo
                            foreach (var participant in signup.Participants)
                            {
                                if (!signup.ParticipantInfo.Any(p => p.Id == participant.Id))
                                {
                                    // Add to ParticipantInfo
                                    signup.ParticipantInfo.Add((participant.Id, participant.Username));
                                    Console.WriteLine($"Added participant {participant.Username} to ParticipantInfo for signup '{signup.Name}'");
                                }
                            }

                            // Save the signup to persist changes
                            UpdateSignup(signup);
                        }
                    }
                }

                // Save all signups to ensure changes are persisted
                SaveSignupsToFile();

                // 1. Check for closed signups that should be tournaments
                var closedSignups = _ongoingRounds.TournamentSignups
                    .Where(s => !s.IsOpen)
                    .ToList();

                foreach (var signup in closedSignups)
                {
                    Console.WriteLine($"Checking closed signup '{signup.Name}' with {signup.ParticipantInfo.Count} participants in info and {signup.Participants.Count} loaded participants");

                    // Check if a tournament with this name already exists
                    if (!_ongoingRounds.Tournaments.Any(t => t.Name.Equals(signup.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine($"No matching tournament found for signup '{signup.Name}'");

                        // If we have a client, try to create a tournament from this signup
                        if (client != null)
                        {
                            try
                            {
                                // Ensure participants are loaded
                                if (signup.Participants.Count == 0 && signup.ParticipantInfo.Count > 0)
                                {
                                    await LoadParticipantsAsync(signup, client);
                                    Console.WriteLine($"Loaded {signup.Participants.Count} participants for signup '{signup.Name}'");
                                }

                                // Create a list of DiscordMembers from the participants
                                List<DiscordMember> players = new List<DiscordMember>();
                                foreach (var participant in signup.Participants)
                                {
                                    try
                                    {
                                        // Try to get each member - this might fail if they left the server
                                        if (participant.Id != 0)
                                        {
                                            var guild = await client.GetGuildAsync(client.Guilds.FirstOrDefault().Key);
                                            var member = await guild.GetMemberAsync(participant.Id);
                                            if (member is not null)
                                            {
                                                players.Add(member);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Could not load participant {participant.Username}: {ex.Message}");
                                    }
                                }

                                // Only create tournament if there are enough players (3 or more)
                                if (players.Count >= 3)
                                {
                                    // Create the tournament
                                    Console.WriteLine($"Creating tournament from signup '{signup.Name}' with {players.Count} players");

                                    // Find a channel to use
                                    DiscordChannel? channel = null;
                                    if (signup.SignupChannelId != 0)
                                    {
                                        try
                                        {
                                            channel = await client.GetChannelAsync(signup.SignupChannelId);
                                        }
                                        catch
                                        {
                                            // Channel might no longer exist
                                            Console.WriteLine($"Could not find channel with ID {signup.SignupChannelId} for signup '{signup.Name}'");
                                        }
                                    }

                                    if (channel is null)
                                    {
                                        // Try to get any channel from the guild
                                        var guild = await client.GetGuildAsync(client.Guilds.FirstOrDefault().Key);
                                        channel = guild.Channels.FirstOrDefault().Value;
                                        Console.WriteLine($"Using fallback channel {channel?.Name ?? "unknown"} for tournament creation");
                                    }

                                    if (channel is not null)
                                    {
                                        // Create the tournament
                                        var tournament = await CreateTournament(signup.Name, players, signup.Format, channel, signup.Type);
                                        Console.WriteLine($"Successfully created tournament '{tournament.Name}' from signup with {players.Count} players");

                                        // Save immediately to ensure the tournament is persisted
                                        SaveTournamentsToFile();
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Could not find any valid channel for tournament creation from signup '{signup.Name}'");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Not creating tournament from signup '{signup.Name}' - only {players.Count} valid players found (need at least 3)");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to create tournament from signup '{signup.Name}': {ex.Message}");
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }
                }

                // 2. Make sure tournaments with active rounds are linked
                foreach (var round in _ongoingRounds.TourneyRounds)
                {
                    if (string.IsNullOrEmpty(round.TournamentId))
                    {
                        var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                            t.Groups.Any(g => g.Matches.Any(m => m.LinkedRound == round)) ||
                            t.PlayoffMatches.Any(m => m.LinkedRound == round));

                        if (tournament != null)
                        {
                            round.TournamentId = tournament.Name;
                            Console.WriteLine($"Fixed missing tournament ID for round: linked to {tournament.Name}");
                        }
                    }
                }

                // 3. Save all data to ensure fixes are persisted
                await SaveAllData();
                Console.WriteLine("Data file repair completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during data file repair: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        // Add this method after GetSignup
        public async Task LoadParticipantsAsync(TournamentSignup signup, DSharpPlus.DiscordClient client, bool verbose = true)
        {
            // Clear existing participants to avoid duplicates
            signup.Participants.Clear();

            if (signup.ParticipantInfo == null || signup.ParticipantInfo.Count == 0)
            {
                if (verbose) Console.WriteLine($"No participant info found for signup '{signup.Name}'");
                return;
            }

            if (verbose) Console.WriteLine($"Loading {signup.ParticipantInfo.Count} participants for signup '{signup.Name}'");

            if (signup.SignupChannelId == 0)
            {
                Console.WriteLine($"ERROR: Signup '{signup.Name}' has no channel ID, cannot load participants");
                return;
            }

            // Check if client is connected
            // DSharpPlus doesn't expose ConnectionState directly
            try
            {
                // Try to get the channel first
                var channel = await client.GetChannelAsync(signup.SignupChannelId);
                if (channel?.Guild is null)
                {
                    Console.WriteLine($"ERROR: Could not find channel {signup.SignupChannelId} or its guild for signup '{signup.Name}'");
                    return;
                }

                var guild = channel.Guild;
                int successCount = 0;
                int failCount = 0;

                foreach (var (id, username) in signup.ParticipantInfo)
                {
                    try
                    {
                        // Try to get the member with retries
                        DiscordMember? member = null;
                        Exception? lastException = null;

                        // Try up to 3 times with short delays between attempts
                        for (int attempt = 1; attempt <= 3; attempt++)
                        {
                            try
                            {
                                member = await guild.GetMemberAsync(id);
                                if (member is not null) break;
                            }
                            catch (Exception ex)
                            {
                                lastException = ex;
                                if (verbose) Console.WriteLine($"Attempt {attempt} failed to get member {username} (ID: {id}): {ex.Message}");

                                if (attempt < 3)
                                {
                                    // Wait a bit before retrying (increasing delay with each attempt)
                                    await Task.Delay(attempt * 500);
                                }
                            }
                        }

                        if (member is not null)
                        {
                            signup.Participants.Add(member);
                            successCount++;
                            if (verbose) Console.WriteLine($"Added participant {username} (ID: {id}) to signup '{signup.Name}'");
                        }
                        else
                        {
                            failCount++;
                            string errorMessage = lastException != null ? $": {lastException.Message}" : " (unknown error)";
                            Console.WriteLine($"Failed to add participant {username} (ID: {id}) after 3 attempts{errorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Console.WriteLine($"ERROR: Exception while processing participant {username} (ID: {id}): {ex.Message}");
                    }
                }

                Console.WriteLine($"Participant loading for '{signup.Name}' complete: {successCount} succeeded, {failCount} failed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to load participants for signup '{signup.Name}': {ex.Message}");
            }

            // Also load seed information for participants
            await LoadSeedInformationAsync(signup, client);
        }

        // Add this method after LoadSignupsFromFile
        public async Task LoadAllParticipantsAsync(DSharpPlus.DiscordClient client)
        {
            if (_ongoingRounds.TournamentSignups == null || !_ongoingRounds.TournamentSignups.Any())
            {
                Console.WriteLine("No signups to load participants for");
                return;
            }

            Console.WriteLine($"Starting to load participants for {_ongoingRounds.TournamentSignups.Count} signups...");

            // Wait a bit after bot startup to ensure Discord connection is stable
            Console.WriteLine("Waiting 5 seconds to ensure Discord connection is fully established...");
            await Task.Delay(5000);

            // DSharpPlus doesn't expose ConnectionState directly

            int successCount = 0;
            int failCount = 0;

            foreach (var signup in _ongoingRounds.TournamentSignups)
            {
                try
                {
                    int originalCount = signup.ParticipantInfo?.Count ?? 0;
                    await LoadParticipantsAsync(signup, client, false);
                    int loadedCount = signup.Participants?.Count ?? 0;

                    if (loadedCount == originalCount)
                    {
                        successCount++;
                        Console.WriteLine($"Successfully loaded all {loadedCount} participants for signup '{signup.Name}'");
                    }
                    else
                    {
                        failCount++;
                        Console.WriteLine($"WARNING: Only loaded {loadedCount} of {originalCount} participants for signup '{signup.Name}'");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Console.WriteLine($"ERROR: Failed to load participants for signup '{signup.Name}': {ex.Message}");
                }
            }

            Console.WriteLine($"Finished loading participants: {successCount} signups fully loaded, {failCount} signups with issues");
        }

        public Task SaveTournamentState(DiscordClient? client = null)
        {
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(_dataDirectory);

                // Make sure any rounds from tournaments have their TournamentId set
                foreach (var round in _ongoingRounds.TourneyRounds)
                {
                    // Find the tournament this round belongs to (if any)
                    var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t =>
                        t.Groups.Any(g => g.Matches.Any(m => m.LinkedRound == round)) ||
                        t.PlayoffMatches.Any(m => m.LinkedRound == round));

                    if (tournament != null && string.IsNullOrEmpty(round.TournamentId))
                    {
                        round.TournamentId = tournament.Name;
                        Console.WriteLine($"Linked round to tournament: {tournament.Name}");
                    }
                }

                // Convert rounds to state
                var activeRounds = ConvertRoundsToState(_ongoingRounds.TourneyRounds);

                // Create sanitized copies of tournaments without Discord objects
                List<Tournament> sanitizedTournaments = new List<Tournament>();
                foreach (var tournament in _ongoingRounds.Tournaments)
                {
                    // Create a clean copy without Discord objects
                    Tournament cleanTournament = CleanTournamentForSerialization(tournament);
                    sanitizedTournaments.Add(cleanTournament);
                }

                // Serialize with preservation of object references
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    ReferenceHandler = ReferenceHandler.Preserve
                };

                // Create the state object manually to ensure proper format
                var stateObj = new
                {
                    Id = "1",
                    Tournaments = new
                    {
                        Id = "2",
                        Values = sanitizedTournaments
                    },
                    ActiveRounds = new
                    {
                        Id = "3",
                        Values = activeRounds
                    }
                };

                // Convert to JSON
                string json = JsonSerializer.Serialize(stateObj, options);

                // Replace property names to match expected format
                json = json.Replace("\"Id\":", "\"$id\":")
                         .Replace("\"Values\":", "\"$values\":");

                // Save the state
                File.WriteAllText(_tournamentStateFilePath, json);
                Console.WriteLine($"Saved tournament state with {sanitizedTournaments.Count} tournaments and {activeRounds.Count} active rounds");

                // Also save tournaments and signups
                SaveTournamentsToFile();
                SaveSignupsToFile();

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tournament state: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return Task.CompletedTask;
            }
        }

        public void LoadTournamentState()
        {
            try
            {
                if (File.Exists(_tournamentStateFilePath))
                {
                    string json = File.ReadAllText(_tournamentStateFilePath);
                    Console.WriteLine($"Loading tournament state from {_tournamentStateFilePath}");

                    if (!string.IsNullOrEmpty(json))
                    {
                        var options = new JsonSerializerOptions
                        {
                            ReferenceHandler = ReferenceHandler.Preserve
                        };

                        try
                        {
                            // Parse using JsonDocument for more flexibility
                            using JsonDocument document = JsonDocument.Parse(json);

                            // Process tournaments
                            if (document.RootElement.TryGetProperty("Tournaments", out JsonElement tournamentsElement) ||
                                document.RootElement.TryGetProperty("tournaments", out tournamentsElement))
                            {
                                if (tournamentsElement.TryGetProperty("$values", out JsonElement tournamentsArray))
                                {
                                    var tournaments = JsonSerializer.Deserialize<List<Tournament>>(tournamentsArray.GetRawText(), options);
                                    if (tournaments != null && tournaments.Count > 0)
                                    {
                                        Console.WriteLine($"Loaded {tournaments.Count} tournaments from state");
                                        _ongoingRounds.Tournaments = tournaments;
                                    }
                                }
                            }

                            // Process active rounds
                            if (document.RootElement.TryGetProperty("ActiveRounds", out JsonElement roundsElement) ||
                                document.RootElement.TryGetProperty("activeRounds", out roundsElement))
                            {
                                if (roundsElement.TryGetProperty("$values", out JsonElement roundsArray))
                                {
                                    var activeRounds = JsonSerializer.Deserialize<List<ActiveRound>>(roundsArray.GetRawText(), options);
                                    if (activeRounds != null)
                                    {
                                        var rounds = ConvertStateToRounds(activeRounds);
                                        _ongoingRounds.TourneyRounds = rounds;
                                        Console.WriteLine($"Loaded {rounds.Count} tournament rounds from state");
                                    }
                                }
                            }

                            // Fall back to standard deserialization if needed
                            if (_ongoingRounds.Tournaments.Count == 0)
                            {
                                var state = JsonSerializer.Deserialize<TournamentState>(json, options);
                                if (state?.Tournaments != null && state.Tournaments.Count > 0)
                                {
                                    _ongoingRounds.Tournaments = state.Tournaments;
                                    Console.WriteLine($"Loaded {state.Tournaments.Count} tournaments using standard deserialization");
                                }

                                if (state?.ActiveRounds != null && state.ActiveRounds.Count > 0)
                                {
                                    var rounds = ConvertStateToRounds(state.ActiveRounds);
                                    _ongoingRounds.TourneyRounds = rounds;
                                    Console.WriteLine($"Loaded {rounds.Count} rounds using standard deserialization");
                                }
                            }

                            // Link rounds to tournaments
                            LinkRoundsToTournaments();
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing tournament state: {ex.Message}");
                            Console.WriteLine(ex.StackTrace);
                        }
                    }
                }

                Console.WriteLine("No tournament state loaded, starting with empty state");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading tournament state: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        // Helper method to link rounds to their tournaments
        private void LinkRoundsToTournaments()
        {
            foreach (var tournament in _ongoingRounds.Tournaments)
            {
                foreach (var group in tournament.Groups)
                {
                    foreach (var match in group.Matches)
                    {
                        if (match.LinkedRound != null)
                        {
                            // Set TournamentId on the round
                            match.LinkedRound.TournamentId = tournament.Name;
                            Console.WriteLine($"Linked round to tournament: {tournament.Name}");
                        }
                    }
                }

                foreach (var match in tournament.PlayoffMatches)
                {
                    if (match.LinkedRound != null)
                    {
                        // Set TournamentId on the round
                        match.LinkedRound.TournamentId = tournament.Name;
                        Console.WriteLine($"Linked playoff round to tournament: {tournament.Name}");
                    }
                }
            }
        }

        public List<ActiveRound> ConvertRoundsToState(List<Round> rounds)
        {
            var result = new List<ActiveRound>();

            foreach (var round in rounds)
            {
                var activeRound = new ActiveRound
                {
                    Id = round.Name ?? Guid.NewGuid().ToString(),
                    Length = round.Length,
                    OneVOne = round.OneVOne,
                    Cycle = round.Cycle,
                    InGame = round.InGame,
                    Maps = round.Maps,
                    PlayedMaps = new List<string>(), // Will be populated from game results
                    TournamentMapPool = GetTournamentMapPool(round.OneVOne),
                    CurrentMapIndex = round.Cycle,
                    Status = round.InGame ? "InProgress" : "Created",
                    CreatedAt = DateTime.Now,
                    LastUpdatedAt = DateTime.Now,
                    ChannelId = round.Teams?.FirstOrDefault()?.Thread?.Id ?? 0
                };

                // Convert teams
                if (round.Teams != null)
                {
                    foreach (var team in round.Teams)
                    {
                        var teamState = new TeamState
                        {
                            Name = team.Name ?? "",
                            ThreadId = team.Thread?.Id ?? 0,
                            Wins = team.Wins,
                            MapBans = team.MapBans
                        };

                        // Convert participants
                        if (team.Participants != null)
                        {
                            foreach (var participant in team.Participants)
                            {
                                if (participant.Player is not null)
                                {
                                    var participantState = new ParticipantState
                                    {
                                        PlayerId = participant.Player.Id,
                                        PlayerName = participant.Player.Username,
                                        Deck = participant.Deck ?? ""
                                    };

                                    teamState.Participants.Add(participantState);
                                }
                            }
                        }

                        activeRound.Teams.Add(teamState);
                    }
                }

                // Track played maps based on the current cycle
                if (round.Cycle > 0 && round.Maps.Count > 0)
                {
                    for (int i = 0; i < round.Cycle; i++)
                    {
                        if (i < round.Maps.Count)
                        {
                            activeRound.PlayedMaps.Add(round.Maps[i]);

                            // Add game result
                            var gameResult = new GameResult
                            {
                                Map = round.Maps[i],
                                CompletedAt = DateTime.Now
                            };

                            // Try to determine winner
                            if (round.Teams != null && round.Teams.Count >= 2)
                            {
                                var team1 = round.Teams[0];
                                var team2 = round.Teams[1];

                                if (team1.Wins > team2.Wins)
                                {
                                    gameResult.WinnerId = team1.Participants?.FirstOrDefault()?.Player?.Id ?? 0;
                                }
                                else if (team2.Wins > team1.Wins)
                                {
                                    gameResult.WinnerId = team2.Participants?.FirstOrDefault()?.Player?.Id ?? 0;
                                }
                            }

                            activeRound.GameResults.Add(gameResult);
                        }
                    }
                }

                result.Add(activeRound);
            }

            return result;
        }

        private List<Round> ConvertStateToRounds(List<ActiveRound> activeRounds)
        {
            var result = new List<Round>();

            // This is a placeholder - actual implementation would need to recreate
            // the Round objects with all their Discord entity references
            // This would require looking up Discord entities by ID

            Console.WriteLine("Warning: Round state restoration is not fully implemented");

            return result;
        }

        // Add this method to get active rounds for a tournament
        public List<ActiveRound> GetActiveRoundsForTournament(string tournamentId)
        {
            try
            {
                // Convert rounds to state objects
                var activeRounds = ConvertRoundsToState(_ongoingRounds.TourneyRounds);

                // Filter by tournament ID
                return activeRounds.Where(r => r.TournamentId == tournamentId).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active rounds: {ex.Message}");
                return new List<ActiveRound>();
            }
        }

        // Add this method to get tournament map pool
        public List<string> GetTournamentMapPool(bool oneVOne)
        {
            string mapSize = oneVOne ? "1v1" : "2v2";
            var mapPool = Maps.MapCollection?
                .Where(m => m.Size == mapSize && m.IsInTournamentPool)
                .Select(m => m.Name)
                .ToList() ?? new List<string>();

            return mapPool;
        }

        public async Task<TournamentSignup?> GetSignupWithParticipants(string name, DSharpPlus.DiscordClient client)
        {
            var signup = GetSignup(name);
            if (signup == null)
                return null;

            Console.WriteLine($"Loading participants for signup '{name}'");

            // Load the participants
            await LoadParticipantsAsync(signup, client);

            // Make a copy of ParticipantInfo to Participants if needed
            if ((signup.Participants == null || signup.Participants.Count == 0) &&
                (signup.ParticipantInfo != null && signup.ParticipantInfo.Count > 0))
            {
                Console.WriteLine($"Participants list is empty but ParticipantInfo exists, restoring {signup.ParticipantInfo.Count} participants");

                // Create participants from participantInfo
                signup.Participants = new List<DiscordMember>();
                var guild = await client.GetGuildAsync(client.Guilds.FirstOrDefault().Key);

                foreach (var info in signup.ParticipantInfo)
                {
                    try
                    {
                        var member = await guild.GetMemberAsync(info.Id);
                        if (member is not null)
                        {
                            signup.Participants.Add(member);
                            Console.WriteLine($"Restored participant: {member.DisplayName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to restore participant {info.Username}: {ex.Message}");
                    }
                }
            }

            // Save immediately to ensure participants are preserved
            UpdateSignup(signup);

            return signup;
        }

        // Add this new method after LoadParticipantsAsync
        private async Task LoadSeedInformationAsync(TournamentSignup signup, DSharpPlus.DiscordClient client)
        {
            try
            {
                if (signup.SeedInfo == null || !signup.SeedInfo.Any())
                {
                    // No seed information to load
                    return;
                }

                Console.WriteLine($"Loading seed information for '{signup.Name}'...");
                int successCount = 0;
                int failCount = 0;

                // Initialize Seeds collection if needed
                if (signup.Seeds == null)
                {
                    signup.Seeds = [];
                }
                else
                {
                    // Clear existing seeds to prevent duplicates
                    signup.Seeds.Clear();
                }

                foreach (var (id, seedValue) in signup.SeedInfo)
                {
                    try
                    {
                        // Find the matching participant in the already loaded participants
                        var participant = signup.Participants.FirstOrDefault(p => p.Id == id);

                        if (participant is not null)
                        {
                            // Create and add the seed information
                            var participantSeed = new ParticipantSeed
                            {
                                Player = participant,
                                PlayerId = id,
                                Seed = seedValue
                            };

                            signup.Seeds.Add(participantSeed);
                            successCount++;
                        }
                        else
                        {
                            // Participant not found in loaded participants
                            failCount++;
                            Console.WriteLine($"Participant with ID {id} not found for seeding in '{signup.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Console.WriteLine($"ERROR: Exception while loading seed for participant ID {id}: {ex.Message}");
                    }
                }

                Console.WriteLine($"Seed information loading for '{signup.Name}' complete: {successCount} succeeded, {failCount} failed");

                // Add this to avoid the "async method lacks await" warning
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to load seed information for signup '{signup.Name}': {ex.Message}");
            }
        }
    }
}