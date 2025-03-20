using DSharpPlus.Entities;
using System.Text;
using Wabbit.Models;
using System;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Helper methods for creating status embeds that are shared between the tournament and scrimmage systems
    /// </summary>
    public static class StatusEmbedHelpers
    {
        /// <summary>
        /// Creates a standard progress bar for match or scrimmage status
        /// </summary>
        /// <param name="currentStage">The current stage of the match or scrimmage</param>
        /// <returns>A formatted progress bar string</returns>
        public static string CreateStageProgressBar(MatchStage currentStage)
        {
            // Create the progress indicators with visually stronger indicators
            string mapBanEmoji = currentStage >= MatchStage.MapBan
                ? (currentStage > MatchStage.MapBan ? "✅" : "▶️")
                : "⬜";

            string deckSubmitEmoji = currentStage >= MatchStage.DeckSubmission
                ? (currentStage > MatchStage.DeckSubmission ? "✅" : "▶️")
                : "⬜";

            string gameResultsEmoji = currentStage >= MatchStage.GameResults
                ? (currentStage > MatchStage.GameResults ? "✅" : "▶️")
                : "⬜";

            // Create a horizontal progress bar with arrows
            return $"{mapBanEmoji} Map Bans ➜ {deckSubmitEmoji} Deck Submission ➜ {gameResultsEmoji} Game Results";
        }

        /// <summary>
        /// Gets a standardized stage-specific instruction text
        /// </summary>
        /// <param name="currentStage">The current stage</param>
        /// <returns>Instruction text appropriate for the stage</returns>
        public static string GetStageInstructions(MatchStage currentStage)
        {
            return currentStage switch
            {
                MatchStage.Created => "Both players need to ready up to begin the match.",
                MatchStage.MapBan => "Select maps to ban using the dropdown below, ordered by priority.",
                MatchStage.DeckSubmission => "Submit your deck using the appropriate command.",
                MatchStage.DeckRevision => "Please revise your deck submission.",
                MatchStage.GameResults => "Play your game and report the result when finished.",
                MatchStage.Completed => "Match is complete. Thank you for playing!",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets a standard color for the given stage
        /// </summary>
        /// <param name="currentStage">The current match stage</param>
        /// <returns>A color appropriate for the stage</returns>
        public static DiscordColor GetStageColor(MatchStage currentStage)
        {
            return currentStage switch
            {
                MatchStage.Created => DiscordColor.Yellow,
                MatchStage.MapBan => new DiscordColor(66, 134, 244),        // Blue
                MatchStage.DeckSubmission => new DiscordColor(255, 140, 0), // Orange
                MatchStage.DeckRevision => new DiscordColor(255, 165, 0),   // Light Orange
                MatchStage.GameResults => new DiscordColor(75, 181, 67),    // Green
                MatchStage.Completed => new DiscordColor(100, 100, 100),    // Gray
                _ => DiscordColor.Green // Default
            };
        }

        /// <summary>
        /// Creates a standard header for map bans display
        /// </summary>
        /// <param name="matchLength">The match length</param>
        /// <returns>A string explaining the map ban guarantees</returns>
        public static string GetMapBanGuaranteeExplanation(MatchLength matchLength)
        {
            return matchLength switch
            {
                MatchLength.Bo1 => "All bans are guaranteed in Bo1 matches",
                MatchLength.Bo3 => "First 2 bans are guaranteed in Bo3 matches",
                MatchLength.Bo5 => "First ban is guaranteed in Bo5 matches",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Convert match length to a readable string
        /// </summary>
        public static string GetMatchLengthString(MatchLength matchLength) => matchLength switch
        {
            MatchLength.Bo1 => "Best of 1",
            MatchLength.Bo3 => "Best of 3",
            MatchLength.Bo5 => "Best of 5",
            _ => "Unknown"
        };

        /// <summary>
        /// Helper method to get the number of games needed to win a match
        /// </summary>
        public static int GetGamesToWin(MatchLength matchLength) => matchLength switch
        {
            MatchLength.Bo3 => 2,
            MatchLength.Bo5 => 3,
            _ => 1 // Default to 1 for Bo1
        };

        /// <summary>
        /// Helper method to get number of map bans needed for a match length
        /// </summary>
        public static int GetMapBanCount(MatchLength matchLength) => matchLength switch
        {
            MatchLength.Bo5 => 2, // Best of 5 has 2 bans
            _ => 3               // Bo1 and Bo3 have 3 bans
        };
    }
}