using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;
using DSharpPlus.Interactivity;

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
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
        }

        /// <summary>
        /// Gets the match status message
        /// </summary>
        public async Task<DiscordMessage?> GetMatchStatusMessageAsync(DiscordChannel channel, DiscordClient client)
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (client is null) throw new ArgumentNullException(nameof(client));

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
            try
            {
                var existingMessage = await GetMatchStatusMessageAsync(channel, client);

                // Get map pool once and reuse it
                var mapPool = round.CurrentStage == MatchStage.MapBan ? _mapService.GetTournamentMapPool(round.OneVOne) : null;

                if (existingMessage is not null)
                {
                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(CreateMatchStatusEmbed(round, mapPool));

                    // Only add components if we're in an interactive stage and have valid components to add
                    // AND we haven't already added them (check message components)
                    if (round.CurrentStage != MatchStage.Created &&
                        round.CurrentStage != MatchStage.Completed &&
                        (existingMessage.Components?.Any() != true))
                    {
                        // Validate stage-specific requirements before adding components
                        switch (round.CurrentStage)
                        {
                            case MatchStage.MapBan:
                                if (mapPool?.Any() == true)
                                {
                                    var availableMaps = mapPool.Where(m => !round.Maps.Contains(m));
                                    if (availableMaps.Any())
                                    {
                                        // Bo1 matches (including group stage) and Bo3 matches have 3 bans
                                        // Bo5 matches have 2 bans
                                        int numBans = round.Length == 5 ? 2 : 3;

                                        builder.AddComponents(new DiscordSelectComponent(
                                            $"map_ban_{round.GetHashCode()}",
                                            $"Select {numBans} maps to ban (in order of priority)",
                                            availableMaps.Select(m => new DiscordSelectComponentOption(m, m)),
                                            false,
                                            minOptions: numBans,
                                            maxOptions: numBans
                                        ));
                                    }
                                }
                                break;
                            case MatchStage.DeckSubmission:
                                // Only add deck submission button if decks haven't been submitted
                                if (round.Teams?.Any() != true || !round.Teams.All(t => t.HasSubmittedDeck))
                                {
                                    builder.AddComponents(new DiscordButtonComponent(
                                        DiscordButtonStyle.Primary,
                                        $"submit_deck_{round.GetHashCode()}",
                                        "Submit Deck"
                                    ));
                                }
                                break;
                            case MatchStage.GameResults:
                                // Game results components are handled separately
                                break;
                        }
                    }

                    await existingMessage.ModifyAsync(builder);
                    return existingMessage;
                }
                else
                {
                    var messageBuilder = new DiscordMessageBuilder()
                        .AddEmbed(CreateMatchStatusEmbed(round, mapPool));

                    // Same component logic for new messages
                    if (round.CurrentStage != MatchStage.Created &&
                        round.CurrentStage != MatchStage.Completed)
                    {
                        // Validate stage-specific requirements before adding components
                        switch (round.CurrentStage)
                        {
                            case MatchStage.MapBan:
                                if (mapPool?.Any() == true)
                                {
                                    var availableMaps = mapPool.Where(m => !round.Maps.Contains(m));
                                    if (availableMaps.Any())
                                    {
                                        // Bo1 matches (including group stage) and Bo3 matches have 3 bans
                                        // Bo5 matches have 2 bans
                                        int numBans = round.Length == 5 ? 2 : 3;

                                        messageBuilder.AddComponents(new DiscordSelectComponent(
                                            $"map_ban_{round.GetHashCode()}",
                                            $"Select {numBans} maps to ban (in order of priority)",
                                            availableMaps.Select(m => new DiscordSelectComponentOption(m, m)),
                                            false,
                                            minOptions: numBans,
                                            maxOptions: numBans
                                        ));
                                    }
                                }
                                break;
                            case MatchStage.DeckSubmission:
                                // Only add deck submission button if decks haven't been submitted
                                if (round.Teams?.Any() != true || !round.Teams.All(t => t.HasSubmittedDeck))
                                {
                                    messageBuilder.AddComponents(new DiscordButtonComponent(
                                        DiscordButtonStyle.Primary,
                                        $"submit_deck_{round.GetHashCode()}",
                                        "Submit Deck"
                                    ));
                                }
                                break;
                            case MatchStage.GameResults:
                                // Game results components are handled separately
                                break;
                        }
                    }

                    var newMessage = await channel.SendMessageAsync(messageBuilder);
                    _channelToMessageMap[channel.Id] = newMessage.Id;
                    round.StatusMessageId = newMessage.Id;

                    // Initialize first stage if needed
                    if (round.CurrentStage == MatchStage.Created)
                    {
                        round.CurrentStage = MatchStage.MapBan;
                        await UpdateToMapBanStageAsync(channel, round, client);
                    }

                    return newMessage;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating match status message");
                throw;
            }
        }

        /// <summary>
        /// Initializes a new match status with proper stage setup
        /// </summary>
        public async Task<DiscordMessage> InitializeMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (round is null) throw new ArgumentNullException(nameof(round));
            if (client is null) throw new ArgumentNullException(nameof(client));

            // Remove any existing message mapping for this channel
            _channelToMessageMap.Remove(channel.Id);

            // Clear any existing status message ID
            round.StatusMessageId = null;

            // Create initial message with no components
            var message = await UpdateMatchStatusAsync(channel, round, client);

            // Transition to first stage (usually map bans)
            if (round.CurrentStage == MatchStage.Created)
            {
                round.CurrentStage = MatchStage.MapBan;
                await UpdateToMapBanStageAsync(channel, round, client);
            }

            return message;
        }

        /// <summary>
        /// Creates a new match status embed for a new match, preserving history
        /// </summary>
        public async Task<DiscordMessage> CreateNewMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (round is null) throw new ArgumentNullException(nameof(round));
            if (client is null) throw new ArgumentNullException(nameof(client));

            // Remove any existing message mapping for this channel
            _channelToMessageMap.Remove(channel.Id);

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
            // Validate current stage
            if (round.CurrentStage != MatchStage.Created && round.CurrentStage != MatchStage.MapBan)
            {
                throw new InvalidOperationException($"Cannot transition to map ban stage from {round.CurrentStage}");
            }

            // Ensure we have valid teams
            if (round.Teams?.Count < 2)
            {
                throw new InvalidOperationException("Cannot start map ban stage without at least two teams");
            }

            // Ensure we have a valid map pool
            var maps = _mapService.GetTournamentMapPool(round.OneVOne);
            if (!maps.Any())
            {
                throw new InvalidOperationException("No maps available for banning");
            }

            round.CurrentStage = MatchStage.MapBan;
            var message = await UpdateMatchStatusAsync(channel, round, client);

            // Also update in all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }

            return message;
        }

        /// <summary>
        /// Updates the status message for deck submission stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToDeckSubmissionStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Update the round's current stage
            round.CurrentStage = MatchStage.DeckSubmission;

            // Update the status message
            var message = await UpdateMatchStatusAsync(channel, round, client);

            // Also update in all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }

            return message;
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
        /// Records a map ban selection in the match status
        /// </summary>
        public async Task RecordMapBanAsync(DiscordChannel channel, Round round, string teamName, List<string> bannedMaps, DiscordClient client)
        {
            if (round.CurrentStage != MatchStage.MapBan)
            {
                throw new InvalidOperationException($"Cannot record map ban in stage {round.CurrentStage}");
            }

            // Find the team by name
            var team = round.Teams.FirstOrDefault(t => string.Equals(t.Name, teamName, StringComparison.OrdinalIgnoreCase));
            if (team == null)
            {
                throw new ArgumentException($"Team '{teamName}' not found in round", nameof(teamName));
            }

            // Update the team's unconfirmed map bans (not confirmed yet)
            team.UnconfirmedMapBans = bannedMaps;

            // Get the message in this channel
            var existingMessage = await GetMatchStatusMessageAsync(channel, client);
            if (existingMessage == null)
            {
                throw new InvalidOperationException("Cannot find match status message to update");
            }

            // Update the status message
            await UpdateMatchStatusAsync(channel, round, client);

            // Also update all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }

            // Add confirm/revise buttons
            var builder = new DiscordMessageBuilder()
                .AddEmbed(CreateMatchStatusEmbed(round, null))
                .AddComponents(
                    new DiscordButtonComponent(
                        DiscordButtonStyle.Success,
                        $"confirm_map_bans_{round.GetHashCode()}",
                        "Confirm Map Bans"
                    ),
                    new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"revise_map_bans_{round.GetHashCode()}",
                        "Revise Map Bans"
                    )
                );

            await existingMessage.ModifyAsync(builder);
        }

        /// <summary>
        /// Confirms a team's map bans
        /// </summary>
        public async Task ConfirmMapBansAsync(DiscordChannel channel, Round round, string teamName, DiscordClient client)
        {
            if (round.CurrentStage != MatchStage.MapBan)
            {
                throw new InvalidOperationException($"Cannot confirm map bans in stage {round.CurrentStage}");
            }

            // Find the team by name
            var team = round.Teams.FirstOrDefault(t => string.Equals(t.Name, teamName, StringComparison.OrdinalIgnoreCase));
            if (team == null)
            {
                throw new ArgumentException($"Team '{teamName}' not found in round", nameof(teamName));
            }

            if (team.UnconfirmedMapBans == null || !team.UnconfirmedMapBans.Any())
            {
                throw new InvalidOperationException("No map bans to confirm");
            }

            // Transfer unconfirmed bans to confirmed bans
            team.MapBans = team.UnconfirmedMapBans.ToList();
            team.UnconfirmedMapBans.Clear();

            // Check if all teams have submitted map bans
            bool allTeamsSubmitted = round.Teams.All(t => t.MapBans?.Any() ?? false);

            // If all teams have submitted, move to deck submission stage
            if (allTeamsSubmitted)
            {
                round.CurrentStage = MatchStage.DeckSubmission;
            }

            // Update the status message
            await UpdateMatchStatusAsync(channel, round, client);

            // Also update all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }
        }

        /// <summary>
        /// Revises a team's map ban selection by returning to the selection dropdown
        /// </summary>
        public async Task ReviseMapBansAsync(DiscordChannel channel, Round round, string teamName, DiscordClient client)
        {
            if (round.CurrentStage != MatchStage.MapBan)
            {
                throw new InvalidOperationException($"Cannot revise map bans in stage {round.CurrentStage}");
            }

            // Find the team by name
            var team = round.Teams.FirstOrDefault(t => string.Equals(t.Name, teamName, StringComparison.OrdinalIgnoreCase));
            if (team == null)
            {
                throw new ArgumentException($"Team '{teamName}' not found in round", nameof(teamName));
            }

            // Clear unconfirmed map bans to allow reselection
            team.UnconfirmedMapBans.Clear();

            // Get the match status message
            var existingMessage = await GetMatchStatusMessageAsync(channel, client);
            if (existingMessage == null)
            {
                throw new InvalidOperationException("Cannot find match status message to update");
            }

            // Get map pool
            var mapPool = _mapService.GetTournamentMapPool(round.OneVOne);
            if (mapPool == null || !mapPool.Any())
            {
                throw new InvalidOperationException("No map pool available");
            }

            // Get available maps (all maps that haven't been played)
            var availableMaps = mapPool.Where(m => !round.Maps.Contains(m));
            if (!availableMaps.Any())
            {
                throw new InvalidOperationException("No available maps to ban");
            }

            // Bo1 matches (including group stage) and Bo3 matches have 3 bans
            // Bo5 matches have 2 bans
            int numBans = round.Length == 5 ? 2 : 3;

            // Update the status message with the dropdown again
            var builder = new DiscordMessageBuilder()
                .AddEmbed(CreateMatchStatusEmbed(round, mapPool))
                .AddComponents(new DiscordSelectComponent(
                    $"map_ban_{round.GetHashCode()}",
                    $"Select {numBans} maps to ban (in order of priority)",
                    availableMaps.Select(m => new DiscordSelectComponentOption(m, m)),
                    false,
                    minOptions: numBans,
                    maxOptions: numBans
                ));

            await existingMessage.ModifyAsync(builder);
        }

        /// <summary>
        /// Updates the map pool field to show current selections
        /// </summary>
        private void UpdateMapPoolWithSelections(DiscordEmbedBuilder builder, Round round, List<string> selectedMaps)
        {
            if (selectedMaps == null || !selectedMaps.Any() || builder == null) return;

            // Remove existing map pool field if present
            var existingFields = builder.Fields?.ToList();
            if (existingFields != null)
            {
                for (int i = 0; i < existingFields.Count; i++)
                {
                    if (existingFields[i]?.Name?.Contains("Map Pool") == true)
                    {
                        builder.RemoveFieldAt(i);
                        break;
                    }
                }
            }

            // Add updated map pool
            AddMapPoolFieldWithDivider(builder, round, selectedMaps);
        }

        /// <summary>
        /// Records a deck submission and updates the match status
        /// </summary>
        public async Task RecordDeckSubmissionAsync(DiscordChannel channel, Round round, ulong playerId, string deckCode, int gameNumber, DiscordClient client)
        {
            if (round is null) throw new ArgumentNullException(nameof(round));

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

            // Also update in all team threads if this is not a team thread
            if (round?.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
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
        /// Creates the basic match status embed with common information
        /// </summary>
        private DiscordEmbedBuilder CreateMatchStatusEmbed(Round round, List<string>? cachedMapPool = null)
        {
            // Default titles for regular matches
            string title = "Match Status";
            string subtitle = $"Current Status: {round.CurrentStage}";

            // Try to get better title from round's custom properties
            if (round.CustomProperties?.TryGetValue("TournamentId", out var tournamentIdObj) == true &&
                tournamentIdObj is string tournamentId)
            {
                // Extract playoff stage info if available
                if (round.CustomProperties.TryGetValue("MatchType", out var matchTypeObj) &&
                    matchTypeObj is string matchType && !string.IsNullOrEmpty(matchType))
                {
                    // Playoff match
                    title = $"Playoffs: {matchType}";
                }
                else if (round.CustomProperties.TryGetValue("GroupName", out var groupNameObj) &&
                         groupNameObj is string groupName && !string.IsNullOrEmpty(groupName))
                {
                    // Group stage match
                    int currentRound = 0;
                    int totalRounds = 0;

                    if (round.CustomProperties.TryGetValue("CurrentRound", out var currentRoundObj) &&
                        currentRoundObj is int currentRoundValue)
                    {
                        currentRound = currentRoundValue;
                    }

                    if (round.CustomProperties.TryGetValue("TotalRounds", out var totalRoundsObj) &&
                        totalRoundsObj is int totalRoundsValue)
                    {
                        totalRounds = totalRoundsValue;
                    }

                    title = $"Group Stage ({groupName}): Round {currentRound} of {totalRounds}";
                }
            }

            // Set subtitle to match and game info if available
            if (round.Teams?.Count >= 2)
            {
                string player1Name = round.Teams[0]?.Name ?? "Player 1";
                string player2Name = round.Teams[1]?.Name ?? "Player 2";
                int currentGame = 0;
                int totalGames = 0;

                if (round.CustomProperties?.TryGetValue("CurrentGame", out var currentGameObj) == true &&
                    currentGameObj is int currentGameValue)
                {
                    currentGame = currentGameValue;
                }

                if (round.CustomProperties?.TryGetValue("TotalGames", out var totalGamesObj) == true &&
                    totalGamesObj is int totalGamesValue)
                {
                    totalGames = totalGamesValue;
                }

                subtitle = $"Match: {player1Name} vs {player2Name}, Game {currentGame} of {totalGames}";
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

            // Get match progress bar
            string progressBar = GetMatchProgressBar(round.CurrentStage);

            // Create embed with progress bar included in the description
            var builder = new DiscordEmbedBuilder()
                .WithTitle(title)
                .WithDescription(subtitle + "\n\n" + progressBar + "\n_______________________________________________")
                .WithColor(embedColor)
                .WithTimestamp(DateTimeOffset.Now);

            // Add map pool with color coding
            if (round.CurrentStage == MatchStage.MapBan)
            {
                var mapPool = cachedMapPool ?? _mapService.GetTournamentMapPool(round.OneVOne);
                AddMapPoolFieldWithDivider(builder, round, mapPool);
            }

            // Add team map bans with divider
            AddTeamMapBansFieldWithDivider(builder, round);

            // Add deck submissions area if applicable
            if (round.CurrentStage >= MatchStage.DeckSubmission)
            {
                AddDeckSubmissionsAreaWithDivider(builder, round);
            }

            // Add game results area with divider
            AddGameResultsAreaWithDivider(builder, round);

            // Add stage-specific instructions at the bottom (no divider needed after this)
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

        private void AddMapPoolFieldWithDivider(DiscordEmbedBuilder builder, Round round, List<string> mapPool)
        {
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

            // Add divider at the end
            mapPoolBuilder.AppendLine("\n_______________________________________________");

            builder.AddField("🗺️ Map Pool", mapPoolBuilder.ToString().Trim(), false);
        }

        private string GetMapStatusEmoji(Round round, string map)
        {
            // Check if map has been played
            if (round.Maps?.Contains(map) == true)
                return "🟦"; // Blue for played maps

            // Check if map is banned by either team (only consider confirmed bans)
            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                if (team.MapBans?.Contains(map) == true)
                {
                    // Determine if this is a guaranteed or conditional ban based on match length and ban priority
                    int banPriority = team.MapBans.IndexOf(map);

                    bool isGuaranteedBan = false;

                    // Best of 1: All 3 bans are guaranteed
                    if (round.Length == 1)
                    {
                        isGuaranteedBan = true;
                    }
                    // Best of 3: Priority 1 and 2 are guaranteed, Priority 3 is conditional
                    else if (round.Length == 3)
                    {
                        isGuaranteedBan = banPriority < 2; // 0 and 1 are guaranteed
                    }
                    // Best of 5: Only Priority 1 is guaranteed, Priority 2 is conditional
                    else if (round.Length == 5)
                    {
                        isGuaranteedBan = banPriority == 0; // Only 0 is guaranteed
                    }

                    return isGuaranteedBan ? "🟥" : "🟨";
                }
            }

            return "🟩"; // Green for available maps
        }

        private void AddTeamMapBansFieldWithDivider(DiscordEmbedBuilder builder, Round round)
        {
            if (round.Teams is null) return;

            var banBuilder = new StringBuilder();

            // Process the teams (we'll show detailed bans for the first team, and just status for other teams)
            var userTeam = round.Teams.FirstOrDefault();
            var opponentTeams = round.Teams.Skip(1).ToList();

            if (userTeam is null) return;

            // User's team map bans
            if (userTeam.UnconfirmedMapBans?.Any() == true && userTeam.MapBans?.Any() != true)
            {
                // Display unconfirmed bans with proper formatting
                banBuilder.AppendLine("My Team Map Bans (unconfirmed):");
                banBuilder.AppendLine("Priority #1      Priority #2      Priority #3");

                // Create a single line for the map names
                var mapLine = new StringBuilder();
                for (int i = 0; i < userTeam.UnconfirmedMapBans.Count; i++)
                {
                    string mapName = userTeam.UnconfirmedMapBans[i];
                    // Pad each map name to align properly
                    mapLine.Append(mapName.PadRight(15));
                    if (i < userTeam.UnconfirmedMapBans.Count - 1)
                        mapLine.Append(" ");
                }
                banBuilder.AppendLine(mapLine.ToString());
            }
            else if (userTeam.MapBans?.Any() == true)
            {
                // Display confirmed bans
                banBuilder.AppendLine("My Team Map Bans:");

                // Show priority numbers clearly
                for (int i = 0; i < userTeam.MapBans.Count; i++)
                {
                    string priority = i == 0 ? "1st" : i == 1 ? "2nd" : "3rd";
                    string mapName = userTeam.MapBans[i];
                    banBuilder.AppendLine($"• {priority} Priority: {mapName} ✅");
                }
            }
            else
            {
                banBuilder.AppendLine("My Team Map Bans:");
                banBuilder.AppendLine("(Not yet submitted)");
            }

            // Add divider between user team and opponent teams
            banBuilder.AppendLine();

            // Add opponent teams (only show submission status, not the actual maps)
            foreach (var team in opponentTeams)
            {
                if (team is null) continue;

                string banStatus = team.MapBans?.Any() == true
                    ? "✅ Submitted"
                    : "⏳ Waiting for submission";

                banBuilder.AppendLine($"Opponent Map Bans: {banStatus}");
            }

            // Add divider at the end
            banBuilder.AppendLine("\n_______________________________________________");

            // Remove the label from the field since it's already in the content
            builder.AddField("\u200B", banBuilder.ToString().Trim(), false);
        }

        private void AddDeckSubmissionsAreaWithDivider(DiscordEmbedBuilder builder, Round round)
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

            // Add divider at the end
            deckBuilder.AppendLine("\n_______________________________________________");

            builder.AddField("🃏 Deck Submissions", deckBuilder.ToString().Trim(), false);
        }

        private void AddGameResultsAreaWithDivider(DiscordEmbedBuilder builder, Round round)
        {
            var resultsBuilder = new StringBuilder();

            if (round.Maps?.Any() != true)
            {
                resultsBuilder.AppendLine("No games completed yet");
                // Add divider at the end
                resultsBuilder.AppendLine("\n_______________________________________________");
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
            resultsBuilder.AppendLine("\n_______________________________________________");

            builder.AddField("🎮 Game Results", resultsBuilder.ToString().Trim(), false);
        }

        private void AddStageInstructions(DiscordEmbedBuilder builder, Round round)
        {
            string instructions = round.CurrentStage switch
            {
                MatchStage.MapBan => round.Teams?.FirstOrDefault()?.UnconfirmedMapBans?.Any() == true
                    ? "Review your map ban selections above and choose to confirm or revise them."
                    : "Select maps to ban using the dropdown below, ordered by priority.",
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

        /// <summary>
        /// Updates the match status embed in all team threads
        /// </summary>
        public async Task UpdateMatchStatusInAllThreadsAsync(Round round, DiscordClient client)
        {
            if (round is null) throw new ArgumentNullException(nameof(round));
            if (client is null) throw new ArgumentNullException(nameof(client));

            foreach (var team in round.Teams)
            {
                if (team.Thread is null)
                {
                    _logger.LogWarning($"Cannot update match status: no thread for team {team.Name}");
                    continue;
                }

                try
                {
                    await UpdateMatchStatusAsync(team.Thread, round, client);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating match status in thread for team {team.Name}");
                }
            }
        }
    }
}