using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Net;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of IMatchStatusService for managing match status display
    /// </summary>
    public partial class MatchStatusService : IMatchStatusService
    {
        private readonly ILogger<MatchStatusService> _logger;
        private readonly ITournamentMapService _mapService;
        private readonly ConcurrentDictionary<ulong, ulong> _channelToMessageMap = new();

        // Add cooldown tracking for refresh buttons
        private readonly ConcurrentDictionary<string, DateTime> _refreshButtonCooldowns = new();
        private const int RefreshCooldownSeconds = 3;

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
                    _channelToMessageMap.TryRemove(channel.Id, out _);
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
                _logger.LogInformation($"Updating match status in channel {channel.Id}");

                // Ensure the message exists
                var message = await EnsureMatchStatusMessageExistsAsync(channel, round, client);
                if (message == null)
                {
                    _logger.LogError($"Failed to ensure match status message exists in channel {channel.Id}");
                    throw new Exception("Failed to create or retrieve match status message");
                }

                // Create the embed based on the round state - pass channel ID
                var embed = BuildMatchStatusEmbed(round, null, channel.Id);

                // Create a message builder with the updated embed
                var messageBuilder = new DiscordMessageBuilder()
                    .AddEmbed(embed);

                // Check if the team has confirmed their map bans (if applicable)
                bool hasConfirmedMapBans = false;
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == channel.Id);
                if (team != null && round.CurrentStage == MatchStage.MapBan)
                {
                    hasConfirmedMapBans = team.MapBans != null && team.MapBans.Any();
                    _logger.LogInformation($"Team {team.Name} in channel {channel.Id} has confirmed map bans: {hasConfirmedMapBans}");
                }

                // Check if the team has a pending deck submission (if applicable)
                bool hasPendingDeckSubmission = false;
                if (team != null && round.CurrentStage == MatchStage.DeckSubmission)
                {
                    // Check if this team has a pending deck submission
                    hasPendingDeckSubmission = round.CustomProperties != null &&
                        round.CustomProperties.ContainsKey($"PendingDeckSubmission_{team.Name}");

                    // Also check if any participant has a temp deck code
                    hasPendingDeckSubmission = hasPendingDeckSubmission ||
                        (team.Participants?.Any(p => !string.IsNullOrEmpty(p?.TempDeckCode)) ?? false);

                    _logger.LogInformation($"Team {team.Name} in channel {channel.Id} has pending deck submission: {hasPendingDeckSubmission}");
                }

                // CASE 1: Map Ban Stage with no confirmation yet - show map ban dropdown
                if (round.CurrentStage == MatchStage.MapBan && !hasConfirmedMapBans)
                {
                    messageBuilder = AddMapBanDropdown(messageBuilder, round, channel.Id);
                }
                // CASE 2: Deck Submission with pending submission - show confirm/revise/refresh buttons
                else if (round.CurrentStage == MatchStage.DeckSubmission && hasPendingDeckSubmission)
                {
                    messageBuilder = AddDeckConfirmationButtons(messageBuilder, round, team);
                }
                // CASE 3: Game Results stage - add winner selection dropdown
                else if (round.CurrentStage == MatchStage.GameResults)
                {
                    messageBuilder = AddGameWinnerDropdown(messageBuilder, round);
                }
                // DEFAULT: Just show refresh button
                else
                {
                    // Add the refresh button for all other stages or when map bans are confirmed
                    var refreshButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"refresh_status_{round.Id}",
                        "Refresh Status",
                        emoji: new DiscordComponentEmoji("🔄"));

                    messageBuilder.AddComponents(refreshButton);

                    LogRefreshButtonStatus(round, hasConfirmedMapBans, hasPendingDeckSubmission, channel.Id);
                }

                // Update the message
                await message.ModifyAsync(messageBuilder);

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating match status: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Log refresh button status for debugging
        /// </summary>
        private void LogRefreshButtonStatus(Round round, bool hasConfirmedMapBans, bool hasPendingDeckSubmission, ulong channelId)
        {
            if (round.CurrentStage == MatchStage.MapBan && hasConfirmedMapBans)
            {
                _logger.LogInformation($"Showing refresh button for channel {channelId} - team has already confirmed map bans");
            }
            else if (round.CurrentStage == MatchStage.DeckSubmission && !hasPendingDeckSubmission)
            {
                _logger.LogInformation($"Showing refresh button for channel {channelId} - no pending deck submission");
            }
            else
            {
                _logger.LogInformation($"Showing refresh button for channel {channelId} - stage: {round.CurrentStage}");
            }
        }

        /// <summary>
        /// Creates a new match status embed for a new match, preserving history
        /// </summary>
        public async Task<DiscordMessage> CreateNewMatchStatusAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (round is null) throw new ArgumentNullException(nameof(round));
            if (client is null) throw new ArgumentNullException(nameof(client));

            // This method should ONLY be used when starting a new match
            // Build the base embed
            var embed = BuildMatchStatusEmbed(round);

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
            _logger.LogInformation($"Created new match status message in channel {channel.Id} with ID {newMessage.Id}");

            // Update mappings with the new message - use thread-safe method
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
                _logger.LogWarning($"Match status message not found in channel {channel.Id}, creating a new one");

                // Clear any existing status message ID since we're creating a new one
                round.StatusMessageId = null;

                // Create a fresh message
                var embed = BuildMatchStatusEmbed(round);
                var messageBuilder = new DiscordMessageBuilder().AddEmbed(embed);
                message = await channel.SendMessageAsync(messageBuilder);

                // Update the mapping
                _channelToMessageMap[channel.Id] = message.Id;
                round.StatusMessageId = message.Id;

                _logger.LogInformation($"Created basic match status message in channel {channel.Id} with ID {message.Id}");
            }

            return message;
        }

        /// <summary>
        /// Updates the map information in the match status
        /// </summary>
        public async Task UpdateMapInformationAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            try
            {
                // Get a random map for the next game
                string? nextMap = _mapService.GetRandomMapForNextGame(round);

                if (nextMap == null)
                {
                    _logger.LogWarning("Failed to get random map for next game");
                    return;
                }

                // Update the round's current map
                if (round.CustomProperties is null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }
                round.CustomProperties["CurrentMap"] = nextMap;

                // Add a custom instruction about the map
                round.CustomProperties["Instructions"] = $"Next map is **{nextMap}**. Please prepare your decks accordingly.";

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

                // Send map thumbnail as the only separate message (auto-deleted after 5 min)
                await _mapService.SendMapThumbnailAsync(channel, nextMap, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating map information: {ex.Message}");
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

        /// <summary>
        /// Checks if a refresh button is on cooldown for a specific user
        /// </summary>
        public bool IsRefreshButtonOnCooldown(string roundId, ulong userId)
        {
            string key = $"refresh:{roundId}:{userId}";

            if (_refreshButtonCooldowns.TryGetValue(key, out DateTime lastUse) &&
                (DateTime.UtcNow - lastUse).TotalSeconds < RefreshCooldownSeconds)
            {
                return true;
            }

            _refreshButtonCooldowns[key] = DateTime.UtcNow;

            // Clean up old entries when adding new ones (simple cleanup strategy)
            if (_refreshButtonCooldowns.Count > 1000)
            {
                foreach (var entry in _refreshButtonCooldowns.ToList())
                {
                    if ((DateTime.UtcNow - entry.Value).TotalMinutes > 10)
                    {
                        _refreshButtonCooldowns.TryRemove(entry.Key, out _);
                    }
                }
            }

            return false;
        }

        // Helper methods for message creation
        private DiscordMessageBuilder AddDeckConfirmationButtons(DiscordMessageBuilder messageBuilder, Round round, Round.Team? team)
        {
            // Find the participant with the pending deck
            var participant = team?.Participants?.FirstOrDefault(p => !string.IsNullOrEmpty(p?.TempDeckCode));
            if (participant?.Player is not null)
            {
                var refreshButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"refresh_status_{round.Id}",
                    "Refresh Status",
                    emoji: new DiscordComponentEmoji("🔄"));

                var confirmButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Success,
                    $"confirm_deck_{participant.Player.Id}",
                    "Confirm Deck");

                var reviseButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"revise_deck_{participant.Player.Id}",
                    "Revise Deck");

                // Add all three buttons
                messageBuilder.AddComponents(refreshButton, confirmButton, reviseButton);

                _logger.LogInformation($"Showing confirm/revise buttons - team has pending deck submission");
            }
            else
            {
                // Fallback to just refresh button if we can't find the participant
                var refreshButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"refresh_status_{round.Id}",
                    "Refresh Status",
                    emoji: new DiscordComponentEmoji("🔄"));

                messageBuilder.AddComponents(refreshButton);
            }

            return messageBuilder;
        }

        private DiscordMessageBuilder AddGameWinnerDropdown(DiscordMessageBuilder messageBuilder, Round round)
        {
            // Create game winner dropdown options
            var options = new List<DiscordSelectComponentOption>();

            // Get all participants from both teams
            if (round.Teams != null)
            {
                foreach (var team in round.Teams)
                {
                    if (team?.Participants is not null)
                    {
                        foreach (var participant in team.Participants)
                        {
                            if (participant?.Player is not null)
                            {
                                options.Add(new DiscordSelectComponentOption(
                                    participant.Player.Username ?? "Unknown User",
                                    $"game_winner:{participant.Player.Id}"));
                            }
                        }
                    }
                }
            }

            // Add draw option
            options.Add(new DiscordSelectComponentOption("Draw", "game_winner:draw"));

            // Create dropdown component
            var gameWinnerDropdown = new DiscordSelectComponent(
                "tournament_game_winner_dropdown",
                "Select Game Winner",
                options);

            // Add dropdown and refresh button
            messageBuilder.AddComponents(gameWinnerDropdown);

            // Also add a refresh button below the dropdown
            var refreshButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"refresh_status_{round.Id}",
                "Refresh Status",
                emoji: new DiscordComponentEmoji("🔄"));

            messageBuilder.AddComponents(refreshButton);

            return messageBuilder;
        }

        /// <summary>
        /// Creates the basic match status embed with common information
        /// </summary>
        private DiscordEmbedBuilder BuildMatchStatusEmbed(Round round, List<string>? cachedMapPool = null, ulong? channelId = null)
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

                // Use 1-based indexing for display purposes (add 1 to both values)
                int displayCurrentGame = currentGame + 1;
                int displayTotalGames = totalGames + 1;

                subtitle = $"Match: {player1Name} vs {player2Name}, Game {displayCurrentGame} of {displayTotalGames}";
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
            string progressBar = GetMatchProgressBar(round.CurrentStage, round);

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

            // Add team map bans with divider, passing the channel ID
            AddTeamMapBansFieldWithDivider(builder, round, channelId);

            // Add deck submissions area if applicable, also passing channel ID
            if (round.CurrentStage >= MatchStage.DeckSubmission)
            {
                AddDeckSubmissionsAreaWithDivider(builder, round, channelId);
            }

            // Add game results area with divider
            AddGameResultsAreaWithDivider(builder, round);

            // Add stage-specific instructions at the bottom (no divider needed after this)
            AddStageInstructions(builder, round, channelId);

            return builder;
        }

        private string GetMatchProgressBar(MatchStage currentStage, Round round)
        {
            // Check if all teams have completed map bans
            bool mapBansCompleted = false;
            if (round?.Teams != null)
            {
                // Map bans are completed when all teams have confirmed their map bans
                mapBansCompleted = round.Teams.All(t => t?.MapBans?.Any() == true);

                // Additionally, if there was a coinflip required, it should be completed
                if (mapBansCompleted &&
                    ((round.Length == 3 || round.Length == 5) &&
                     round.CoinflipPerformed))
                {
                    mapBansCompleted = true;
                }
            }

            // Check if all teams have completed deck submissions for the current game
            bool deckSubmissionsCompleted = false;
            if (round?.Teams != null)
            {
                deckSubmissionsCompleted = round.Teams.All(t =>
                    t?.Participants?.Where(p => p?.Player is not null)
                     .All(p => !string.IsNullOrEmpty(p?.Deck)) ?? false);
            }

            // Create a horizontal progress bar with arrows
            string mapBanEmoji;
            if (currentStage > MatchStage.MapBan || mapBansCompleted)
            {
                mapBanEmoji = "✅"; // Completed
            }
            else if (currentStage == MatchStage.MapBan && !mapBansCompleted)
            {
                mapBanEmoji = "▶️"; // In progress
            }
            else
            {
                mapBanEmoji = "⬜"; // Not started
            }

            string deckSubmitEmoji;
            if (currentStage > MatchStage.DeckSubmission ||
                (currentStage == MatchStage.DeckSubmission && deckSubmissionsCompleted))
            {
                deckSubmitEmoji = "✅"; // Completed
            }
            else if (currentStage == MatchStage.DeckSubmission && !deckSubmissionsCompleted)
            {
                deckSubmitEmoji = "▶️"; // In progress
            }
            else
            {
                deckSubmitEmoji = "⬜"; // Not started
            }

            string gameResultsEmoji = currentStage == MatchStage.GameResults ? "▶️" : currentStage > MatchStage.GameResults ? "✅" : "⬜";

            return $"{mapBanEmoji} Map Bans ➜ {deckSubmitEmoji} Deck Submission ➜ {gameResultsEmoji} Game Results";
        }

        /// <summary>
        /// Adds stage-specific instructions to the embed
        /// </summary>
        private void AddStageInstructions(DiscordEmbedBuilder builder, Round round, ulong? channelId = null)
        {
            string instructions = "";

            // Determine which instructions to show based on stage
            switch (round.CurrentStage)
            {
                case MatchStage.MapBan:
                    instructions = round.Teams?.FirstOrDefault()?.UnconfirmedMapBans?.Any() == true
                        ? "Review your map ban selections above and choose to confirm or revise them."
                        : "Select maps to ban using the dropdown below, ordered by priority.";
                    break;

                case MatchStage.DeckSubmission:
                    // Only show deck submission instructions if ALL players on the team in this channel have confirmed their decks
                    if (channelId.HasValue)
                    {
                        var currentTeam = round.Teams?.FirstOrDefault(t => t?.Thread?.Id == channelId.Value);
                        if (currentTeam != null && currentTeam.Participants != null && currentTeam.Participants.Any())
                        {
                            // Check if ANY team member is missing a confirmed deck
                            bool anyTeamMemberMissingDeck = false;

                            foreach (var teamMember in currentTeam.Participants)
                            {
                                if (teamMember != null && string.IsNullOrEmpty(teamMember.Deck))
                                {
                                    anyTeamMemberMissingDeck = true;
                                    break;
                                }
                            }

                            if (anyTeamMemberMissingDeck)
                            {
                                instructions = "Submit your deck using `/tournament submit_deck`.";
                            }
                            else
                            {
                                instructions = "Waiting for the match to begin...";
                            }
                        }
                        else
                        {
                            // Default if team not found or no participants
                            instructions = "Submit your deck using `/tournament submit_deck`.";
                        }
                    }
                    else
                    {
                        // Default if no channel ID provided
                        instructions = "Submit your deck using `/tournament submit_deck`.";
                    }
                    break;

                case MatchStage.DeckRevision:
                    instructions = "Please submit your revised deck using `/tournament submit_deck`.";
                    break;

                case MatchStage.GameResults:
                    instructions = "Select the winner from the dropdown below.";
                    break;

                case MatchStage.Completed:
                    instructions = $"Match completed! {round.WinMsg}";
                    break;
            }

            if (!string.IsNullOrEmpty(instructions))
            {
                builder.AddField("📝 Instructions", instructions, false);
            }

            // Check if there are custom instructions in the CustomProperties
            if (round.CustomProperties?.TryGetValue("Instructions", out var customInstructionsObj) == true &&
                customInstructionsObj is string customInstructions && !string.IsNullOrEmpty(customInstructions))
            {
                // Add coinflip information to the instructions if a coinflip was performed
                if (round.CoinflipPerformed &&
                    !string.IsNullOrEmpty(round.CoinflipWinnerTeamName) &&
                    !string.IsNullOrEmpty(round.CoinflipHeadsTeamName) &&
                    !string.IsNullOrEmpty(round.CoinflipTailsTeamName))
                {
                    string randomSelectionText = $"\n\n**Random Selection Result for Conditional Map Ban:**\n" +
                        $"Teams: **{round.CoinflipHeadsTeamName}** vs **{round.CoinflipTailsTeamName}**\n" +
                        $"**Winner: {round.CoinflipWinnerTeamName}**\n" +
                        $"Their {(round.Length == 3 ? "3rd" : "2nd")} priority ban was applied to the map pool.";

                    customInstructions += randomSelectionText;
                }

                builder.AddField("__Custom Instructions__", customInstructions);
            }
        }
    }
}
