using System;
using System.Collections.Generic;

namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Represents an individual player's 1v1 rating.
    /// Player ratings are only tracked for 1v1 games; team-based formats use Team ratings.
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
        /// The player's 1v1 rating
        /// </summary>
        public int Rating { get; set; } = 1200;

        /// <summary>
        /// The player's tournament 1v1 rating
        /// </summary>
        public int TournamentRating { get; set; } = 1200;

        /// <summary>
        /// The player's 1v1 win count
        /// </summary>
        public int Wins { get; set; } = 0;

        /// <summary>
        /// The player's 1v1 loss count
        /// </summary>
        public int Losses { get; set; } = 0;

        /// <summary>
        /// History of rating changes
        /// </summary>
        public List<PlayerRatingChange> RatingHistory { get; set; } = new List<PlayerRatingChange>();

        /// <summary>
        /// Gets the player's win rate percentage
        /// </summary>
        public double WinRate => Wins + Losses > 0 ? (double)Wins / (Wins + Losses) * 100 : 0;

        /// <summary>
        /// Gets the total number of matches played
        /// </summary>
        public int MatchesPlayed => Wins + Losses;

        /// <summary>
        /// Updates the player's 1v1 rating
        /// </summary>
        public void UpdateRating(int newRating, bool isWin, string opponentName, string teamName = "")
        {
            int ratingChange = newRating - Rating;

            // Update win/loss record
            if (isWin)
                Wins++;
            else
                Losses++;

            // Record rating history
            RatingHistory.Add(new PlayerRatingChange
            {
                OldRating = Rating,
                NewRating = newRating,
                Change = ratingChange,
                Opponent = opponentName,
                TeamName = teamName,
                IsWin = isWin,
                Date = DateTimeOffset.UtcNow
            });

            // Update the current rating
            Rating = newRating;
        }

        /// <summary>
        /// Updates the player's tournament 1v1 rating
        /// </summary>
        public void UpdateTournamentRating(int newRating, bool isWin, string opponentName, string tournamentName)
        {
            int ratingChange = newRating - TournamentRating;

            // Record rating history
            RatingHistory.Add(new PlayerRatingChange
            {
                OldRating = TournamentRating,
                NewRating = newRating,
                Change = ratingChange,
                Opponent = opponentName,
                IsTournamentMatch = true,
                TournamentName = tournamentName,
                IsWin = isWin,
                Date = DateTimeOffset.UtcNow
            });

            // Update the tournament rating
            TournamentRating = newRating;
        }

        /// <summary>
        /// Resets ratings to default (1200)
        /// </summary>
        public void ResetRatings()
        {
            Rating = 1200;
        }

        /// <summary>
        /// Resets tournament ratings to default (1200)
        /// </summary>
        public void ResetTournamentRatings()
        {
            TournamentRating = 1200;
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