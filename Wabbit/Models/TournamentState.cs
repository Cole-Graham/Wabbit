using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Wabbit.Models
{
    /// <summary>
    /// Top-level class for tournament state persistence
    /// </summary>
    public class TournamentState
    {
        /// <summary>
        /// List of all tournaments
        /// </summary>
        public List<Tournament> Tournaments { get; set; } = [];

        /// <summary>
        /// List of active rounds
        /// </summary>
        public List<ActiveRound> ActiveRounds { get; set; } = [];
    }

    /// <summary>
    /// Represents an active round in a tournament
    /// </summary>
    public class ActiveRound
    {
        /// <summary>
        /// Unique identifier for the round
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// ID of the tournament this round belongs to
        /// </summary>
        public string TournamentId { get; set; } = "";

        /// <summary>
        /// ID of the match this round belongs to
        /// </summary>
        public string MatchId { get; set; } = "";

        /// <summary>
        /// Type of round (1v1 or 2v2)
        /// </summary>
        public string Type { get; set; } = "1v1";

        /// <summary>
        /// Length of the round (best of X)
        /// </summary>
        public int Length { get; set; } = 3; // Default to BO3

        /// <summary>
        /// Whether this is a 1v1 round
        /// </summary>
        public bool OneVOne { get; set; } = true;

        /// <summary>
        /// Current game cycle (0-based index)
        /// </summary>
        public int Cycle { get; set; } = 0;

        /// <summary>
        /// Whether a game is currently in progress
        /// </summary>
        public bool InGame { get; set; } = false;

        /// <summary>
        /// Teams participating in this round
        /// </summary>
        public List<TeamState> Teams { get; set; } = [];

        /// <summary>
        /// Maps selected for this round
        /// </summary>
        public List<string> Maps { get; set; } = [];

        /// <summary>
        /// Maps that have been played in this round
        /// </summary>
        public List<string> PlayedMaps { get; set; } = [];

        /// <summary>
        /// Maps that are available in the tournament pool
        /// </summary>
        public List<string> TournamentMapPool { get; set; } = [];

        /// <summary>
        /// Index of the current map being played
        /// </summary>
        public int CurrentMapIndex { get; set; } = 0;

        /// <summary>
        /// Current status of the round
        /// </summary>
        public string Status { get; set; } = "Created"; // Created, MapBanning, DeckSubmission, InProgress, Completed

        /// <summary>
        /// When the round was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// When the round was last updated
        /// </summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Discord channel ID where the round is being played
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Discord message IDs for important messages
        /// </summary>
        [JsonIgnore]
        public List<ulong> MessageIds { get; set; } = [];

        /// <summary>
        /// Game results for this round
        /// </summary>
        public List<GameResult> GameResults { get; set; } = [];
    }

    /// <summary>
    /// Represents a team in an active round
    /// </summary>
    public class TeamState
    {
        /// <summary>
        /// Name of the team
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Discord thread ID for team communication
        /// </summary>
        public ulong ThreadId { get; set; }

        /// <summary>
        /// Participants in the team
        /// </summary>
        public List<ParticipantState> Participants { get; set; } = [];

        /// <summary>
        /// Number of wins for this team
        /// </summary>
        public int Wins { get; set; } = 0;

        /// <summary>
        /// Maps banned by this team
        /// </summary>
        public List<string> MapBans { get; set; } = [];
    }

    /// <summary>
    /// Represents a participant in a team
    /// </summary>
    public class ParticipantState
    {
        /// <summary>
        /// Discord user ID of the player
        /// </summary>
        public ulong PlayerId { get; set; }

        /// <summary>
        /// Username of the player
        /// </summary>
        public string PlayerName { get; set; } = "";

        /// <summary>
        /// Deck code submitted by the player
        /// </summary>
        public string Deck { get; set; } = "";
    }

    /// <summary>
    /// Represents the result of a game
    /// </summary>
    public class GameResult
    {
        /// <summary>
        /// Map played
        /// </summary>
        public string Map { get; set; } = "";

        /// <summary>
        /// ID of the winning player
        /// </summary>
        public ulong WinnerId { get; set; }

        /// <summary>
        /// When the game was completed
        /// </summary>
        public DateTime CompletedAt { get; set; } = DateTime.Now;
    }
}