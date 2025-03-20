using DSharpPlus.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Wabbit.Models;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Helper methods for game result operations that are shared between tournament and scrimmage systems
    /// </summary>
    public static class GameResultHelpers
    {
        /// <summary>
        /// Creates a game winner dropdown for reporting results
        /// </summary>
        /// <param name="player1Name">Name of first player/team</param>
        /// <param name="player2Name">Name of second player/team</param>
        /// <param name="gameNumber">The game number (0-based index)</param>
        /// <param name="uniqueId">A unique identifier for the component (could be round or scrimmage hash)</param>
        /// <returns>A dropdown component for selecting the game winner</returns>
        public static DiscordSelectComponent CreateGameWinnerDropdown(
            string player1Name,
            string player2Name,
            int gameNumber,
            string uniqueId)
        {
            // Create winner options
            var options = new List<DiscordSelectComponentOption>
            {
                new DiscordSelectComponentOption(
                    $"{player1Name} Won",
                    $"game_winner:1:{gameNumber}:{uniqueId}",
                    $"{player1Name} is the winner of game {gameNumber + 1}",
                    false,
                    new DiscordComponentEmoji("🏆")),

                new DiscordSelectComponentOption(
                    $"{player2Name} Won",
                    $"game_winner:2:{gameNumber}:{uniqueId}",
                    $"{player2Name} is the winner of game {gameNumber + 1}",
                    false,
                    new DiscordComponentEmoji("🏆"))
            };

            // Create the dropdown component
            return new DiscordSelectComponent(
                $"select_game_winner_{uniqueId}_{gameNumber}",
                "Select Game Winner",
                options);
        }

        /// <summary>
        /// Creates winner selection buttons as an alternative to the dropdown
        /// </summary>
        /// <param name="player1Name">Name of first player/team</param>
        /// <param name="player2Name">Name of second player/team</param>
        /// <param name="uniqueId">A unique identifier for the components</param>
        /// <returns>List of buttons for selecting the winner</returns>
        public static List<DiscordComponent> CreateWinnerButtons(
            string player1Name,
            string player2Name,
            string uniqueId)
        {
            var buttons = new List<DiscordComponent>();

            // Player 1 win button
            var player1WinButton = new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                $"report_win_1_{uniqueId}",
                $"{player1Name} Won");

            // Player 2 win button
            var player2WinButton = new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                $"report_win_2_{uniqueId}",
                $"{player2Name} Won");

            buttons.Add(player1WinButton);
            buttons.Add(player2WinButton);

            return buttons;
        }

        /// <summary>
        /// Formats game results for display in an embed
        /// </summary>
        /// <param name="results">List of results (1 for player 1 win, 2 for player 2 win)</param>
        /// <param name="maps">List of maps played</param>
        /// <param name="player1Name">Name of first player/team</param>
        /// <param name="player2Name">Name of second player/team</param>
        /// <returns>Formatted string describing the game results</returns>
        public static string FormatGameResults(
            List<int>? results,
            List<string>? maps,
            string player1Name,
            string player2Name)
        {
            var resultsBuilder = new StringBuilder();

            // Handle case where both maps and results are empty or null
            if ((results == null || !results.Any()) && (maps == null || !maps.Any()))
            {
                resultsBuilder.AppendLine("No games completed yet");
                return resultsBuilder.ToString().Trim();
            }

            resultsBuilder.AppendLine("**Game History**");

            // Check if there are maps available
            int mapCount = maps?.Count ?? 0;
            int resultCount = results?.Count ?? 0;

            // Show each map with its result
            for (int i = 0; i < Math.Max(mapCount, resultCount); i++)
            {
                string mapName = i < mapCount ? maps![i] : "Unknown Map";
                string gameNumber = $"Game {i + 1}";

                // Determine the winner
                string result;
                if (i < resultCount)
                {
                    string winner = results![i] == 1 ? player1Name : player2Name;
                    result = $"Winner: **{winner}**";
                }
                else
                {
                    result = "⏳ In Progress";
                }

                // Format with tree structure
                resultsBuilder.AppendLine($"\n{gameNumber} • {mapName}");
                resultsBuilder.AppendLine($"└─ {result}");
            }

            return resultsBuilder.ToString().Trim();
        }

        /// <summary>
        /// Determines if the match is complete based on the score
        /// </summary>
        /// <param name="player1Score">Score of player 1</param>
        /// <param name="player2Score">Score of player 2</param>
        /// <param name="matchLength">Match length which determines games to win</param>
        /// <returns>True if the match is complete, false otherwise</returns>
        public static bool IsMatchComplete(int player1Score, int player2Score, MatchLength matchLength)
        {
            int gamesToWin = StatusEmbedHelpers.GetGamesToWin(matchLength);
            return player1Score >= gamesToWin || player2Score >= gamesToWin;
        }

        /// <summary>
        /// Gets the name of the winner of a match
        /// </summary>
        /// <param name="player1Score">Score of player 1</param>
        /// <param name="player2Score">Score of player 2</param>
        /// <param name="player1Name">Name of player 1</param>
        /// <param name="player2Name">Name of player 2</param>
        /// <param name="matchLength">Match length which determines games to win</param>
        /// <returns>Name of the winner, or null if the match is not complete</returns>
        public static string? GetMatchWinnerName(
            int player1Score,
            int player2Score,
            string player1Name,
            string player2Name,
            MatchLength matchLength)
        {
            if (!IsMatchComplete(player1Score, player2Score, matchLength))
            {
                return null;
            }

            int gamesToWin = StatusEmbedHelpers.GetGamesToWin(matchLength);

            if (player1Score >= gamesToWin)
            {
                return player1Name;
            }
            else if (player2Score >= gamesToWin)
            {
                return player2Name;
            }

            return null;
        }
    }
}