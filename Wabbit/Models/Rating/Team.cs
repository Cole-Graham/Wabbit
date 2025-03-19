using DSharpPlus.Entities;
using System;
using System.Collections.Generic;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a player team for scrimmages and tournaments
    /// </summary>
    public class Team
    {
        /// <summary>
        /// Unique identifier for the team
        /// </summary>
        public string TeamId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The team's name (must be unique)
        /// </summary>
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// The type of team (1v1, 2v2, etc.)
        /// </summary>
        public Wabbit.Models.TeamGameType TeamGameType { get; set; } = Wabbit.Models.TeamGameType.OneVOne;

        /// <summary>
        /// Constructor with name and game type using TeamGameType
        /// </summary>
        /// <param name="name">Team name</param>
        /// <param name="type">Team game type</param>
        public Team(string name, Wabbit.Models.TeamGameType type)
        {
            TeamName = name;
            TeamGameType = type;
        }

        /// <summary>
        /// Constructor with name and game type using RatingGameType (for backward compatibility)
        /// </summary>
        /// <param name="name">Team name</param>
        /// <param name="type">Team game type as RatingGameType</param>
        public Team(string name, RatingGameType type)
        {
            TeamName = name;
            TeamGameType = (Wabbit.Models.TeamGameType)(int)type;
        }

        /// <summary>
        /// Default constructor for serialization
        /// </summary>
        public Team()
        {
        }

        /// <summary>
        /// The team's current ELO rating (starting at 1200)
        /// </summary>
        public int Rating { get; set; } = 1200;

        /// <summary>
        /// The date and time when the team was created
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The date when the team name was last changed
        /// </summary>
        public DateTimeOffset? LastNameChangeDate { get; set; }

        /// <summary>
        /// The date when secondary players were last changed
        /// </summary>
        public DateTimeOffset? LastSecondaryChangeDate { get; set; }

        /// <summary>
        /// The date when substitute players were last changed
        /// </summary>
        public DateTimeOffset? LastSubstituteChangeDate { get; set; }

        /// <summary>
        /// Core players that cannot be changed
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<DiscordUser> CorePlayers { get; set; } = [];

        /// <summary>
        /// Secondary players that can be changed monthly
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<DiscordUser> SecondaryPlayers { get; set; } = [];

        /// <summary>
        /// Substitute players that can be changed weekly
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<DiscordUser> SubstitutePlayers { get; set; } = [];

        /// <summary>
        /// Team record - number of wins
        /// </summary>
        public int Wins { get; set; } = 0;

        /// <summary>
        /// Team record - number of losses
        /// </summary>
        public int Losses { get; set; } = 0;

        /// <summary>
        /// Win rate percentage
        /// </summary>
        public double WinRate => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) * 100 : 0;

        // Serialization-friendly player ID storage
        /// <summary>
        /// IDs of core players for serialization
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<PlayerInfo> CorePlayerInfo { get; set; } = [];

        /// <summary>
        /// IDs of secondary players for serialization
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<PlayerInfo> SecondaryPlayerInfo { get; set; } = [];

        /// <summary>
        /// IDs of substitute players for serialization
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public List<PlayerInfo> SubstitutePlayerInfo { get; set; } = [];

        /// <summary>
        /// The creator/owner of the team
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DiscordUser Creator { get; set; } = null!;

        /// <summary>
        /// Creator ID for serialization
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public ulong CreatorId { get; set; }

        /// <summary>
        /// Creator username for display
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
        public string CreatorUsername { get; set; } = string.Empty;

        /// <summary>
        /// Rating history for tracking changes over time
        /// </summary>
        public List<RatingChange> RatingHistory { get; set; } = [];

        /// <summary>
        /// Adds a core player to the team
        /// </summary>
        public void AddCorePlayer(DiscordUser player)
        {
            if (!CorePlayers.Contains(player))
            {
                CorePlayers.Add(player);
                CorePlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
            }
        }

        /// <summary>
        /// Adds a secondary player to the team
        /// </summary>
        public void AddSecondaryPlayer(DiscordUser player)
        {
            if (!SecondaryPlayers.Contains(player))
            {
                SecondaryPlayers.Add(player);
                SecondaryPlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
                LastSecondaryChangeDate = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Adds a substitute player to the team
        /// </summary>
        public void AddSubstitutePlayer(DiscordUser player)
        {
            if (!SubstitutePlayers.Contains(player))
            {
                SubstitutePlayers.Add(player);
                SubstitutePlayerInfo.Add(new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                });
                LastSubstituteChangeDate = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Changes the team name and updates the last change date
        /// </summary>
        public void ChangeName(string newName)
        {
            TeamName = newName;
            LastNameChangeDate = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Gets the required and maximum number of players for this team type
        /// </summary>
        public (int requiredCorePlayers, int maxSecondaryPlayers, int maxSubstitutePlayers) GetPlayerCounts()
        {
            return TeamGameType switch
            {
                Wabbit.Models.TeamGameType.OneVOne => (1, 0, 0),
                Wabbit.Models.TeamGameType.TwoVTwo => (2, 1, 0),
                Wabbit.Models.TeamGameType.ThreeVThree => (2, 1, 1),
                Wabbit.Models.TeamGameType.FourVFour => (3, 1, 1),
                _ => (0, 0, 0)
            };
        }

        /// <summary>
        /// Updates the team's rating based on a match result
        /// </summary>
        public void UpdateRating(int newRating, bool isWin, string opponentName)
        {
            int ratingChange = newRating - Rating;

            // Update win/loss record
            if (isWin)
                Wins++;
            else
                Losses++;

            // Add to rating history
            RatingHistory.Add(new RatingChange
            {
                OldRating = Rating,
                NewRating = newRating,
                Change = ratingChange,
                Opponent = opponentName,
                IsWin = isWin,
                Date = DateTimeOffset.UtcNow
            });

            // Update the current rating
            Rating = newRating;
        }

        /// <summary>
        /// Checks if the team has all required players
        /// </summary>
        public bool HasRequiredPlayers()
        {
            var (requiredCore, _, _) = GetPlayerCounts();
            return CorePlayers.Count >= requiredCore;
        }

        /// <summary>
        /// Check if a player is on this team (in any role)
        /// </summary>
        public bool HasPlayer(ulong userId)
        {
            return CorePlayers.Any(p => p.Id == userId) ||
                   SecondaryPlayers.Any(p => p.Id == userId) ||
                   SubstitutePlayers.Any(p => p.Id == userId);
        }

        /// <summary>
        /// Check if a player is on this team in a specific role
        /// </summary>
        public bool HasPlayerInRole(ulong userId, PlayerRole role)
        {
            return role switch
            {
                PlayerRole.Core => CorePlayers.Any(p => p.Id == userId),
                PlayerRole.Secondary => SecondaryPlayers.Any(p => p.Id == userId),
                PlayerRole.Substitute => SubstitutePlayers.Any(p => p.Id == userId),
                _ => false
            };
        }
    }

    /// <summary>
    /// Represents a change in a team's rating
    /// </summary>
    public class RatingChange
    {
        /// <summary>
        /// Rating before the change
        /// </summary>
        public int OldRating { get; set; }

        /// <summary>
        /// Rating after the change
        /// </summary>
        public int NewRating { get; set; }

        /// <summary>
        /// The amount of change (positive or negative)
        /// </summary>
        public int Change { get; set; }

        /// <summary>
        /// Name of the opponent
        /// </summary>
        public string Opponent { get; set; } = string.Empty;

        /// <summary>
        /// Whether this was a win
        /// </summary>
        public bool IsWin { get; set; }

        /// <summary>
        /// When the rating change occurred
        /// </summary>
        public DateTimeOffset Date { get; set; }
    }

    /// <summary>
    /// Player information for serialization
    /// </summary>
    public class PlayerInfo
    {
        /// <summary>
        /// Discord User ID
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// Discord Username
        /// </summary>
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a player's role in a team
    /// </summary>
    public enum PlayerRole
    {
        /// <summary>
        /// Core player, cannot be changed
        /// </summary>
        Core,

        /// <summary>
        /// Secondary player, can be changed monthly
        /// </summary>
        Secondary,

        /// <summary>
        /// Substitute player, can be changed weekly
        /// </summary>
        Substitute
    }
}