using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Reflection;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Misc;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// ActiveRound class for serializing/deserializing round state
    /// </summary>
    public class ActiveRound
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ulong ChannelId { get; set; }
        public ulong? Player1Id { get; set; }
        public string Player1Username { get; set; } = string.Empty;
        public ulong? Player2Id { get; set; }
        public string Player2Username { get; set; } = string.Empty;
        public ulong? MessageId { get; set; }
        public string Map { get; set; } = string.Empty;
        public int MapNum { get; set; }
        public int BestOf { get; set; }
        public int BanCount { get; set; }
        public List<string> MapPool { get; set; } = new List<string>();
        public int Player1Score { get; set; }
        public int Player2Score { get; set; }
        public List<string> Maps { get; set; } = new List<string>();
        public List<string> BannedMaps { get; set; } = new List<string>();
        public int MapBanPhase { get; set; }
        public int CurrentTurn { get; set; }
        public bool MapBanCompleted { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsReplayVerified { get; set; }
        public bool SpectatorMode { get; set; }
        public bool IsRanked { get; set; }
        public int RankedSeed { get; set; }
        public bool IsTournamentRound { get; set; }
        public string TournamentId { get; set; } = string.Empty;
        public int Winner { get; set; }
        public List<string> PlayedMaps { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service for tournament state management
    /// </summary>
    public class TournamentStateService : ITournamentStateService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly string _dataDirectory;
        private readonly string _tournamentStateFilePath;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger<TournamentStateService> _logger;
        private readonly ITournamentMapService _mapService;

        public TournamentStateService(
            OngoingRounds ongoingRounds,
            ILogger<TournamentStateService> logger,
            ITournamentMapService mapService)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;
            _mapService = mapService;

            // Setup data directory
            _dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(_dataDirectory);

            _tournamentStateFilePath = Path.Combine(_dataDirectory, "tournament_state.json");

            // Configure JSON serializer options
            _serializerOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };

            // Load tournament state
            LoadTournamentState();
        }

        /// <summary>
        /// Saves the current tournament state
        /// </summary>
        public async Task SaveTournamentStateAsync(DiscordClient? client = null)
        {
            try
            {
                _logger.LogInformation("Saving tournament state");

                // Convert all rounds to their state representation
                var activeRounds = ConvertRoundsToState(_ongoingRounds.TourneyRounds);

                // Serialize to JSON
                string json = JsonSerializer.Serialize(activeRounds, _serializerOptions);

                // Save to file
                await File.WriteAllTextAsync(_tournamentStateFilePath, json);

                _logger.LogInformation($"Saved {activeRounds.Count} active rounds to {_tournamentStateFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving tournament state: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the tournament state
        /// </summary>
        public void LoadTournamentState()
        {
            try
            {
                if (File.Exists(_tournamentStateFilePath))
                {
                    string json = File.ReadAllText(_tournamentStateFilePath);
                    _logger.LogInformation($"Loading tournament state from {_tournamentStateFilePath}");

                    if (!string.IsNullOrEmpty(json))
                    {
                        // Deserialize the active rounds
                        var activeRounds = JsonSerializer.Deserialize<List<ActiveRound>>(json, _serializerOptions);

                        if (activeRounds != null)
                        {
                            // Convert active rounds back to regular rounds
                            var rounds = ConvertStateToRounds(activeRounds);

                            // Update the ongoing rounds
                            _ongoingRounds.TourneyRounds = rounds;

                            _logger.LogInformation($"Loaded {rounds.Count} rounds from tournament state");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation($"No tournament state file found at {_tournamentStateFilePath}");
                }

                // Link rounds to tournaments
                LinkRoundsToTournaments();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading tournament state: {ex.Message}");

                // Initialize empty list if loading fails
                _ongoingRounds.TourneyRounds = new List<Round>();
            }
        }

        /// <summary>
        /// Links rounds to tournaments
        /// </summary>
        public void LinkRoundsToTournaments()
        {
            _logger.LogInformation("Linking rounds to tournaments");

            foreach (var tournament in _ongoingRounds.Tournaments)
            {
                // Link group stage matches
                foreach (var group in tournament.Groups)
                {
                    foreach (var match in group.Matches)
                    {
                        if (match.LinkedRound != null)
                        {
                            // Try to find the actual round in the system
                            // Use TournamentId to find matching rounds
                            var round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                                r.TournamentId == tournament.Name &&
                                r.Name == match.Name);

                            if (round != null)
                            {
                                match.LinkedRound = round;

                                // Set reference back to the match in the round
                                if (round.CustomProperties == null)
                                {
                                    round.CustomProperties = new Dictionary<string, object>();
                                }
                                round.CustomProperties["TournamentMatch"] = match;
                                round.TournamentId = tournament.Name;
                            }
                        }
                    }
                }

                // Link playoff matches
                foreach (var match in tournament.PlayoffMatches)
                {
                    if (match.LinkedRound != null)
                    {
                        // Try to find the actual round in the system
                        var round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                            r.TournamentId == tournament.Name &&
                            r.Name == match.Name);

                        if (round != null)
                        {
                            match.LinkedRound = round;

                            // Set reference back to the match in the round
                            if (round.CustomProperties == null)
                            {
                                round.CustomProperties = new Dictionary<string, object>();
                            }
                            round.CustomProperties["TournamentMatch"] = match;
                            round.TournamentId = tournament.Name;
                        }
                    }
                }
            }

            _logger.LogInformation("Rounds linked to tournaments");
        }

        /// <summary>
        /// Converts rounds to state
        /// </summary>
        public List<Wabbit.Services.ActiveRound> ConvertRoundsToState(List<Round> rounds)
        {
            var result = new List<Wabbit.Services.ActiveRound>();

            foreach (var round in rounds)
            {
                if (round == null) continue;

                // Extract data from the round's Teams collection
                var team1 = round.Teams?.FirstOrDefault();
                var team2 = round.Teams?.Skip(1).FirstOrDefault();

                var player1 = team1?.Participants?.FirstOrDefault()?.Player;
                var player2 = team2?.Participants?.FirstOrDefault()?.Player;

                // Get player IDs safely
                ulong? player1Id = null;
                ulong? player2Id = null;
                string player1Username = string.Empty;
                string player2Username = string.Empty;

                if (player1 is not null)
                {
                    // Check if player has Id property
                    var idProperty = player1.GetType().GetProperty("Id");
                    if (idProperty != null)
                    {
                        var idValue = idProperty.GetValue(player1);
                        if (idValue != null && ulong.TryParse(idValue.ToString(), out ulong id))
                        {
                            player1Id = id;
                        }
                    }

                    // Get username
                    var usernameProperty = player1.GetType().GetProperty("Username");
                    if (usernameProperty != null)
                    {
                        var usernameValue = usernameProperty.GetValue(player1);
                        player1Username = usernameValue?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        player1Username = player1.ToString() ?? string.Empty;
                    }
                }

                if (player2 is not null)
                {
                    // Check if player has Id property
                    var idProperty = player2.GetType().GetProperty("Id");
                    if (idProperty != null)
                    {
                        var idValue = idProperty.GetValue(player2);
                        if (idValue != null && ulong.TryParse(idValue.ToString(), out ulong id))
                        {
                            player2Id = id;
                        }
                    }

                    // Get username
                    var usernameProperty = player2.GetType().GetProperty("Username");
                    if (usernameProperty != null)
                    {
                        var usernameValue = usernameProperty.GetValue(player2);
                        player2Username = usernameValue?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        player2Username = player2.ToString() ?? string.Empty;
                    }
                }

                // Create the active round with safe property access
                var activeRound = new Wabbit.Services.ActiveRound
                {
                    Id = round.CustomProperties != null && round.CustomProperties.ContainsKey("RoundId") ?
                        round.CustomProperties["RoundId"]?.ToString() ?? Guid.NewGuid().ToString() :
                        Guid.NewGuid().ToString(),
                    ChannelId = team1?.Thread?.Id ?? 0,
                    Player1Id = player1Id,
                    Player1Username = player1Username,
                    Player2Id = player2Id,
                    Player2Username = player2Username,
                    MessageId = round.MsgToDel?.FirstOrDefault()?.Id,
                    Map = round.Maps?.FirstOrDefault() ?? string.Empty,
                    MapNum = round.Cycle,
                    BestOf = round.Length,
                    Maps = round.Maps?.ToList() ?? new List<string>(),
                    PlayedMaps = round.Maps?.ToList() ?? new List<string>()
                };

                // Get scores from teams
                if (team1 != null) activeRound.Player1Score = team1.Wins;
                if (team2 != null) activeRound.Player2Score = team2.Wins;

                // Get data from CustomProperties
                if (round.CustomProperties != null)
                {
                    // Map ban data
                    if (team1 != null)
                    {
                        activeRound.BanCount = team1.MapBans?.Count ?? 0;
                        activeRound.BannedMaps = team1.MapBans?.ToList() ?? new List<string>();
                        if (team2 != null && team2.MapBans != null)
                        {
                            activeRound.BannedMaps.AddRange(team2.MapBans);
                        }
                    }

                    // Map pool
                    if (round.CustomProperties.ContainsKey("MapPool") && round.CustomProperties["MapPool"] is List<string> mapPool)
                    {
                        activeRound.MapPool = mapPool;
                    }

                    // Match state
                    if (round.CustomProperties.ContainsKey("MapBanPhase"))
                    {
                        activeRound.MapBanPhase = Convert.ToInt32(round.CustomProperties["MapBanPhase"]);
                    }

                    if (round.CustomProperties.ContainsKey("CurrentTurn"))
                    {
                        activeRound.CurrentTurn = Convert.ToInt32(round.CustomProperties["CurrentTurn"]);
                    }

                    if (round.CustomProperties.ContainsKey("MapBanCompleted"))
                    {
                        activeRound.MapBanCompleted = Convert.ToBoolean(round.CustomProperties["MapBanCompleted"]);
                    }

                    // Match completion
                    activeRound.IsCompleted = !round.InGame && round.Cycle >= round.Length;

                    if (round.CustomProperties.ContainsKey("CreatedAt"))
                    {
                        activeRound.CreatedAt = Convert.ToDateTime(round.CustomProperties["CreatedAt"]);
                    }
                    else
                    {
                        activeRound.CreatedAt = DateTime.Now;
                    }

                    if (round.CustomProperties.ContainsKey("CompletedAt"))
                    {
                        activeRound.CompletedAt = Convert.ToDateTime(round.CustomProperties["CompletedAt"]);
                    }

                    if (round.CustomProperties.ContainsKey("IsReplayVerified"))
                    {
                        activeRound.IsReplayVerified = Convert.ToBoolean(round.CustomProperties["IsReplayVerified"]);
                    }

                    if (round.CustomProperties.ContainsKey("SpectatorMode"))
                    {
                        activeRound.SpectatorMode = Convert.ToBoolean(round.CustomProperties["SpectatorMode"]);
                    }

                    if (round.CustomProperties.ContainsKey("IsRanked"))
                    {
                        activeRound.IsRanked = Convert.ToBoolean(round.CustomProperties["IsRanked"]);
                    }

                    if (round.CustomProperties.ContainsKey("RankedSeed"))
                    {
                        activeRound.RankedSeed = Convert.ToInt32(round.CustomProperties["RankedSeed"]);
                    }

                    if (round.CustomProperties.ContainsKey("Winner"))
                    {
                        activeRound.Winner = Convert.ToInt32(round.CustomProperties["Winner"]);
                    }
                }

                // Tournament data
                activeRound.IsTournamentRound = !string.IsNullOrEmpty(round.TournamentId);
                activeRound.TournamentId = round.TournamentId ?? string.Empty;

                result.Add(activeRound);
            }

            return result;
        }

        /// <summary>
        /// Converts state to rounds
        /// </summary>
        public List<Round> ConvertStateToRounds(List<Wabbit.Services.ActiveRound> activeRounds)
        {
            var result = new List<Round>();

            foreach (var activeRound in activeRounds)
            {
                // Create the base round
                var round = new Round
                {
                    Name = activeRound.TournamentId,
                    Length = activeRound.BestOf,
                    OneVOne = true,
                    Cycle = activeRound.MapNum,
                    InGame = !activeRound.IsCompleted,
                    Maps = activeRound.Maps?.ToList() ?? new List<string>(),
                    TournamentId = activeRound.TournamentId,
                    Teams = new List<Round.Team>(),
                    MsgToDel = new List<DiscordMessage>()
                };

                // Initialize CustomProperties if needed
                if (round.CustomProperties == null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }

                // Store ActiveRound properties in CustomProperties
                round.CustomProperties["RoundId"] = activeRound.Id;
                round.CustomProperties["MapBanPhase"] = activeRound.MapBanPhase;
                round.CustomProperties["CurrentTurn"] = activeRound.CurrentTurn;
                round.CustomProperties["MapBanCompleted"] = activeRound.MapBanCompleted;
                round.CustomProperties["CreatedAt"] = activeRound.CreatedAt ?? DateTime.Now;

                if (activeRound.CompletedAt.HasValue)
                {
                    round.CustomProperties["CompletedAt"] = activeRound.CompletedAt.Value;
                }

                round.CustomProperties["IsReplayVerified"] = activeRound.IsReplayVerified;
                round.CustomProperties["SpectatorMode"] = activeRound.SpectatorMode;
                round.CustomProperties["IsRanked"] = activeRound.IsRanked;
                round.CustomProperties["RankedSeed"] = activeRound.RankedSeed;
                round.CustomProperties["Winner"] = activeRound.Winner;
                round.CustomProperties["MapPool"] = activeRound.MapPool;

                // We need to create empty teams as placeholders
                // The actual player objects will be filled in when needed
                var team1 = new Round.Team
                {
                    Name = activeRound.Player1Username,
                    Wins = activeRound.Player1Score,
                    Participants = new List<Round.Participant>(),
                    MapBans = activeRound.BannedMaps?.Take(activeRound.BanCount / 2).ToList() ?? new List<string>()
                };

                var team2 = new Round.Team
                {
                    Name = activeRound.Player2Username,
                    Wins = activeRound.Player2Score,
                    Participants = new List<Round.Participant>(),
                    MapBans = activeRound.BannedMaps?.Skip(activeRound.BanCount / 2).Take(activeRound.BanCount / 2).ToList() ?? new List<string>()
                };

                // Store player info in custom properties for later
                if (round.CustomProperties != null)
                {
                    round.CustomProperties["Player1Id"] = activeRound.Player1Id ?? 0;
                    round.CustomProperties["Player1Username"] = activeRound.Player1Username ?? string.Empty;
                    round.CustomProperties["Player2Id"] = activeRound.Player2Id ?? 0;
                    round.CustomProperties["Player2Username"] = activeRound.Player2Username ?? string.Empty;
                }

                // Add teams to round (without participants for now)
                round.Teams.Add(team1);
                round.Teams.Add(team2);

                result.Add(round);
            }

            return result;
        }

        /// <summary>
        /// Serialized player class for storing in Round
        /// </summary>
        private class SerializedPlayer
        {
            public ulong? Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Type => "SerializedPlayer";

            public override string ToString() => Username;
        }

        /// <summary>
        /// Gets active rounds for a tournament
        /// </summary>
        public List<Wabbit.Services.ActiveRound> GetActiveRoundsForTournament(string tournamentId)
        {
            try
            {
                return ConvertRoundsToState(_ongoingRounds.TourneyRounds)
                    .Where(r => r.TournamentId == tournamentId)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting active rounds: {ex.Message}");
                return new List<Wabbit.Services.ActiveRound>();
            }
        }

        /// <summary>
        /// Gets the tournament map pool by delegating to the specialized map service
        /// </summary>
        public List<string> GetTournamentMapPool(bool oneVOne)
        {
            _logger.LogInformation($"Getting tournament map pool for {(oneVOne ? "1v1" : "team")} matches");
            return _mapService.GetTournamentMapPool(oneVOne);
        }

        /// <summary>
        /// Updates tournament from a round
        /// </summary>
        public void UpdateTournamentFromRound(Tournament tournament)
        {
            if (tournament == null) return;

            _logger.LogInformation($"Updating tournament {tournament.Name} from rounds");

            // Update group stage matches
            foreach (var group in tournament.Groups)
            {
                foreach (var match in group.Matches)
                {
                    if (match.LinkedRound != null &&
                        match.LinkedRound.CustomProperties.ContainsKey("IsCompleted") &&
                        Convert.ToBoolean(match.LinkedRound.CustomProperties["IsCompleted"]))
                    {
                        UpdateMatchFromRound(match, match.LinkedRound);
                    }
                }
            }

            // Update playoff matches
            foreach (var match in tournament.PlayoffMatches)
            {
                if (match.LinkedRound != null &&
                    match.LinkedRound.CustomProperties.ContainsKey("IsCompleted") &&
                    Convert.ToBoolean(match.LinkedRound.CustomProperties["IsCompleted"]))
                {
                    UpdateMatchFromRound(match, match.LinkedRound);
                }
            }

            // Check if tournament is complete
            bool allPlayoffMatchesComplete = tournament.PlayoffMatches.Count > 0 &&
                tournament.PlayoffMatches.All(m => m.IsComplete);

            if (tournament.CurrentStage == TournamentStage.Playoffs && allPlayoffMatchesComplete)
            {
                tournament.CurrentStage = TournamentStage.Complete;
                tournament.IsComplete = true;

                _logger.LogInformation($"Tournament {tournament.Name} is now complete");
            }
        }

        /// <summary>
        /// Updates a match from a round
        /// </summary>
        private void UpdateMatchFromRound(Tournament.Match match, Round round)
        {
            if (match == null || round == null) return;

            // Create match result if it doesn't exist
            if (match.Result == null)
            {
                match.Result = new Tournament.MatchResult
                {
                    CompletedAt = round.CustomProperties.ContainsKey("CompletedAt") ?
                        Convert.ToDateTime(round.CustomProperties["CompletedAt"]) : DateTime.Now,
                    MapResults = round.Maps?.ToList() ?? new List<string>()
                };
            }

            // Update scores
            if (match.Participants.Count >= 2)
            {
                int player1Score = round.Teams?.FirstOrDefault()?.Wins ?? 0;
                int player2Score = round.Teams?.Skip(1).FirstOrDefault()?.Wins ?? 0;

                match.Participants[0].Score = player1Score;
                match.Participants[1].Score = player2Score;

                // Get winner from custom properties
                int winner = round.CustomProperties.ContainsKey("Winner") ?
                    Convert.ToInt32(round.CustomProperties["Winner"]) : 0;

                // Update winner
                if (winner == 1)
                {
                    match.Participants[0].IsWinner = true;
                    match.Participants[1].IsWinner = false;
                    match.Result.Winner = match.Participants[0].Player;
                }
                else if (winner == 2)
                {
                    match.Participants[0].IsWinner = false;
                    match.Participants[1].IsWinner = true;
                    match.Result.Winner = match.Participants[1].Player;
                }

                // Update stats for group participants
                if (match.Type == TournamentMatchType.GroupStage &&
                    match.Participants[0]?.SourceGroup != null &&
                    match.Participants[0]?.SourceGroup?.Participants?.Count > 0 &&
                    match.Participants[1]?.SourceGroup != null &&
                    match.Participants[1]?.SourceGroup?.Participants?.Count > 0)
                {
                    var player1 = match.Participants[0]?.Player;
                    var player2 = match.Participants[1]?.Player;

                    var group1 = match.Participants[0]?.SourceGroup;
                    var group2 = match.Participants[1]?.SourceGroup;

                    // Find participants in groups
                    var groupParticipant1 = group1?.Participants?.FirstOrDefault(p =>
                        ArePlayersEqual(p?.Player, player1));

                    var groupParticipant2 = group2?.Participants?.FirstOrDefault(p =>
                        ArePlayersEqual(p?.Player, player2));

                    if (groupParticipant1 != null && groupParticipant2 != null)
                    {
                        // Update wins/losses
                        if (winner == 1)
                        {
                            groupParticipant1.Wins++;
                            groupParticipant2.Losses++;
                        }
                        else if (winner == 2)
                        {
                            groupParticipant1.Losses++;
                            groupParticipant2.Wins++;
                        }
                        else
                        {
                            // Draw
                            groupParticipant1.Draws++;
                            groupParticipant2.Draws++;
                        }

                        // Update game counts
                        groupParticipant1.GamesWon += player1Score;
                        groupParticipant1.GamesLost += player2Score;

                        groupParticipant2.GamesWon += player2Score;
                        groupParticipant2.GamesLost += player1Score;
                    }
                }

                // Update next match if this is part of a bracket
                if (match.NextMatch != null && match.Result != null && match.Result.Winner != null)
                {
                    // Find index of this match in the previous round
                    var tournament = FindTournamentByMatch(match);
                    if (tournament != null)
                    {
                        int matchIndex = tournament.PlayoffMatches.IndexOf(match);
                        if (matchIndex >= 0)
                        {
                            // Determine if this is the first or second match feeding into next match
                            bool isFirstMatch = matchIndex % 2 == 0;

                            // Update the appropriate participant in the next match
                            if (isFirstMatch && match.NextMatch.Participants.Count > 0)
                            {
                                match.NextMatch.Participants[0].Player = match.Result.Winner;
                                match.NextMatch.Name = UpdateMatchName(match.NextMatch);
                            }
                            else if (!isFirstMatch && match.NextMatch.Participants.Count > 1)
                            {
                                match.NextMatch.Participants[1].Player = match.Result.Winner;
                                match.NextMatch.Name = UpdateMatchName(match.NextMatch);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates a match name based on its participants
        /// </summary>
        private string UpdateMatchName(Tournament.Match match)
        {
            if (match.Participants.Count < 2) return "TBD vs TBD";

            string player1Name = match.Participants[0].Player != null ?
                GetPlayerName(match.Participants[0].Player) : "TBD";

            string player2Name = match.Participants[1].Player != null ?
                GetPlayerName(match.Participants[1].Player) : "TBD";

            return $"{player1Name} vs {player2Name}";
        }

        /// <summary>
        /// Gets a player's name
        /// </summary>
        private string GetPlayerName(object? player)
        {
            if (player == null) return "TBD";

            if (player is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("Username", out var username))
                {
                    return username.ToString() ?? "Unknown";
                }
                return "Unknown";
            }

            if (player is DiscordMember member)
                return member.DisplayName;

            if (player is DiscordUser user)
                return user.Username;

            return player.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Checks if two player objects refer to the same player
        /// </summary>
        private bool ArePlayersEqual(object? player1, object? player2)
        {
            if (player1 == null || player2 == null) return false;

            // Get IDs if possible
            ulong? id1 = GetPlayerId(player1);
            ulong? id2 = GetPlayerId(player2);

            // Compare IDs if available
            if (id1.HasValue && id2.HasValue)
            {
                return id1.Value == id2.Value;
            }

            // Fallback to string comparison
            return GetPlayerName(player1) == GetPlayerName(player2);
        }

        /// <summary>
        /// Gets a player's ID
        /// </summary>
        private ulong? GetPlayerId(object? player)
        {
            if (player == null) return null;

            if (player is DiscordMember member)
            {
                return member.Id;
            }

            if (player is DiscordUser user)
            {
                return user.Id;
            }

            if (player is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("Id", out var idObj) && idObj != null)
                {
                    return Convert.ToUInt64(idObj);
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the tournament containing a match
        /// </summary>
        private Tournament? FindTournamentByMatch(Tournament.Match match)
        {
            foreach (var tournament in _ongoingRounds.Tournaments)
            {
                // Check group stage matches
                foreach (var group in tournament.Groups)
                {
                    if (group.Matches.Contains(match))
                    {
                        return tournament;
                    }
                }

                // Check playoff matches
                if (tournament.PlayoffMatches.Contains(match))
                {
                    return tournament;
                }
            }

            return null;
        }

        /// <summary>
        /// Safely saves the tournament state with retry logic and error handling
        /// </summary>
        public async Task<bool> SafeSaveTournamentStateAsync(DiscordClient? client = null, string? caller = null)
        {
            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                try
                {
                    attempt++;
                    _logger.LogInformation($"Saving tournament state (attempt {attempt}/{maxRetries}){(caller != null ? $" from {caller}" : "")}");

                    // Convert all rounds to their state representation
                    var activeRounds = ConvertRoundsToState(_ongoingRounds.TourneyRounds);

                    // Serialize to JSON
                    string json = JsonSerializer.Serialize(activeRounds, _serializerOptions);

                    // Save to file
                    await File.WriteAllTextAsync(_tournamentStateFilePath, json);

                    _logger.LogInformation($"Successfully saved {activeRounds.Count} active rounds to {_tournamentStateFilePath}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving tournament state (attempt {attempt}/{maxRetries}): {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * attempt); // Exponential backoff
                        continue;
                    }
                }
            }

            return false;
        }
    }
}