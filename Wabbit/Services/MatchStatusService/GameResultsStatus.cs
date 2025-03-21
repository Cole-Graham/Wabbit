using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Misc;

namespace Wabbit.Services
{
    public partial class MatchStatusService
    {
        /// <summary>
        /// Updates the status message for game results stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToGameResultsStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            try
            {
                _logger.LogInformation($"Updating to game results stage in channel {channel.Id}");

                // Update the round's current stage
                round.CurrentStage = MatchStage.GameResults;

                // Add custom instruction about selecting a winner
                if (round.CustomProperties == null)
                    round.CustomProperties = new Dictionary<string, object>();

                string mapName = round.Maps?.LastOrDefault() ?? "Unknown Map";
                round.CustomProperties["Instructions"] = $"The game will be played on **{mapName}**. After the match completes, select the winner using the dropdown below.";

                // Get the existing message or create a new one 
                var message = await GetMatchStatusMessageAsync(channel, client);

                if (message is null)
                {
                    _logger.LogWarning($"No existing message found in channel {channel.Id}, creating new one");
                    try
                    {
                        message = await CreateNewMatchStatusAsync(channel, round, client);
                        _logger.LogInformation($"Created new game results message in channel {channel.Id} with ID {message.Id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to create new message in channel {channel.Id}, attempting recovery");
                        // Try one more time with a basic approach
                        var embed = CreateMatchStatusEmbed(round);
                        var messageBuilder = new DiscordMessageBuilder().AddEmbed(embed);
                        message = await channel.SendMessageAsync(messageBuilder);

                        // Update mappings
                        _channelToMessageMap[channel.Id] = message.Id;
                        round.StatusMessageId = message.Id;
                    }
                }
                else
                {
                    _logger.LogInformation($"Found existing message in channel {channel.Id} with ID {message.Id}, updating");
                    try
                    {
                        await UpdateMatchStatusAsync(channel, round, client);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to update existing message in channel {channel.Id}, attempting recovery");
                        // Try direct modification as a fallback
                        var embed = CreateMatchStatusEmbed(round);
                        await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
                    }
                }

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in UpdateToGameResultsStageAsync for channel {channel.Id}");

                // Last resort recovery - create a basic message without the usual flow
                try
                {
                    var fallbackEmbed = new DiscordEmbedBuilder()
                        .WithTitle("Match Status")
                        .WithDescription($"Current Stage: {round.CurrentStage}")
                        .WithColor(new DiscordColor(75, 181, 67))
                        .AddField("Recovery Mode", "The status message has been restored after an error.");

                    var fallbackMessage = await channel.SendMessageAsync(
                        new DiscordMessageBuilder().AddEmbed(fallbackEmbed));

                    // Update mappings
                    _channelToMessageMap[channel.Id] = fallbackMessage.Id;
                    round.StatusMessageId = fallbackMessage.Id;

                    return fallbackMessage;
                }
                catch
                {
                    // If even this fails, just rethrow the original exception
                    throw;
                }
            }
        }

