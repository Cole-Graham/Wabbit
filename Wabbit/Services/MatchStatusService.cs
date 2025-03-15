using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of IMatchStatusService for managing match status display
    /// </summary>
    public class MatchStatusService : IMatchStatusService
    {
        private readonly ILogger<MatchStatusService> _logger;
        private readonly ITournamentMapService _mapService;
        private readonly Dictionary<ulong, ulong> _channelToMessageMap = new();

        public MatchStatusService(
            ILogger<MatchStatusService> logger,
            ITournamentMapService mapService)
        {
            _logger = logger;
            _mapService = mapService;
        }

        /// <summary>
        /// Gets the match status message
        /// </summary>
        public async Task<DiscordMessage?> GetMatchStatusMessageAsync(DiscordChannel channel, DiscordClient client)
        {
            // If we have a message ID for this channel, try to retrieve it
            if (_channelToMessageMap.TryGetValue(channel.Id, out ulong messageId))
            {
                try
                {
                    return await channel.GetMessageAsync(messageId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not get match status message: {ex.Message}");
                    _channelToMessageMap.Remove(channel.Id);
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates or updates the match status embed
        /// </summary>
        public async Task<DiscordMessage> UpdateMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            var existingMessage = await GetMatchStatusMessageAsync(channel, client);

            // Build the base embed
            var embed = CreateMatchStatusEmbed(round);

            var teamNames = round.Teams?.Select(t => t.Name ?? "Unknown Team").ToList() ?? new List<string> { "Team 1", "Team 2" };

            // Create buttons based on the current match stage
            var components = new List<DiscordComponent>();

            if (existingMessage is not null)
            {
                await existingMessage.ModifyAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(components));

                return existingMessage;
            }
            else
            {
                // Create a new message
                var messageBuilder = new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(components);

                var newMessage = await channel.SendMessageAsync(messageBuilder);
                _channelToMessageMap[channel.Id] = newMessage.Id;

                // Store the message ID in the round for tracking
                round.StatusMessageId = newMessage.Id;

                return newMessage;
            }
        }

        /// <summary>
        /// Creates a new match status embed for a new match, preserving history
        /// </summary>
        public async Task<DiscordMessage> CreateNewMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Remove any existing message mapping for this channel to ensure we create a new one
            if (_channelToMessageMap.ContainsKey(channel.Id))
            {
                _channelToMessageMap.Remove(channel.Id);
            }

            // Clear any existing status message ID
            round.StatusMessageId = null;

            // Build the base embed
            var embed = CreateMatchStatusEmbed(round);

            // Add a clear transition message for new matches in group stages
            if (round.GroupStageMatchNumber > 0 && round.TotalGroupStageMatches > 0)
            {
                string description = embed.Description ?? "";
                embed.Description = $"{description}\n\n**🆕 New Match {round.GroupStageMatchNumber} of {round.TotalGroupStageMatches}**";

                // Use a distinct color for a new match to make it visually different
                embed.WithColor(new DiscordColor(51, 102, 255)); // Bright blue for new match
            }

            // Create a fresh message
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embed);

            var newMessage = await channel.SendMessageAsync(messageBuilder);

            // Update mappings with the new message
            _channelToMessageMap[channel.Id] = newMessage.Id;
            round.StatusMessageId = newMessage.Id;

            return newMessage;
        }

        /// <summary>
        /// Ensures a match status message exists and is properly initialized
        /// </summary>
        public async Task<DiscordMessage> EnsureMatchStatusMessageExistsAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            var message = await GetMatchStatusMessageAsync(channel, client);

            if (message is null)
            {
                _logger.LogWarning($"Match status message not found in channel {channel.Id}. Creating a new one.");

                // Clear any existing status message ID since we're creating a new one
                round.StatusMessageId = null;

                // Check if this is a completed match - if so, we should create a new message
                if (round.IsCompleted)
                {
                    return await CreateNewMatchStatusAsync(channel, round, client);
                }

                // Recreate the message based on current round state
                message = await UpdateMatchStatusAsync(channel, round, client);

                // Update the message with the current stage
                switch (round.CurrentStage)
                {
                    case MatchStage.MapBan:
                        await UpdateToMapBanStageAsync(channel, round, client);
                        break;
                    case MatchStage.DeckSubmission:
                        await UpdateToDeckSubmissionStageAsync(channel, round, client);
                        break;
                    case MatchStage.GameResults:
                        await UpdateToGameResultsStageAsync(channel, round, client);
                        break;
                    default:
                        // Default to map ban stage if not specified
                        round.CurrentStage = MatchStage.MapBan;
                        await UpdateToMapBanStageAsync(channel, round, client);
                        break;
                }
            }

            return message;
        }

        /// <summary>
        /// Updates the status message for map ban stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToMapBanStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Update the round's current stage
            round.CurrentStage = MatchStage.MapBan;

            var message = await GetMatchStatusMessageAsync(channel, client);
            var embed = CreateMatchStatusEmbed(round);

            // Add progress bar to show current stage
            embed.AddField("Progress", GetMatchProgressBar(round.CurrentStage), false);

            // Get the map pool for this match type
            var mapPool = _mapService.GetTournamentMapPool(round.OneVOne);

            // Create map ban dropdown
            var mapSelectOptions = new List<DiscordSelectComponentOption>();
            if (mapPool is not null)
            {
                foreach (var mapName in mapPool.OrderBy(m => m))
                {
                    if (mapName is not null)
                    {
                        var option = new DiscordSelectComponentOption(mapName, mapName);
                        mapSelectOptions.Add(option);
                    }
                }
            }

            // Selection count depends on match length
            int selectionCount = round.Length == 5 ? 2 : 3; // Bo5 = 2 bans, others = 3 bans

            var mapBanDropdown = new DiscordSelectComponent(
                "map_ban_dropdown",
                "Select maps to ban",
                mapSelectOptions,
                false,
                selectionCount,
                selectionCount);

            // Build instructions based on match length
            string instructions = round.Length switch
            {
                1 => "Select 3 maps to ban in order of priority. Your first two bans are guaranteed, third is conditional.",
                3 => "Select 3 maps to ban in order of priority. Your first two bans are guaranteed, third is conditional.",
                5 => "Select 2 maps to ban in order of priority. Your first ban is guaranteed, second is conditional.",
                _ => "Select maps to ban in order of priority."
            };

            // Add map ban instructions section
            embed.AddField("Map Ban Instructions", instructions, false);

            // Add the dropdown component
            var components = new List<DiscordComponent> { mapBanDropdown };

            // Add map pool and other sections
            AddMapPoolField(embed, round);
            AddTeamMapBansField(embed, round);
            AddGameResultsArea(embed, round);
            AddDeckSubmissionsArea(embed, round);

            // Use blue color for map ban stage
            embed.WithColor(new DiscordColor(66, 134, 244));

            if (message is not null)
            {
                await message.ModifyAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(components));
                return message;
            }
            else
            {
                var newMessage = await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(components));

                _channelToMessageMap[channel.Id] = newMessage.Id;
                return newMessage;
            }
        }

        /// <summary>
        /// Updates the status message for deck submission stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToDeckSubmissionStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Update the round's current stage
            round.CurrentStage = MatchStage.DeckSubmission;

            var message = await GetMatchStatusMessageAsync(channel, client);
            var embed = CreateMatchStatusEmbed(round);

            // Add deck submission instructions with emoji
            embed.AddField("🃏 Current Stage: Deck Submission",
                "Submit your deck using the `/tournament submit_deck` command.");

            // Add progress bar to show current stage
            embed.AddField("Match Progress",
                "✅ Map Bans\n" +
                "▶️ Deck Submission\n" +
                "⬜ Game Results",
                false);

            // Use orange color for deck submission stage
            embed.WithColor(new DiscordColor(255, 140, 0));

            string description = embed.Description ?? "";
            embed.Description = $"{description}\n\n**Deck Submission Stage**\n" +
                               "Use `/tournament submit_deck` to submit your deck for the upcoming match.";

            // Add submit deck button for direct access to the command
            var submitDeckButton = new DiscordButtonComponent(
                DiscordButtonStyle.Primary,
                "submit_deck_button",
                "Submit Deck");

            if (message is not null)
            {
                await message.ModifyAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(submitDeckButton));
                return message;
            }
            else
            {
                var newMessage = await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(submitDeckButton));

                _channelToMessageMap[channel.Id] = newMessage.Id;
                return newMessage;
            }
        }

        /// <summary>
        /// Updates the status message for game results stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToGameResultsStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Update the round's current stage
            round.CurrentStage = MatchStage.GameResults;

            var message = await GetMatchStatusMessageAsync(channel, client);
            var embed = CreateMatchStatusEmbed(round);

            // Get participating players
            List<DiscordMember> players = new();
            if (round.Teams is not null)
            {
                foreach (var team in round.Teams)
                {
                    if (team?.Participants is not null)
                    {
                        foreach (var participant in team.Participants)
                        {
                            if (participant?.Player is not null)
                            {
                                players.Add(participant.Player);
                            }
                        }
                    }
                }
            }

            // Create game winner dropdown
            var options = new List<DiscordSelectComponentOption>();
            foreach (var player in players)
            {
                if (player is not null)
                {
                    options.Add(new DiscordSelectComponentOption(
                        player.Username ?? "Unknown User",
                        $"game_winner:{player.Id}"));
                }
            }

            // Add draw option if needed
            options.Add(new DiscordSelectComponentOption("Draw", "game_winner:draw"));

            var gameWinnerDropdown = new DiscordSelectComponent(
                "tournament_game_winner_dropdown",
                "Select Game Winner",
                options);

            // Add game results instructions with emoji
            embed.AddField("🏆 Current Stage: Report Game Result",
                "Select the winner of the game from the dropdown below.");

            // Add progress bar to show current stage
            embed.AddField("Match Progress",
                "✅ Map Bans\n" +
                "✅ Deck Submission\n" +
                "▶️ Game Results",
                false);

            // Use green color for game results stage
            embed.WithColor(new DiscordColor(75, 181, 67));

            string description = embed.Description ?? "";
            embed.Description = $"{description}\n\n**Game Results Stage**\n" +
                               "Select the winner of the current game from the dropdown.";

            if (message is not null)
            {
                await message.ModifyAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(gameWinnerDropdown));
                return message;
            }
            else
            {
                var newMessage = await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .AddComponents(gameWinnerDropdown));

                _channelToMessageMap[channel.Id] = newMessage.Id;
                return newMessage;
            }
        }

        /// <summary>
        /// Records a map ban selection and shows confirmation UI
        /// </summary>
        public async Task RecordMapBanAsync(DiscordChannel channel, Round round, string teamName, List<string> bannedMaps, DiscordClient client)
        {
            // Ensure the message exists before trying to update it
            var message = await EnsureMatchStatusMessageExistsAsync(channel, round, client);
            if (message is null) return;

            var embed = message.Embeds.FirstOrDefault();
            if (embed is null) return;

            // Create a new embed builder with the same base properties
            var builder = new DiscordEmbedBuilder()
                .WithTitle(embed.Title ?? "Match Status")
                .WithDescription(embed.Description ?? "")
                .WithColor(embed.Color ?? DiscordColor.NotQuiteBlack)
                .WithTimestamp(embed.Timestamp);

            // Add progress bar field
            builder.AddField("Progress", GetMatchProgressBar(round.CurrentStage), false);

            // Add confirmation instructions
            builder.AddField("Review Your Selections",
                "Click Confirm to lock in your choices or Revise to make changes.",
                false);

            // Show selected maps
            var selectedMapsField = new StringBuilder();
            for (int i = 0; i < bannedMaps.Count; i++)
            {
                selectedMapsField.AppendLine($"Priority #{i + 1}: {bannedMaps[i]}");
            }
            builder.AddField("Selected Maps", selectedMapsField.ToString(), false);

            // Update map pool and other sections
            AddMapPoolField(builder, round);
            AddTeamMapBansField(builder, round);
            AddGameResultsArea(builder, round);
            AddDeckSubmissionsArea(builder, round);

            // Create confirmation buttons
            var confirmBtn = new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                $"confirm_map_bans_{teamName}",
                "Confirm Bans");
            var reviseBtn = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"revise_map_bans_{teamName}",
                "Revise Bans");

            await message.ModifyAsync(new DiscordMessageBuilder()
                .AddEmbed(builder.Build())
                .AddComponents(confirmBtn, reviseBtn));
        }

        /// <summary>
        /// Updates the map pool field to show current selections
        /// </summary>
        private void UpdateMapPoolWithSelections(DiscordEmbedBuilder builder, Round round, List<string> selectedMaps)
        {
            // Update the team's map bans temporarily for display
            if (round.Teams is not null)
            {
                foreach (var team in round.Teams)
                {
                    if (team.MapBans is not null && team.MapBans.SequenceEqual(selectedMaps))
                    {
                        team.MapBans = selectedMaps;
                        break;
                    }
                }
            }

            // Add updated map pool
            AddMapPoolField(builder, round);
        }

        /// <summary>
        /// Records a deck submission
        /// </summary>
        public async Task RecordDeckSubmissionAsync(DiscordChannel channel, Round round, ulong playerId, string deckCode, int gameNumber, DiscordClient client)
        {
            // Ensure the message exists before trying to update it
            var message = await EnsureMatchStatusMessageExistsAsync(channel, round, client);
            if (message is null) return;

            var embed = message.Embeds.FirstOrDefault();
            if (embed is null) return;

            var builder = new DiscordEmbedBuilder(embed);

            // Find the player name
            string playerName = "Unknown Player";
            if (round?.Teams is not null)
            {
                foreach (var team in round.Teams)
                {
                    if (team?.Participants is not null)
                    {
                        var participant = team.Participants.FirstOrDefault(p => p?.Player?.Id == playerId);
                        if (participant?.Player is not null)
                        {
                            playerName = participant.Player.Username;
                            break;
                        }
                    }
                }
            }

            // Find if we already have a deck submissions field
            var deckField = builder.Fields?.FirstOrDefault(f => f?.Name != null && f.Name.Contains("Deck Submissions", StringComparison.OrdinalIgnoreCase));

            StringBuilder deckContent = new();
            if (deckField is not null && !string.IsNullOrEmpty(deckField.Value))
            {
                deckContent.AppendLine(deckField.Value);
            }

            // Add the new deck submission
            deckContent.AppendLine($"Game {gameNumber}: **{playerName}** submitted deck `{deckCode}`");

            // Create a new builder with all fields except the deck field, then add the updated field
            var newBuilder = new DiscordEmbedBuilder()
                .WithTitle(builder.Title ?? "Match Status")
                .WithDescription(builder.Description ?? "")
                .WithColor(builder.Color ?? DiscordColor.NotQuiteBlack)
                .WithTimestamp(builder.Timestamp);

            // Add all fields except the deck field we're updating
            if (builder.Fields is not null)
            {
                foreach (var field in builder.Fields)
                {
                    if (field is not null && field.Name is not null &&
                        !field.Name.Contains("Deck Submissions", StringComparison.OrdinalIgnoreCase))
                    {
                        newBuilder.AddField(field.Name, field.Value ?? "No content", field.Inline);
                    }
                }
            }

            // Add the updated deck submissions field
            newBuilder.AddField("Deck Submissions", deckContent.ToString() ?? "No submissions yet", false);

            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(newBuilder.Build()));
        }

        /// <summary>
        /// Records a game result
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
                resultsContent.AppendLine($"Game {gameNumber}: **Draw**");
            }
            else
            {
                resultsContent.AppendLine($"Game {gameNumber}: **{winnerName}** won");
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
        }

        /// <summary>
        /// Finalizes a match with results and awards points
        /// </summary>
        public async Task FinalizeMatchAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (round is null || round.Teams is null || round.Teams.Count < 2)
            {
                _logger.LogWarning("Cannot finalize match: Invalid round data");
                return;
            }

            // Check if we should create a message or update existing
            bool createNewMessage = round.StatusMessageId.HasValue &&
                await channel.GetMessageAsync(round.StatusMessageId.Value) is not null;

            // Calculate final scores
            int team1Score = round.Teams[0].Wins;
            int team2Score = round.Teams[1].Wins;
            string team1Name = round.Teams[0].Name ?? "Team 1";
            string team2Name = round.Teams[1].Name ?? "Team 2";

            // Set the match result
            round.MatchResult = $"**{team1Name}** {team1Score} - {team2Score} **{team2Name}**";

            // Determine winner and award points
            if (team1Score > team2Score)
            {
                // Team 1 wins
                round.PointsAwarded = 3;
                round.WinMsg = $"**{team1Name}** won the match ({team1Score} - {team2Score})";
            }
            else if (team2Score > team1Score)
            {
                // Team 2 wins
                round.PointsAwarded = 0; // From perspective of player 1
                round.WinMsg = $"**{team2Name}** won the match ({team2Score} - {team1Score})";
            }
            else
            {
                // Draw
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
        /// Creates the basic match status embed with common information
        /// </summary>
        private DiscordEmbedBuilder CreateMatchStatusEmbed(Round round)
        {
            string matchLength = round.Length switch
            {
                1 => "Best of 1",
                3 => "Best of 3",
                5 => "Best of 5",
                7 => "Best of 7",
                _ => $"Best of {round.Length}"
            };

            var teams = round.Teams?.Select(t => t.Name ?? "Unknown Team").ToList() ?? new List<string> { "Team 1", "Team 2" };
            string matchTitle = string.Join(" vs ", teams);

            // Add group stage information if available
            string groupStageInfo = "";
            if (round.TournamentRound && round.GroupStageMatchNumber > 0 && round.TotalGroupStageMatches > 0)
            {
                groupStageInfo = $"Group Stage: Match {round.GroupStageMatchNumber} of {round.TotalGroupStageMatches}";
            }

            // Set color based on match stage
            DiscordColor embedColor = round.CurrentStage switch
            {
                MatchStage.MapBan => new DiscordColor(66, 134, 244),        // Blue
                MatchStage.DeckSubmission => new DiscordColor(255, 140, 0), // Orange
                MatchStage.GameResults => new DiscordColor(75, 181, 67),    // Green
                MatchStage.Completed => new DiscordColor(100, 100, 100),    // Gray
                _ => new DiscordColor(75, 181, 67)                          // Default green
            };

            var builder = new DiscordEmbedBuilder()
                .WithTitle(groupStageInfo)
                .WithDescription($"{matchTitle}\n{matchLength}")
                .WithColor(embedColor)
                .WithTimestamp(DateTimeOffset.Now);

            // Add match progress bar
            string progressBar = GetMatchProgressBar(round.CurrentStage);
            builder.AddField("Progress", progressBar, false);

            // Add map pool with color coding
            AddMapPoolField(builder, round);

            // Add team map bans
            AddTeamMapBansField(builder, round);

            // Add deck submissions area if applicable
            if (round.CurrentStage >= MatchStage.DeckSubmission)
            {
                AddDeckSubmissionsArea(builder, round);
            }

            // Add game results area
            AddGameResultsArea(builder, round);

            // Add stage-specific instructions at the bottom
            AddStageInstructions(builder, round);

            return builder;
        }

        private string GetMatchProgressBar(MatchStage currentStage)
        {
            // Create a horizontal progress bar with arrows
            string mapBanEmoji = currentStage > MatchStage.MapBan ? "✅" : currentStage == MatchStage.MapBan ? "▶️" : "⬜";
            string deckSubmitEmoji = currentStage > MatchStage.DeckSubmission ? "✅" : currentStage == MatchStage.DeckSubmission ? "▶️" : "⬜";
            string gameResultsEmoji = currentStage == MatchStage.GameResults ? "▶️" : currentStage > MatchStage.GameResults ? "✅" : "⬜";

            return $"{mapBanEmoji} Map Bans ➜ {deckSubmitEmoji} Deck Submission ➜ {gameResultsEmoji} Game Results";
        }

        private void AddMapPoolField(DiscordEmbedBuilder builder, Round round)
        {
            var mapPool = _mapService.GetTournamentMapPool(round.OneVOne);
            if (mapPool is null || !mapPool.Any()) return;

            // Sort maps alphanumerically
            var sortedMaps = mapPool.OrderBy(m => m).ToList();

            // Always use 4 maps per row for consistency
            const int mapsPerRow = 4;
            var mapPoolBuilder = new StringBuilder();

            // Add legend at the top
            mapPoolBuilder.AppendLine("🟥 Guaranteed Ban  🟨 Potential Ban  🟦 Played  🟩 Available\n");

            // Calculate padding for consistent spacing
            int maxMapLength = sortedMaps.Max(m => m.Length);
            string padding = "  "; // Two spaces between maps

            for (int i = 0; i < sortedMaps.Count; i += mapsPerRow)
            {
                var rowMaps = sortedMaps.Skip(i).Take(mapsPerRow);
                foreach (var map in rowMaps)
                {
                    string status = GetMapStatusEmoji(round, map);
                    // Pad the map name to align the next map
                    string paddedMap = map.PadRight(maxMapLength);
                    mapPoolBuilder.Append($"{status} {paddedMap}{padding}");
                }
                mapPoolBuilder.AppendLine();
            }

            builder.AddField("🗺️ Map Pool", mapPoolBuilder.ToString().Trim(), false);
        }

        private string GetMapStatusEmoji(Round round, string map)
        {
            // Check if map has been played
            if (round.Maps?.Contains(map) == true)
                return "🟦"; // Blue for played maps

            // Check if map is banned by either team
            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                if (team.MapBans?.Contains(map) == true)
                {
                    // First ban is guaranteed in Bo5, all bans in Bo3
                    bool isGuaranteedBan = round.Length == 3 ||
                                          (round.Length == 5 && team.MapBans.IndexOf(map) == 0);
                    return isGuaranteedBan ? "🟥" : "🟨";
                }
            }

            return "🟩"; // Green for available maps
        }

        private void AddTeamMapBansField(DiscordEmbedBuilder builder, Round round)
        {
            if (round.Teams is null) return;

            var banBuilder = new StringBuilder();
            banBuilder.AppendLine("**Map Ban Status**");

            foreach (var team in round.Teams)
            {
                if (team is null) continue;

                if (team.MapBans?.Any() == true)
                {
                    banBuilder.AppendLine($"\n{team.Name}:");
                    // Show priority numbers clearly
                    for (int i = 0; i < team.MapBans.Count; i++)
                    {
                        string priority = i == 0 ? "1st" : i == 1 ? "2nd" : "3rd";
                        banBuilder.AppendLine($"• {priority} Priority Ban ✅");
                    }
                }
                else
                {
                    banBuilder.AppendLine($"\n{team.Name}: ⏳ Waiting for map bans");
                }
            }

            builder.AddField("🚫 Map Bans", banBuilder.ToString().Trim(), false);
        }

        private void AddDeckSubmissionsArea(DiscordEmbedBuilder builder, Round round)
        {
            if (round.CustomProperties?.ContainsKey("DeckCodes") != true) return;

            var deckBuilder = new StringBuilder();
            deckBuilder.AppendLine("**Deck Submission Status**");

            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is null) continue;

                    string userId = participant.Player.Id.ToString();
                    var deckCodes = round.CustomProperties["DeckCodes"] as Dictionary<string, Dictionary<string, string>>;
                    bool hasSubmitted = deckCodes?.Any(dc => dc.Value.ContainsKey(userId)) ?? false;

                    string status = hasSubmitted ? "✅ Deck submitted" : "⏳ Waiting for deck";
                    deckBuilder.AppendLine($"\n{team.Name}: {status}");

                    // Only show deck code to the submitting player
                    if (hasSubmitted && deckCodes?.TryGetValue(round.Name ?? "unknown", out var codes) == true &&
                        codes.TryGetValue(userId, out var code))
                    {
                        // Add deck code in a separate line with monospace formatting
                        deckBuilder.AppendLine($"Your deck code: `{code}`");
                    }
                }
            }

            builder.AddField("🃏 Deck Submissions", deckBuilder.ToString().Trim(), false);
        }

        private void AddGameResultsArea(DiscordEmbedBuilder builder, Round round)
        {
            if (round.Maps?.Any() != true)
            {
                builder.AddField("🎮 Game Results", "No games completed yet", false);
                return;
            }

            var resultsBuilder = new StringBuilder();
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

            builder.AddField("🎮 Game Results", resultsBuilder.ToString().Trim(), false);
        }

        private void AddStageInstructions(DiscordEmbedBuilder builder, Round round)
        {
            string instructions = round.CurrentStage switch
            {
                MatchStage.DeckSubmission => "Submit your deck using `/tournament submit_deck`.",
                MatchStage.DeckRevision => "Please submit your revised deck using `/tournament submit_deck`.",
                MatchStage.GameResults => "Select the winner from the dropdown below.",
                MatchStage.Completed => $"Match completed! {round.WinMsg}",
                _ => ""
            };

            if (!string.IsNullOrEmpty(instructions))
            {
                builder.AddField("📝 Instructions", instructions, false);
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
                        $"game_winner:{player1Id}",
                        $"{player1Name} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        $"{player2Name} wins",
                        $"game_winner:{player2Id}",
                        $"{player2Name} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        "Draw",
                        "game_winner:draw",
                        "This game ended in a draw"
                    )
                };

                // Create the dropdown component
                var winnerDropdown = new DiscordSelectComponent(
                    "tournament_game_winner_dropdown",
                    "Select game winner",
                    winnerOptions
                );

                // Send the dropdown to the channel
                var message = await channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"🎮 **Game {gameNumber}:** Select the winner")
                        .AddComponents(winnerDropdown)
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
        /// Updates the match status to show the current map and map pool
        /// </summary>
        public async Task UpdateMapInformationAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            try
            {
                var message = await GetMatchStatusMessageAsync(channel, client);
                if (message is null)
                {
                    _logger.LogWarning("Cannot update map information: No status message found");
                    return;
                }

                // Get available maps for the next game
                var availableMaps = _mapService.GetAvailableMapsForNextGame(round);
                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available for next game");
                    return;
                }

                // Select a random map for the next game
                var random = new Random();
                string nextMap = availableMaps[random.Next(availableMaps.Count)];

                // Update the round's current map
                if (round.CustomProperties is null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }
                round.CustomProperties["CurrentMap"] = nextMap;

                // Add the map to the round's map list if not already present
                if (round.Maps is null)
                {
                    round.Maps = new List<string>();
                }

                if (!round.Maps.Contains(nextMap))
                {
                    round.Maps.Add(nextMap);
                }

                // Update the status message with the new map information
                await UpdateMatchStatusAsync(channel, round, client);

                // Send a message announcing the next map
                await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent($"🎮 **Next Map:** {nextMap}")
                    .WithAllowedMentions(new List<IMention>())); // No mentions
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating map information");
            }
        }
    }
}