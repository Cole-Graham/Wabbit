using System;
using System.Collections.Generic;
using Wabbit.Models;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents a player's ratings across different game types
    /// </summary>
    public class PlayerRating
    {
        /// <summary>
        /// The Discord User ID of the player
        /// </summary>
        public ulong PlayerId { get; set; }

        /// <summary>
        /// The player's username (for display)
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Ratings for different game types
        /// </summary>
        public Dictionary<TeamGameType, int> Ratings { get; set; } = new Dictionary<TeamGameType, int>
        {
            { TeamGameType.OneVOne, 1200 },
            { TeamGameType.TwoVTwo, 1200 },
            { TeamGameType.ThreeVThree, 1200 },
            { TeamGameType.FourVFour, 1200 }
        };

        /// <summary>
        /// Separate tournament ratings
        /// </summary>
        public Dictionary<TeamGameType, int> TournamentRatings { get; set; } = new Dictionary<TeamGameType, int>
        {
            { TeamGameType.OneVOne, 1200 },
            { TeamGameType.TwoVTwo, 1200 },
            { TeamGameType.ThreeVThree, 1200 },
            { TeamGameType.FourVFour, 1200 }
        };

        /// <summary>
        /// Match record for each game type (wins)
        /// </summary>
        public Dictionary<TeamGameType, int> Wins { get; set; } = new Dictionary<TeamGameType, int>
        {
            { TeamGameType.OneVOne, 0 },
            { TeamGameType.TwoVTwo, 0 },
            { TeamGameType.ThreeVThree, 0 },
            { TeamGameType.FourVFour, 0 }
        };

        /// <summary>
        /// Match record for each game type (losses)
        /// </summary>
        public Dictionary<TeamGameType, int> Losses { get; set; } = new Dictionary<TeamGameType, int>
        {
            { TeamGameType.OneVOne, 0 },
            { TeamGameType.TwoVTwo, 0 },
            { TeamGameType.ThreeVThree, 0 },
            { TeamGameType.FourVFour, 0 }
        };

        /// <summary>
        /// History of rating changes
        /// </summary>
        public Dictionary<TeamGameType, List<PlayerRatingChange>> RatingHistory { get; set; } = new Dictionary<TeamGameType, List<PlayerRatingChange>>
        {
            { TeamGameType.OneVOne, new List<PlayerRatingChange>() },
            { TeamGameType.TwoVTwo, new List<PlayerRatingChange>() },
            { TeamGameType.ThreeVThree, new List<PlayerRatingChange>() },
            { TeamGameType.FourVFour, new List<PlayerRatingChange>() }
        };

        /// <summary>
        /// Gets the player's rating for a specific game type
        /// </summary>
        public int GetRating(TeamGameType TeamGameType)
        {
            return Ratings.TryGetValue(TeamGameType, out int rating) ? rating : 1200;
        }

        /// <summary>
        /// Gets the player's tournament rating for a specific game type
        /// </summary>
        public int GetTournamentRating(TeamGameType TeamGameType)
        {
            return TournamentRatings.TryGetValue(TeamGameType, out int rating) ? rating : 1200;
        }

        /// <summary>
        /// Updates the player's rating for a specific game type
        /// </summary>
        public void UpdateRating(TeamGameType TeamGameType, int newRating, bool isWin, string opponentName, string teamName = "")
        {
            if (!Ratings.TryGetValue(TeamGameType, out int currentRating))
            {
                currentRating = 1200;
                Ratings[TeamGameType] = currentRating;
            }

            // Calculate rating change
            int ratingChange = newRating - currentRating;

            // Update rating
            Ratings[TeamGameType] = newRating;

            // Update win/loss record
            if (isWin)
            {
                if (!Wins.ContainsKey(TeamGameType))
                    Wins[TeamGameType] = 0;
                Wins[TeamGameType]++;
            }
            else
            {
                if (!Losses.ContainsKey(TeamGameType))
                    Losses[TeamGameType] = 0;
                Losses[TeamGameType]++;
            }

            // Record rating history
            if (!RatingHistory.ContainsKey(TeamGameType))
                RatingHistory[TeamGameType] = new List<PlayerRatingChange>();

            RatingHistory[TeamGameType].Add(new PlayerRatingChange
            {
                OldRating = currentRating,
                NewRating = newRating,
                Change = ratingChange,
                Opponent = opponentName,
                TeamName = teamName,
                IsWin = isWin,
                Date = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Updates the player's tournament rating for a specific game type
        /// </summary>
        public void UpdateTournamentRating(TeamGameType TeamGameType, int newRating, bool isWin, string opponentName, string tournamentName)
        {
            if (!TournamentRatings.TryGetValue(TeamGameType, out int currentRating))
            {
                currentRating = 1200;
                TournamentRatings[TeamGameType] = currentRating;
            }

            // Calculate rating change
            int ratingChange = newRating - currentRating;

            // Update rating
            TournamentRatings[TeamGameType] = newRating;

            // Record rating history
            if (!RatingHistory.ContainsKey(TeamGameType))
                RatingHistory[TeamGameType] = new List<PlayerRatingChange>();

            RatingHistory[TeamGameType].Add(new PlayerRatingChange
            {
                OldRating = currentRating,
                NewRating = newRating,
                Change = ratingChange,
                Opponent = opponentName,
                IsTournamentMatch = true,
                TournamentName = tournamentName,
                IsWin = isWin,
                Date = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Gets the player's win rate for a specific game type
        /// </summary>
        public double GetWinRate(TeamGameType TeamGameType)
        {
            int wins = Wins.TryGetValue(TeamGameType, out int w) ? w : 0;
            int losses = Losses.TryGetValue(TeamGameType, out int l) ? l : 0;

            return wins + losses > 0 ? (double)wins / (wins + losses) * 100 : 0;
        }

        /// <summary>
        /// Gets the total matches played for a specific game type
        /// </summary>
        public int GetMatchesPlayed(TeamGameType TeamGameType)
        {
            int wins = Wins.TryGetValue(TeamGameType, out int w) ? w : 0;
            int losses = Losses.TryGetValue(TeamGameType, out int l) ? l : 0;

            return wins + losses;
        }

        /// <summary>
        /// Resets ratings to default (1200)
        /// </summary>
        public void ResetRatings()
        {
            foreach (TeamGameType type in Enum.GetValues(typeof(TeamGameType)))
            {
                Ratings[type] = 1200;
            }
        }

        /// <summary>
        /// Resets tournament ratings to default (1200)
        /// </summary>
        public void ResetTournamentRatings()
        {
            foreach (TeamGameType type in Enum.GetValues(typeof(TeamGameType)))
            {
                TournamentRatings[type] = 1200;
            }
        }
    }

    /// <summary>
    /// Represents a change in a player's rating
    /// </summary>
    public class PlayerRatingChange
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
        /// Name of the team (if applicable)
        /// </summary>
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// Whether this was a tournament match
        /// </summary>
        public bool IsTournamentMatch { get; set; }

        /// <summary>
        /// Name of the tournament (if applicable)
        /// </summary>
        public string TournamentName { get; set; } = string.Empty;

        /// <summary>
        /// Whether this was a win
        /// </summary>
        public bool IsWin { get; set; }

        /// <summary>
        /// When the rating change occurred
        /// </summary>
        public DateTimeOffset Date { get; set; }
    }
}