        /// <summary>
        /// Records a game result and updates match status
        /// </summary>
        public async Task RecordGameResultAsync(DiscordChannel channel, Round round, string winnerName, int gameNumber, DiscordClient client)
        {
            // Ensure the message exists before trying to update it
            var message = await EnsureMatchStatusMessageExistsAsync(channel, round, client);
            if (message is null) return;

            var embed = message.Embeds.FirstOrDefault();
            if (embed is null) return;

            var builder = new DiscordEmbedBuilder(embed);

            // Find if we already have a game results field
            var resultsField = builder.Fields?.FirstOrDefault(f => f?.Name != null && f.Name.Contains("Game Results", StringComparison.OrdinalIgnoreCase));

            StringBuilder resultsContent = new();
            if (resultsField is not null && !string.IsNullOrEmpty(resultsField.Value))
            {
                resultsContent.AppendLine(resultsField.Value);
            }

            // Add the new result
            if (winnerName.Equals("draw", StringComparison.OrdinalIgnoreCase))
            {
                resultsContent.AppendLine($"Game {gameNumber + 1}: **Draw**");
            }
            else
            {
                resultsContent.AppendLine($"Game {gameNumber + 1}: **{winnerName}** won");
            }

            // Create a new builder with all fields except the results field, then add the updated field
            var newBuilder = new DiscordEmbedBuilder()
                .WithTitle(builder.Title ?? "Match Status")
                .WithDescription(builder.Description ?? "")
                .WithColor(builder.Color ?? DiscordColor.NotQuiteBlack)
                .WithTimestamp(builder.Timestamp);

            // Add all fields except the results field we're updating
            if (builder.Fields is not null)
            {
                foreach (var field in builder.Fields)
                {
                    if (field is not null && field.Name is not null &&
                        !field.Name.Contains("Game Results", StringComparison.OrdinalIgnoreCase))
                    {
                        newBuilder.AddField(field.Name, field.Value ?? "No content", field.Inline);
                    }
                }
            }

            // Add the updated game results field
            newBuilder.AddField("Game Results", resultsContent.ToString() ?? "No results yet", false);

            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(newBuilder.Build()));

