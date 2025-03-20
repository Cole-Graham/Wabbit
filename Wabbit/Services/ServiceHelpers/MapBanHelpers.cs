using DSharpPlus.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Wabbit.Models;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Helper methods for map ban operations that are shared between tournament and scrimmage systems
    /// </summary>
    public static class MapBanHelpers
    {
        /// <summary>
        /// Creates a standard map ban dropdown component
        /// </summary>
        /// <param name="availableMaps">List of available maps</param>
        /// <param name="matchLength">Match length to determine ban count</param>
        /// <param name="uniqueId">A unique identifier for the component (could be round or scrimmage hash)</param>
        /// <returns>A Discord select component for map bans</returns>
        public static DiscordSelectComponent CreateMapBanDropdown(
            List<string> availableMaps,
            MatchLength matchLength,
            string uniqueId)
        {
            // Determine the number of bans
            int numBans = StatusEmbedHelpers.GetMapBanCount(matchLength);

            // Create the dropdown
            return new DiscordSelectComponent(
                $"map_ban_{uniqueId}",
                $"Select {numBans} maps to ban (in order of priority)",
                availableMaps.Select(m => new DiscordSelectComponentOption(m, m)),
                false,
                minOptions: numBans,
                maxOptions: numBans);
        }

        /// <summary>
        /// Creates standard confirmation buttons for map ban selections
        /// </summary>
        /// <param name="teamIdentifier">An identifier for the team (name or number)</param>
        /// <param name="uniqueId">A unique identifier for the components (could be round or scrimmage hash)</param>
        /// <returns>List of buttons for map ban confirmation</returns>
        public static List<DiscordComponent> CreateMapBanConfirmButtons(string teamIdentifier, string uniqueId)
        {
            var buttons = new List<DiscordComponent>();

            // Create confirm button
            var confirmButton = new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                $"confirm_map_bans_{teamIdentifier}_{uniqueId}",
                "Confirm Map Bans",
                emoji: new DiscordComponentEmoji("✅"));

            // Create revise button
            var reviseButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"revise_map_bans_{teamIdentifier}_{uniqueId}",
                "Revise Map Bans",
                emoji: new DiscordComponentEmoji("🔄"));

            buttons.Add(confirmButton);
            buttons.Add(reviseButton);

            return buttons;
        }

        /// <summary>
        /// Formats map bans for display in an embed
        /// </summary>
        /// <param name="team1Name">Name of first team</param>
        /// <param name="team1MapBans">Map bans for first team</param>
        /// <param name="team2Name">Name of second team</param>
        /// <param name="team2MapBans">Map bans for second team</param>
        /// <param name="matchLength">Match length to determine ban guarantees</param>
        /// <returns>Formatted string describing the map bans</returns>
        public static string FormatMapBans(
            string team1Name,
            List<string> team1MapBans,
            string team2Name,
            List<string> team2MapBans,
            MatchLength matchLength)
        {
            var builder = new StringBuilder();

            // Team 1 bans
            if (team1MapBans?.Any() == true)
            {
                builder.AppendLine($"**{team1Name} Bans:** ✅");
                builder.AppendLine("```");
                for (int i = 0; i < team1MapBans.Count; i++)
                {
                    builder.AppendLine($"Priority #{i + 1}: {team1MapBans[i]}");
                }
                builder.AppendLine("```");
            }
            else
            {
                builder.AppendLine($"**{team1Name} Bans:** Not submitted yet");
            }

            // Team 2 bans
            if (team2MapBans?.Any() == true)
            {
                builder.AppendLine($"**{team2Name} Bans:** ✅");
                builder.AppendLine("```");
                for (int i = 0; i < team2MapBans.Count; i++)
                {
                    builder.AppendLine($"Priority #{i + 1}: {team2MapBans[i]}");
                }
                builder.AppendLine("```");
            }
            else
            {
                builder.AppendLine($"**{team2Name} Bans:** Not submitted yet");
            }

            // Add guarantee information
            builder.AppendLine();
            builder.AppendLine(StatusEmbedHelpers.GetMapBanGuaranteeExplanation(matchLength));

            return builder.ToString();
        }

        /// <summary>
        /// Determines the map status emoji for visual display in the map pool
        /// </summary>
        /// <param name="map">Map name</param>
        /// <param name="guaranteedBans">List of guaranteed banned maps</param>
        /// <param name="potentialBans">List of conditional banned maps</param>
        /// <param name="playedMaps">List of maps that have been played</param>
        /// <returns>An emoji representing the map status</returns>
        public static string GetMapStatusEmoji(
            string map,
            List<string> guaranteedBans,
            List<string> potentialBans,
            List<string> playedMaps)
        {
            if (playedMaps?.Contains(map) == true)
            {
                return "🟦"; // Blue for played maps
            }

            if (guaranteedBans?.Contains(map) == true)
            {
                return "🟥"; // Red for guaranteed bans
            }

            if (potentialBans?.Contains(map) == true)
            {
                return "🟨"; // Yellow for potential/conditional bans
            }

            return "🟩"; // Green for available maps
        }

        /// <summary>
        /// Formats map bans for display in an embed from a specific team's perspective
        /// </summary>
        /// <param name="userTeamBans">Map bans for the viewing team</param>
        /// <param name="opponentHasSubmitted">Whether the opponent has submitted bans</param>
        /// <param name="userTeamUnconfirmedBans">Unconfirmed map bans for the viewing team</param>
        /// <param name="matchLength">Match length to determine ban guarantees</param>
        /// <returns>Formatted string describing the map bans from team perspective</returns>
        public static string FormatTeamPerspectiveBans(
            List<string> userTeamBans,
            bool opponentHasSubmitted,
            List<string>? userTeamUnconfirmedBans = null,
            MatchLength matchLength = MatchLength.Bo3)
        {
            var builder = new StringBuilder();

            // First handle user's team bans (confirmed or unconfirmed)
            if (userTeamUnconfirmedBans?.Any() == true)
            {
                builder.AppendLine("My Team Map Bans (unconfirmed):");
                builder.AppendLine("```");
                builder.AppendLine("Priority #1          Priority #2          Priority #3");

                // Create a single line with fixed-width spacing
                var mapLine = new StringBuilder();
                for (int i = 0; i < userTeamUnconfirmedBans.Count; i++)
                {
                    string mapName = userTeamUnconfirmedBans[i];
                    mapLine.Append(mapName.PadRight(20));
                }
                builder.AppendLine(mapLine.ToString());
                builder.AppendLine("```");
            }
            else if (userTeamBans?.Any() == true)
            {
                builder.AppendLine("My Team Map Bans: ✅");
                builder.AppendLine("```");
                builder.AppendLine("Priority #1          Priority #2          Priority #3");

                var mapLine = new StringBuilder();
                for (int i = 0; i < userTeamBans.Count; i++)
                {
                    string mapName = userTeamBans[i];
                    mapLine.Append(mapName.PadRight(20));
                }
                builder.AppendLine(mapLine.ToString());
                builder.AppendLine("```");
            }
            else
            {
                builder.AppendLine("My Team Map Bans: Not submitted yet");
            }

            // Now handle opponent's bans - don't show specific maps
            builder.AppendLine();
            if (opponentHasSubmitted)
            {
                builder.AppendLine("Opponent Map Bans: ✅ Submitted");
            }
            else
            {
                builder.AppendLine("Opponent Map Bans: ⏳ Waiting for submission");
            }

            // Add guarantee explanation
            builder.AppendLine();
            builder.AppendLine(StatusEmbedHelpers.GetMapBanGuaranteeExplanation(matchLength));

            return builder.ToString();
        }
    }
}