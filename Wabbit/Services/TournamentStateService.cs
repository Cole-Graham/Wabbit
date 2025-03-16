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
using Wabbit.Services.ServiceHelpers;

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
        private readonly ITournamentStateValidator _stateValidator;
        private readonly ITournamentScoreManager _scoreManager;
        private readonly ITournamentProgressTracker _progressTracker;

        public TournamentStateService(
            OngoingRounds ongoingRounds,
            ILogger<TournamentStateService> logger,
            ITournamentMapService mapService,
            ITournamentStateValidator stateValidator,
            ITournamentScoreManager scoreManager,
            ITournamentProgressTracker progressTracker)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;
            _mapService = mapService;
            _stateValidator = stateValidator;
            _scoreManager = scoreManager;
            _progressTracker = progressTracker;

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
        }

        /// <summary>
        /// Initializes the tournament state service by loading the state.
        /// Should be called immediately after construction.
        /// </summary>
        public async Task InitializeAsync()
        {
            await LoadTournamentState();
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
        public async Task<Tournament?> LoadTournamentState()
        {
            try
            {
                if (File.Exists(_tournamentStateFilePath))
                {
                    string json = await File.ReadAllTextAsync(_tournamentStateFilePath);
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

                            // Return the first tournament if any exists
                            return _ongoingRounds.Tournaments.FirstOrDefault();
                        }
                    }
                }
                else
                {
                    _logger.LogInformation($"No tournament state file found at {_tournamentStateFilePath}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading tournament state: {ex.Message}");
                return null;
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
                // Validate state transition
                if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Complete))
                {
                    tournament.CurrentStage = TournamentStage.Complete;
                    tournament.IsComplete = true;

                    _logger.LogInformation($"Tournament {tournament.Name} is now complete");
                }
                else
                {
                    _logger.LogWarning($"Invalid state transition from {tournament.CurrentStage} to Complete for tournament {tournament.Name}");
                }
            }
        }

        /// <summary>
        /// Updates a match from its linked round
        /// </summary>
        private void UpdateMatchFromRound(Tournament.Match match, Round round)
        {
            if (round == null || !round.CustomProperties.ContainsKey("IsCompleted"))
            {
                _logger.LogWarning($"Cannot update match {match.Name}: round is null or incomplete");
                return;
            }

            // Get winner from round properties
            if (round.CustomProperties.ContainsKey("Winner") &&
                Convert.ToInt32(round.CustomProperties["Winner"]) != 0)
            {
                match.Result = new Tournament.MatchResult
                {
                    Winner = round.CustomProperties.ContainsKey("Winner") && Convert.ToInt32(round.CustomProperties["Winner"]) == 1 ?
                        match.Participants[0].Player : match.Participants[1].Player,
                    WinnerScore = round.CustomProperties.ContainsKey("Player1Score") ?
                        Convert.ToInt32(round.CustomProperties["Player1Score"]) : 0,
                    LoserScore = round.CustomProperties.ContainsKey("Player2Score") ?
                        Convert.ToInt32(round.CustomProperties["Player2Score"]) : 0,
                    CompletedAt = round.CustomProperties.ContainsKey("CompletedAt") ?
                        Convert.ToDateTime(round.CustomProperties["CompletedAt"]) : DateTime.UtcNow
                };

                // Update group scores if this is a group stage match
                if (match.Type == TournamentMatchType.GroupStage &&
                    match.Participants[0].SourceGroup != null)
                {
                    var group = match.Participants[0].SourceGroup;
                    if (group != null)
                    {
                        _scoreManager.UpdateGroupScores(group, match);
                    }
                }

                // Update tournament progress
                var tournament = _ongoingRounds.Tournaments.FirstOrDefault(t => t.Name == match.TournamentId);
                if (tournament != null)
                {
                    var stageProgress = _progressTracker.GetStageProgress(tournament);
                    bool canProgress = _progressTracker.CanProgressToNextStage(tournament);
                }
            }
        }

        /// <summary>
        /// Updates tournament state from completed rounds
        /// </summary>
        public void UpdateTournamentFromRounds(Tournament tournament)
        {
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

            // Check tournament progress and update state
            var stageProgress = _progressTracker.GetStageProgress(tournament);
            bool canProgress = _progressTracker.CanProgressToNextStage(tournament);

            if (canProgress && tournament.CurrentStage == TournamentStage.Complete)
            {
                // Validate state transition
                if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Complete))
                {
                    tournament.CurrentStage = TournamentStage.Complete;
                    tournament.IsComplete = true;
                    _logger.LogInformation($"Tournament {tournament.Name} is now complete");
                }
            }
            else if (canProgress && tournament.CurrentStage == TournamentStage.Groups)
            {
                // Validate state transition
                if (_stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs))
                {
                    tournament.CurrentStage = TournamentStage.Playoffs;
                    _logger.LogInformation($"Tournament {tournament.Name} is ready for playoffs");
                }
            }
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