            // Also update in all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }
        }

        /// <summary>
        /// Finalizes a match with results and awards points
        /// </summary>
        public async Task FinalizeMatchAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (round is null) throw new ArgumentNullException(nameof(round));
            if (client is null) throw new ArgumentNullException(nameof(client));

            if (round.Teams == null || round.Teams.Count < 2)
            {
                _logger.LogWarning("Cannot finalize match: Invalid round data");
                return;
            }

            // Check if we should create a message or update existing
            bool createNewMessage = round.StatusMessageId.HasValue &&
                await channel.GetMessageAsync(round.StatusMessageId.Value) is not null;

            // Calculate final scores
            var team1 = round.Teams[0];
            var team2 = round.Teams[1];

            if (team1 == null || team2 == null)
            {
                _logger.LogWarning("Cannot finalize match: Invalid team data");
                return;
            }

            int team1Score = team1.Wins;
            int team2Score = team2.Wins;
            string team1Name = team1.Name ?? "Team 1";
            string team2Name = team2.Name ?? "Team 2";

            // Set the match result
            round.MatchResult = $"**{team1Name}** {team1Score} - {team2Score} **{team2Name}**";

            // Determine winner and award points
            if (team1Score > team2Score)
            {
                round.PointsAwarded = 3;
                round.WinMsg = $"**{team1Name}** won the match ({team1Score} - {team2Score})";
            }
            else if (team2Score > team1Score)
            {
                round.PointsAwarded = 0;
                round.WinMsg = $"**{team2Name}** won the match ({team2Score} - {team1Score})";
            }
            else
            {
                round.PointsAwarded = 1;
                round.WinMsg = $"The match ended in a draw ({team1Score} - {team2Score})";
            }

            // Mark as completed
            round.IsCompleted = true;
            round.CurrentStage = MatchStage.Completed;

            // Build a special finalized embed
            var embed = CreateMatchStatusEmbed(round);

            // Add summary of games
            StringBuilder gamesSummary = new();
            if (round.Maps?.Count > 0)
            {
                for (int i = 0; i < round.Maps.Count; i++)
                {
                    if (i < round.Maps.Count)
                    {
                        gamesSummary.AppendLine($"Game {i + 1}: {round.Maps[i]}");
                    }
                }
            }

            if (gamesSummary.Length > 0)
            {
                embed.AddField("Games Played", gamesSummary.ToString(), false);
            }

            // Add map bans summary
            StringBuilder mapBansSummary = new();
            if (round.Teams != null)
            {
                foreach (var team in round.Teams)
                {
                    if (team.MapBans?.Count > 0)
                    {
                        mapBansSummary.AppendLine($"**{team.Name}** banned: {string.Join(", ", team.MapBans)}");
                    }
                }
            }

            if (mapBansSummary.Length > 0)
            {
                embed.AddField("Map Bans", mapBansSummary.ToString(), false);
            }

            // Make the completion more visually distinct
            embed.WithColor(new DiscordColor(46, 204, 113)); // Green for completed match

            // Create a more visually distinct final result section
            StringBuilder resultBuilder = new();
            resultBuilder.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            resultBuilder.AppendLine($"🏁 **MATCH COMPLETE** 🏁");
            resultBuilder.AppendLine($"{round.WinMsg}");
            resultBuilder.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            // Add final result at the bottom
            embed.AddField("Match Result", resultBuilder.ToString(), false);

            // Add group stage context if this is a group stage match
            if (round.GroupStageMatchNumber > 0 && round.TotalGroupStageMatches > 0)
            {
                if (round.GroupStageMatchNumber < round.TotalGroupStageMatches)
                {
                    // This isn't the final match of the group stage
                    int remainingMatches = round.TotalGroupStageMatches - round.GroupStageMatchNumber;
                    embed.AddField("Group Stage Progress",
                        $"✅ Completed: Match {round.GroupStageMatchNumber} of {round.TotalGroupStageMatches}\n" +
                        $"⏭️ Next: {remainingMatches} more match(es) to play in this group",
                        false);
                }
                else
                {
                    // This is the final match of the group stage
                    embed.AddField("Group Stage Progress",
                        $"🏆 **Group Stage Complete!**\n" +
                        $"You have completed all {round.TotalGroupStageMatches} matches in your group stage.",
                        false);
                }
            }

            // Add archiving information
            embed.AddField("📊 Match History",
                "This match record will be preserved in your thread history. " +
                "The thread will be archived after 24 hours of inactivity.\n\n" +
                $"**Match ID:** {round.GetHashCode()}\n" +
                $"**Completed:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}", false);

            // If we should create a new message to preserve history
            if (createNewMessage)
            {
                await CreateNewMatchStatusAsync(channel, round, client);
            }

            // Get the current status message
            var message = await GetMatchStatusMessageAsync(channel, client);
            if (message is null)
            {
                // Create a new message if none exists
                var newMessage = await channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed.Build()));
                _channelToMessageMap[channel.Id] = newMessage.Id;
                round.StatusMessageId = newMessage.Id;
            }
            else
            {
                // Update the existing message
                await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed.Build()));
            }

            // Send a summary message to the channel
            await channel.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent($"📊 **Match Complete!** 📊\n{round.WinMsg}\n\n" +
                            "🔒 This match thread will be archived after 24 hours of inactivity. " +
                            "All match records will remain visible in the thread history.")
                .WithAllowedMentions(new List<IMention>())); // No mentions
        }

        /// <summary>
        /// Adds a visual separator between matches in group stages
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="client">The Discord client</param>
        /// <param name="nextMatchNumber">The next match number in the sequence</param>
        /// <param name="totalMatches">Total matches in the group stage</param>
        /// <param name="nextOpponentName">The name of the next opponent</param>
        /// <returns>The separator message that was sent, or null if sending failed</returns>
        public async Task<DiscordMessage?> AddMatchSeparatorAsync(
            DiscordChannel channel,
            DiscordClient client,
            int nextMatchNumber,
            int totalMatches,
            string nextOpponentName)
        {
            try
            {
                // Create a more visually distinct separator with better context
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🔄 Group Stage: Match {nextMatchNumber} of {totalMatches}")
                    .WithDescription($"**Next Opponent:** {nextOpponentName}")
                    .WithColor(new DiscordColor(255, 165, 0)) // Orange for transitions
                    .WithFooter($"Group Stage Progress: {nextMatchNumber}/{totalMatches}")
                    .WithTimestamp(DateTimeOffset.Now);

                // Create a visually distinct separator text
                string separatorContent =
                    "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    $"🆕 **NEW MATCH STARTING ({nextMatchNumber}/{totalMatches})** 🆕\n" +
                    "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

                // Send the separator message with both text and embed for maximum visibility
                var separatorMsg = await channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent(separatorContent)
                        .AddEmbed(embed));

                return separatorMsg;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add match separator");
                return null;
            }
        }

        /// <summary>
        /// Creates and sends a game winner dropdown for the next game
        /// </summary>
        /// <param name="channel">The match thread channel</param>
        /// <param name="round">The tournament round</param>
        /// <param name="client">The Discord client</param>
        /// <param name="gameNumber">The game number</param>
        /// <param name="player1Name">First player's name</param>
        /// <param name="player2Name">Second player's name</param>
        /// <param name="player1Id">First player's Discord ID</param>
        /// <param name="player2Id">Second player's Discord ID</param>
        /// <returns>The sent dropdown message</returns>
        public async Task<DiscordMessage> CreateGameWinnerDropdownAsync(
            DiscordChannel channel,
            Round round,
            DiscordClient client,
            int gameNumber,
            string player1Name,
            string player2Name,
            ulong player1Id,
            ulong player2Id)
        {
            try
            {
                _logger.LogInformation($"Creating game winner dropdown for game {gameNumber}");

                // Create winner selection options
                var winnerOptions = new List<DiscordSelectComponentOption>
                {
                    new DiscordSelectComponentOption(
                        $"{player1Name} wins",
                        $"{player1Id}",
                        $"{player1Name} is the winner of this game",
                        false,
                        new DiscordComponentEmoji("🏆")),
                    new DiscordSelectComponentOption(
                        $"{player2Name} wins",
                        $"{player2Id}",
                        $"{player2Name} is the winner of this game",
                        false,
                        new DiscordComponentEmoji("🏆")),
                    new DiscordSelectComponentOption(
                        "Draw (no winner)",
                        "draw",
                        "The game ended in a draw",
                        false,
                        new DiscordComponentEmoji("🤝"))
                };

                // Create dropdown for winner selection
                var winnerDropdown = new DiscordSelectComponent(
                    $"game_winner:{round.Name}:{gameNumber}",
                    "Select Winner",
                    winnerOptions
                );

                // Send the dropdown to the channel (with 1-based indexing for display)
                var message = await channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"🎮 **Game {gameNumber + 1}:** Select the winner")
                        .AddComponents(winnerDropdown)
                        .WithAllowedMentions(new List<IMention>()) // No mentions
                );

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating game winner dropdown: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Adds the game results area to the embed with a divider
        /// </summary>
        private void AddGameResultsAreaWithDivider(DiscordEmbedBuilder builder, Round round)
        {
            var resultsBuilder = new StringBuilder();

            if (round.Maps?.Any() != true)
            {
                resultsBuilder.AppendLine("No games completed yet");
                // Add divider at the end
                resultsBuilder.AppendLine("_______________________________________________");
                builder.AddField("🎮 Game Results", resultsBuilder.ToString().Trim(), false);
                return;
            }

            resultsBuilder.AppendLine("**Game History**");

            for (int i = 0; i < round.Maps.Count; i++)
            {
                string winner;
                if (round.CustomProperties?.ContainsKey("GameWinners") == true &&
                    round.CustomProperties["GameWinners"] is Dictionary<int, string> gameWinners &&
                    gameWinners.TryGetValue(i, out var winnerName))
                {
                    winner = winnerName;
                }
                else
                {
                    winner = "In Progress";
                }

                string gameNumber = $"Game {i + 1}";
                string mapName = round.Maps[i];
                string result = winner == "In Progress" ? "⏳ In Progress" : $"Winner: **{winner}**";

                resultsBuilder.AppendLine($"\n{gameNumber} • {mapName}");
                resultsBuilder.AppendLine($"└─ {result}");
            }

            // Add divider at the end
            resultsBuilder.AppendLine("_______________________________________________");

            builder.AddField("🎮 Game Results", resultsBuilder.ToString().Trim(), false);
        }
    }
}
