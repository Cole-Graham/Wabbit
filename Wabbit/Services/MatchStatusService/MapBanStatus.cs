using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.Models;

namespace Wabbit.Services
{
    public partial class MatchStatusService
    {
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

            // Also update in all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }

            // Add confirm/revise buttons - pass channel ID to show proper priorities
            var builder = new DiscordMessageBuilder()
                .AddEmbed(CreateMatchStatusEmbed(round, null, channel.Id))
                .AddComponents(
                    new DiscordButtonComponent(
                        DiscordButtonStyle.Success,
                        $"confirm_map_bans_{teamName}",
                        "Confirm Map Bans"
                    ),
                    new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"revise_map_bans_{teamName}",
                        "Revise Map Bans"
                    )
                );

            await existingMessage.ModifyAsync(builder);
        }

        /// <summary>
        /// Confirms map bans for a team and updates the status
        /// </summary>
        public async Task<bool> ConfirmMapBansAsync(
            DiscordChannel channel,
            Round round,
            DiscordClient client)
        {
            try
            {
                _logger.LogInformation($"Confirming map bans in channel {channel.Id}");

                // Find the team for this channel
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == channel.Id);
                if (team is null)
                {
                    _logger.LogWarning($"Could not find team for channel {channel.Id}");
                    return false;
                }

                // Check if there are unconfirmed bans to confirm
                if (team.UnconfirmedMapBans is null || !team.UnconfirmedMapBans.Any())
                {
                    _logger.LogWarning($"No unconfirmed map bans to confirm for team {team.Name}");
                    return false;
                }

                // Remember the current stage before changes
                var previousStage = round.CurrentStage;

                // Confirm the bans by moving them to MapBans
                if (team.MapBans is null)
                {
                    team.MapBans = new List<string>();
                }

                team.MapBans.Clear();
                team.MapBans.AddRange(team.UnconfirmedMapBans);

                // Check if all teams have submitted map bans
                bool allTeamsSubmitted = round.Teams?.All(t => t?.MapBans?.Any() ?? false) ?? false;

                // If all teams have submitted, move to deck submission stage
                if (allTeamsSubmitted && round.CurrentStage == MatchStage.MapBan)
                {
                    // Perform coinflip for conditional bans if needed
                    if (round is not null && round.Teams is not null)
                    {
                        if (round.Teams.Count == 2 && (round.Length == 3 || round.Length == 5) && !round.CoinflipPerformed)
                        {
                            await PerformConditionalBanCoinflipAsync(channel, round, client);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Round or teams are null");
                        return false;
                    }

                    round.CurrentStage = MatchStage.DeckSubmission;
                }

                // Get the existing message to update
                var message = await GetMatchStatusMessageAsync(channel, client);
                if (message is null)
                {
                    _logger.LogWarning("Could not find message to update after confirming map bans");
                    return false;
                }

                // Create a fresh embed without any buttons
                var embed = CreateMatchStatusEmbed(round);

                // Update match status in the current channel without confirm/revise buttons
                var messageBuilder = new DiscordMessageBuilder()
                    .AddEmbed(embed);

                // Add refresh button after team has confirmed their map bans
                // We always want to show the refresh button for a team that has confirmed their bans
                var refreshButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"refresh_status_{round.Id}",
                    "Refresh Status",
                    emoji: new DiscordComponentEmoji("🔄"));

                messageBuilder.AddComponents(refreshButton);

                // Update the message with the new embed and refresh button
                await message.ModifyAsync(messageBuilder);

                // Only update other team threads automatically if we've transitioned to a new stage
                bool stageChanged = previousStage != round.CurrentStage;
                if (stageChanged && (round.Teams?.Any(t => t is not null && t.Thread?.Id != channel.Id) ?? false))
                {
                    _logger.LogInformation($"Match advanced to {round.CurrentStage} stage. Automatically updating all team threads.");
                    await UpdateMatchStatusInAllThreadsAsync(round, client);
                }
                // Otherwise, teams will need to manually refresh to see updates

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming map bans: {ex.Message}");
                return false;
            }
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

            // Remember the current stage before changes
            var previousStage = round.CurrentStage;

            // Transfer unconfirmed bans to confirmed bans
            if (team.MapBans is null)
                team.MapBans = new List<string>();

            team.MapBans.Clear();
            team.MapBans.AddRange(team.UnconfirmedMapBans);

            // Check if all teams have submitted map bans
            bool allTeamsSubmitted = round.Teams.All(t => t.MapBans?.Any() ?? false);

            // If all teams have submitted, move to deck submission stage
            if (allTeamsSubmitted)
            {
                // Perform coinflip for conditional bans if needed
                if (round.Teams.Count == 2 && (round.Length == 3 || round.Length == 5) && !round.CoinflipPerformed)
                {
                    await PerformConditionalBanCoinflipAsync(channel, round, client);
                }

                round.CurrentStage = MatchStage.DeckSubmission;
            }

            // Get the existing message
            var message = await GetMatchStatusMessageAsync(channel, client);
            if (message is null)
            {
                _logger.LogWarning("Could not find message to update after confirming map bans");
                throw new InvalidOperationException("Could not find message to update");
            }

            // Create a fresh embed without any buttons
            var embed = CreateMatchStatusEmbed(round);

            // Update match status in the current channel without confirm/revise buttons
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embed);

            // Add refresh button after team has confirmed their map bans
            // We always want to show the refresh button for a team that has confirmed their bans
            var refreshButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"refresh_status_{round.Id}",
                "Refresh Status",
                emoji: new DiscordComponentEmoji("🔄"));

            messageBuilder.AddComponents(refreshButton);

            // Update the message with the new embed and refresh button
            await message.ModifyAsync(messageBuilder);

            // Only update other team threads automatically if we've transitioned to a new stage
            bool stageChanged = previousStage != round.CurrentStage;
            if (stageChanged && (round.Teams?.Any(t => t is not null && t.Thread?.Id != channel.Id) ?? false))
            {
                _logger.LogInformation($"Match advanced to {round.CurrentStage} stage. Automatically updating all team threads.");
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }
            // Otherwise, teams will need to manually refresh to see updates
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
        /// Helper method to add the map ban dropdown to the message builder
        /// </summary>
        private DiscordMessageBuilder AddMapBanDropdown(DiscordMessageBuilder messageBuilder, Round round, ulong channelId)
        {
            _logger.LogInformation($"Showing map ban dropdown for channel {channelId} - team has not confirmed bans yet");

            // Get the map pool for this round
            var mapPool = _mapService.GetTournamentMapPool(round.OneVOne);
            if (mapPool == null || !mapPool.Any())
            {
                _logger.LogWarning($"No map pool available for round {round.Id}");
                return messageBuilder;
            }

            // Get available maps (excluding already banned ones)
            var availableMaps = mapPool.ToList();
            if (round.Teams != null)
            {
                // Remove maps that have already been banned by all teams
                var allBannedMaps = round.Teams
                    .Where(t => t.MapBans != null && t.MapBans.Any())
                    .SelectMany(t => t.MapBans)
                    .Distinct()
                    .ToList();

                availableMaps = availableMaps
                    .Except(allBannedMaps)
                    .ToList();
            }

            // Bo1 matches (including group stage) and Bo3 matches have 3 bans
            // Bo5 matches have 2 bans
            int numBans = round.Length == 5 ? 2 : 3;

            // Add the map ban dropdown
            var selectComponent = new DiscordSelectComponent(
                $"map_ban_{round.GetHashCode()}",
                $"Select {numBans} maps to ban (in order of priority)",
                availableMaps.Select(m => new DiscordSelectComponentOption(m, m)),
                false,
                minOptions: numBans,
                maxOptions: numBans);
            messageBuilder.AddComponents(selectComponent);

            return messageBuilder;
        }

        /// <summary>
        /// Adds map pool field to the embed with a divider
        /// </summary>
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
            mapPoolBuilder.AppendLine("_______________________________________________");

            builder.AddField("🗺️ Map Pool", mapPoolBuilder.ToString().Trim(), false);
        }

        /// <summary>
        /// Gets the emoji for map status (banned, played, available)
        /// </summary>
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
                        // Priority 0 and 1 (1st and 2nd) are guaranteed
                        if (banPriority < 2)
                        {
                            isGuaranteedBan = true;
                        }
                        // Priority 2 (3rd) is conditional and depends on coinflip
                        else if (banPriority == 2 && round.CoinflipPerformed)
                        {
                            // If this team won the coinflip, their priority 3 ban is applied
                            isGuaranteedBan = string.Equals(team.Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    // Best of 5: Only Priority 1 is guaranteed, Priority 2 is conditional
                    else if (round.Length == 5)
                    {
                        // Priority 0 (1st) is guaranteed
                        if (banPriority == 0)
                        {
                            isGuaranteedBan = true;
                        }
                        // Priority 1 (2nd) is conditional and depends on coinflip
                        else if (banPriority == 1 && round.CoinflipPerformed)
                        {
                            // If this team won the coinflip, their priority 2 ban is applied
                            isGuaranteedBan = string.Equals(team.Name, round.CoinflipWinnerTeamName, StringComparison.OrdinalIgnoreCase);
                        }
                    }

                    return isGuaranteedBan ? "🟥" : "🟨";
                }
            }

            return "🟩"; // Green for available maps
        }

        /// <summary>
        /// Add team map bans to the embed with a divider
        /// </summary>
        private void AddTeamMapBansFieldWithDivider(DiscordEmbedBuilder builder, Round round, ulong? channelId = null)
        {
            if (round.Teams == null || !round.Teams.Any())
                return;

            var banBuilder = new StringBuilder();

            // Determine which team's thread we're in based on the channel ID
            // This ensures we only show each team their own map bans
            var userTeam = channelId.HasValue
                ? round.Teams.FirstOrDefault(t => t?.Thread?.Id == channelId.Value)
                : null;

            // If we can't determine the team by channel ID (e.g., in admin view),
            // default to the standard behavior but without showing specific bans
            if (userTeam == null)
            {
                userTeam = round.Teams.FirstOrDefault();
                if (userTeam == null) return;
            }

            // Get opponent team
            var opponentTeam = round.Teams.FirstOrDefault(t => t != userTeam);

            // First handle the user's team bans
            if (userTeam.UnconfirmedMapBans?.Any() == true && (userTeam.MapBans == null || !userTeam.MapBans.Any()))
            {
                // Display unconfirmed bans with proper formatting
                banBuilder.AppendLine("My Team Map Bans (unconfirmed):");
                banBuilder.AppendLine("```");
                banBuilder.AppendLine("Priority #1          Priority #2          Priority #3");

                // Create a single line for the map names with fixed-width spacing
                var mapLine = new StringBuilder();
                for (int i = 0; i < userTeam.UnconfirmedMapBans.Count; i++)
                {
                    string mapName = userTeam.UnconfirmedMapBans[i];
                    // Pad each map name to align properly with consistent width
                    mapLine.Append(mapName.PadRight(20));
                }
                banBuilder.AppendLine(mapLine.ToString());
                banBuilder.AppendLine("```");

                // Add guarantee information based on match length
                banBuilder.AppendLine();
                if (round.Length == 1)
                {
                    banBuilder.AppendLine("All bans are guaranteed in Bo1 matches");
                }
                else if (round.Length == 3)
                {
                    banBuilder.AppendLine("First 2 bans are guaranteed in Bo3 matches");
                }
                else if (round.Length == 5)
                {
                    banBuilder.AppendLine("First ban is guaranteed in Bo5 matches");
                }
            }
            else if (userTeam.MapBans?.Any() == true)
            {
                // Display confirmed bans
                banBuilder.AppendLine("My Team Map Bans: ✅");

                // Use code blocks for better alignment of confirmed bans too
                banBuilder.AppendLine("```");

                // For confirmed bans, let's use the same format as unconfirmed for consistency
                if (userTeam.MapBans.Count > 0)
                {
                    banBuilder.AppendLine("Priority #1          Priority #2          Priority #3");

                    // Create a single line for the map names with fixed-width spacing
                    var mapLine = new StringBuilder();
                    for (int i = 0; i < userTeam.MapBans.Count; i++)
                    {
                        string mapName = userTeam.MapBans[i];
                        // Pad each map name to align properly with consistent width
                        mapLine.Append(mapName.PadRight(20));
                    }
                    banBuilder.AppendLine(mapLine.ToString());
                }

                banBuilder.AppendLine("```");
            }
            else
            {
                banBuilder.AppendLine("My Team Map Bans: Not submitted yet");
            }

            // Now handle opponent team's bans if they exist
            if (opponentTeam != null)
            {
                banBuilder.AppendLine();
                if (opponentTeam.MapBans?.Any() == true)
                {
                    banBuilder.AppendLine("Opponent Map Bans: ✅ Submitted");
                }
                else
                {
                    banBuilder.AppendLine("Opponent Map Bans: ⏳ Waiting for submission");
                }
            }

            // Add divider at the end
            banBuilder.AppendLine("_______________________________________________");

            // Remove the label from the field since it's already in the content
            builder.AddField("\u200B", banBuilder.ToString().Trim(), false);
        }

        /// <summary>
        /// Performs random selection for overlapping conditional bans
        /// </summary>
        private async Task PerformConditionalBanCoinflipAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            if (round.Teams.Count != 2)
                return;

            var team1 = round.Teams[0];
            var team2 = round.Teams[1];

            // Check if both teams have map bans
            if (team1.MapBans?.Any() != true || team2.MapBans?.Any() != true)
                return;

            // For Bo3: Check if there are overlapping bans resulting in fewer than 4 unique banned maps
            // For Bo5: Check if there are overlapping bans resulting in fewer than 2 unique banned maps
            var uniqueMapBans = new HashSet<string>();
            foreach (var mapBan in team1.MapBans.Union(team2.MapBans))
            {
                uniqueMapBans.Add(mapBan);
            }

            bool needCoinflip = false;
            if (round.Length == 3 && uniqueMapBans.Count < 4 && team1.MapBans.Count >= 3 && team2.MapBans.Count >= 3)
            {
                needCoinflip = true;
            }
            else if (round.Length == 5 && uniqueMapBans.Count < 2 && team1.MapBans.Count >= 2 && team2.MapBans.Count >= 2)
            {
                needCoinflip = true;
            }

            if (needCoinflip)
            {
                // First send a teaser message
                await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent($"**INITIATING GAMBLING SEQUENCE...** 🤩 🎮 🎲 🎯"));

                await Task.Delay(1000); // Short dramatic pause

                var random = new Random();
                bool team1IsHeads = random.Next(2) == 0;
                bool headsWins = random.Next(2) == 0;

                // Determine winner
                var selectionWinner = headsWins ? (team1IsHeads ? team1 : team2) : (team1IsHeads ? team2 : team1);
                var selectionLoser = headsWins ? (team1IsHeads ? team2 : team1) : (team1IsHeads ? team1 : team2);

                // Record random selection results
                round.CoinflipPerformed = true;
                round.CoinflipWinnerTeamName = selectionWinner.Name;
                round.CoinflipHeadsTeamName = team1IsHeads ? team1.Name : team2.Name;
                round.CoinflipTailsTeamName = team1IsHeads ? team2.Name : team1.Name;

                // Randomly choose between animation types (0: coinflip, 1: slot machine, 2: roulette)
                int animationType = random.Next(3);
                bool team1Wins = string.Equals(team1.Name, selectionWinner.Name, StringComparison.OrdinalIgnoreCase);

                switch (animationType)
                {
                    case 0:
                        // Use coinflip animation
                        await ShowAnimatedCoinflipAsync(
                            channel,
                            round.CoinflipHeadsTeamName ?? "Heads Team",
                            round.CoinflipTailsTeamName ?? "Tails Team",
                            headsWins);
                        break;

                    case 1:
                        // Use slot machine animation
                        await ShowAnimatedSlotMachineAsync(
                            channel,
                            team1.Name ?? "Team 1",
                            team2.Name ?? "Team 2",
                            team1Wins);
                        break;

                    case 2:
                        // Use roulette animation
                        await ShowAnimatedRouletteAsync(
                            channel,
                            team1.Name ?? "Team 1",
                            team2.Name ?? "Team 2",
                            team1Wins);
                        break;
                }

                // Send a message about the random selection result
                var resultEmbed = new DiscordEmbedBuilder()
                    .WithTitle("🎲 Random Selection Result for Conditional Map Ban")
                    .WithDescription($"Due to overlapping map bans, a random selection was needed to determine which conditional ban applies.")
                    .WithColor(new DiscordColor(255, 215, 0))
                    .AddField("Winner", $"**{selectionWinner.Name}**", false);

                // Add specific map ban information
                int conditionalBanIndex = round.Length == 3 ? 2 : 1; // Priority 3 (index 2) for Bo3, Priority 2 (index 1) for Bo5
                if (selectionWinner.MapBans.Count > conditionalBanIndex)
                {
                    string conditionalMap = selectionWinner.MapBans[conditionalBanIndex];
                    resultEmbed.AddField("Applied Conditional Ban",
                        $"**{selectionWinner.Name}**'s {(conditionalBanIndex == 2 ? "3rd" : "2nd")} priority ban (**{conditionalMap}**) has been applied.", false);
                }

                if (selectionLoser.MapBans.Count > conditionalBanIndex)
                {
                    string ignoredMap = selectionLoser.MapBans[conditionalBanIndex];
                    resultEmbed.AddField("Ignored Conditional Ban",
                        $"**{selectionLoser.Name}**'s {(conditionalBanIndex == 2 ? "3rd" : "2nd")} priority ban (**{ignoredMap}**) will not be applied.", false);
                }

                await channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(resultEmbed));
            }
        }

        /// <summary>
        /// Creates and shows an animated coinflip
        /// </summary>
        private async Task<DiscordMessage> ShowAnimatedCoinflipAsync(
            DiscordChannel channel,
            string headsTeamName,
            string tailsTeamName,
            bool headsWins)
        {
            // Coin flip animation frames with more varied flipping
            string[] coinFrames = [
                "🪙",
                "↕️",
                "🪙",
                "↔️",
                "🪙",
                "↘️",
                "🪙",
                "↗️",
                "🪙",
                "↕️",
                "🪙"
            ];

            // Color animation - changes color during the flip for visual effect
            DiscordColor[] colorFrames = [
                new DiscordColor(255, 215, 0),    // Gold
                new DiscordColor(200, 170, 0),    // Dark gold
                new DiscordColor(230, 190, 0),    // Medium gold
                new DiscordColor(255, 215, 0),    // Gold
                new DiscordColor(255, 230, 100)   // Light gold
            ];

            // Create initial message with first frame
            var message = await channel.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent($"**COINFLIP IN PROGRESS** 🎲")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("🪙 Coinflip for Conditional Map Ban")
                    .WithDescription($"Flipping a coin to determine which team's conditional ban applies...\n\n{coinFrames[0]}")
                    .WithColor(colorFrames[0])
                    .AddField("Heads", $"**{headsTeamName}**", true)
                    .AddField("Tails", $"**{tailsTeamName}**", true)));

            // Update message to show animation frames
            for (int i = 1; i < coinFrames.Length; i++)
            {
                await Task.Delay(400); // Delay between frames

                await message.ModifyAsync(new DiscordMessageBuilder()
                    .WithContent($"**COINFLIP IN PROGRESS** 🎲")
                    .AddEmbed(new DiscordEmbedBuilder()
                        .WithTitle("🪙 Coinflip for Conditional Map Ban")
                        .WithDescription($"Flipping a coin to determine which team's conditional ban applies...\n\n{coinFrames[i]}")
                        .WithColor(colorFrames[i % colorFrames.Length])
                        .AddField("Heads", $"**{headsTeamName}**", true)
                        .AddField("Tails", $"**{tailsTeamName}**", true)));
            }

            // Final frame showing result with dramatic flair
            string resultEmoji = headsWins ? "⭐" : "🌙";
            string winnerName = headsWins ? headsTeamName : tailsTeamName;
            string resultText = headsWins ? "Heads" : "Tails";

            // Suspense pause
            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent($"**COINFLIP RESULT INCOMING...** 👀")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("🪙 The coin has stopped spinning...")
                    .WithDescription($"And the result is...\n\n...")
                    .WithColor(new DiscordColor(100, 100, 100))
                    .AddField("Heads", $"**{headsTeamName}**", true)
                    .AddField("Tails", $"**{tailsTeamName}**", true)));

            await Task.Delay(1000); // Dramatic pause

            // Final result with fanfare
            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent($"**COINFLIP RESULT** 🎯")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle($"🪙 Coinflip Result: {resultText}! 🎉")
                    .WithDescription($"The coin has landed on **{resultText}**! {resultEmoji}\n\n**{winnerName}** wins the coinflip! 🎊")
                    .WithColor(headsWins ? new DiscordColor(255, 223, 0) : new DiscordColor(192, 192, 192))
                    .AddField("Heads", $"**{headsTeamName}** {(headsWins ? "✅" : "")}", true)
                    .AddField("Tails", $"**{tailsTeamName}** {(!headsWins ? "✅" : "")}", true)
                    .AddField("Result", $"**{resultText}** {resultEmoji}", false)));

            return message;
        }

        /// <summary>
        /// Creates and shows an animated slot machine
        /// </summary>
        private async Task<DiscordMessage> ShowAnimatedSlotMachineAsync(
            DiscordChannel channel,
            string team1Name,
            string team2Name,
            bool team1Wins)
        {
            // Slot symbols with more variety for visual appeal
            string[] symbols = ["🍒", "🍊", "🍋", "🍇", "🔔", "💎", "7️⃣", "🎰", "⭐", "🍀", "🎲"];

            // Create initial message
            var message = await channel.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent($"**SPINNING THE SLOT MACHINE** 🎰")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("🎰 Slot Machine for Conditional Map Ban")
                    .WithDescription($"Spinning to determine which team's conditional ban applies...")
                    .WithColor(new DiscordColor(138, 43, 226))
                    .AddField(team1Name, "🎰", true)
                    .AddField(team2Name, "🎰", true)));

            var random = new Random();

            // Animation frames with increasing speed
            int[] delays = [600, 500, 400, 300, 250, 200];

            // First round of spins - slower
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(delays[0]);
                string team1Symbol = symbols[random.Next(symbols.Length)];
                string team2Symbol = symbols[random.Next(symbols.Length)];

                await message.ModifyAsync(new DiscordMessageBuilder()
                    .WithContent($"**SPINNING THE SLOT MACHINE** 🎰")
                    .AddEmbed(new DiscordEmbedBuilder()
                        .WithTitle("🎰 Slot Machine for Conditional Map Ban")
                        .WithDescription($"Spinning to determine which team's conditional ban applies...")
                        .WithColor(new DiscordColor(138, 43, 226))
                        .AddField(team1Name, team1Symbol, true)
                        .AddField(team2Name, team2Symbol, true)));
            }

            // Second round - faster
            for (int i = 0; i < delays.Length - 1; i++)
            {
                await Task.Delay(delays[i]);

                // Multiple rapid symbol changes for visual effect
                for (int j = 0; j < 2; j++)
                {
                    string team1Symbol = symbols[random.Next(symbols.Length)];
                    string team2Symbol = symbols[random.Next(symbols.Length)];

                    await message.ModifyAsync(new DiscordMessageBuilder()
                        .WithContent($"**SPINNING THE SLOT MACHINE** 🎰")
                        .AddEmbed(new DiscordEmbedBuilder()
                            .WithTitle("🎰 Slot Machine for Conditional Map Ban")
                            .WithDescription($"Spinning to determine which team's conditional ban applies...\n{(i > 2 ? "Almost there..." : "")}")
                            .WithColor(new DiscordColor(138, 43, 226))
                            .AddField(team1Name, team1Symbol, true)
                            .AddField(team2Name, team2Symbol, true)));

                    await Task.Delay(100); // Quick flicker between symbols
                }
            }

            // Prepare for final reveal
            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent($"**SLOT MACHINE SLOWING DOWN...** 🎰")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("🎰 Slot Machine for Conditional Map Ban")
                    .WithDescription($"The reels are slowing down...")
                    .WithColor(new DiscordColor(138, 43, 226))
                    .AddField(team1Name, "❓", true)
                    .AddField(team2Name, "❓", true)));

            await Task.Delay(800);

            // Final spin with results
            string winningSymbol = "🏆";
            string losingSymbol = symbols[random.Next(symbols.Length)];

            string team1Final = team1Wins ? winningSymbol : losingSymbol;
            string team2Final = team1Wins ? losingSymbol : winningSymbol;
            string winnerName = team1Wins ? team1Name : team2Name;

            // Final result with fanfare
            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent($"**SLOT MACHINE RESULT** 🎉")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle($"🎰 We Have a Winner! 🎊")
                    .WithDescription($"The slot machine has selected a winner!\n\n**{winnerName}** gets the trophy! 🏆\n\nJACKPOT! 💰💰💰")
                    .WithColor(new DiscordColor(255, 215, 0))
                    .AddField(team1Name, team1Final, true)
                    .AddField(team2Name, team2Final, true)
                    .AddField("Winner", $"**{winnerName}**'s conditional map ban will be applied", false)));

            return message;
        }

        /// <summary>
        /// Creates and shows an animated roulette wheel
        /// </summary>
        private async Task<DiscordMessage> ShowAnimatedRouletteAsync(
            DiscordChannel channel,
            string team1Name,
            string team2Name,
            bool team1Wins)
        {
            // Roulette wheel spinning frames
            string[] rouletteFrames = [
                "🔄 0️⃣",
                "🔄 🔴",
                "🔄 ⚫",
                "🔄 🔴",
                "🔄 ⚫",
                "🔄 🔴",
                "🔄 ⚫",
                "🔄 🔴",
                "🔄 ⚫"
            ];

            // Team colors
            string team1Color = "🔴";
            string team2Color = "⚫";

            // Create initial message
            var message = await channel.SendMessageAsync(new DiscordMessageBuilder()
                .WithContent($"**ROULETTE WHEEL SPINNING** 🎰")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle("🎡 Roulette for Conditional Map Ban")
                    .WithDescription($"Spinning the roulette wheel to determine which team's conditional ban applies...\n\n{rouletteFrames[0]}")
                    .WithColor(new DiscordColor(204, 0, 0))
                    .AddField($"{team1Name} - Red", team1Color, true)
                    .AddField($"{team2Name} - Black", team2Color, true)));

            // Update message to show animation frames
            for (int i = 1; i < rouletteFrames.Length; i++)
            {
                await Task.Delay(500);

                await message.ModifyAsync(new DiscordMessageBuilder()
                    .WithContent($"**ROULETTE WHEEL SPINNING** 🎰")
                    .AddEmbed(new DiscordEmbedBuilder()
                        .WithTitle("🎡 Roulette for Conditional Map Ban")
                        .WithDescription($"Spinning the roulette wheel to determine which team's conditional ban applies...\n\n{rouletteFrames[i]}")
                        .WithColor(new DiscordColor(204, 0, 0))
                        .AddField($"{team1Name} - Red", team1Color, true)
                        .AddField($"{team2Name} - Black", team2Color, true)));
            }

            // Final result
            string winningColor = team1Wins ? "🔴 RED" : "⚫ BLACK";
            string winnerName = team1Wins ? team1Name : team2Name;

            await Task.Delay(700);

            await message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent($"**ROULETTE RESULT** 🎯")
                .AddEmbed(new DiscordEmbedBuilder()
                    .WithTitle($"🎡 Roulette Result: {winningColor}!")
                    .WithDescription($"The ball has landed on **{winningColor}**!\n\n**{winnerName}** wins the spin!")
                    .WithColor(team1Wins ? new DiscordColor(204, 0, 0) : new DiscordColor(0, 0, 0))
                    .AddField($"{team1Name} - Red", team1Wins ? "🎯" : "❌", true)
                    .AddField($"{team2Name} - Black", team1Wins ? "❌" : "🎯", true)
                    .AddField("Result", $"**{winnerName}**'s conditional map ban will be applied", false)));

            return message;
        }
    }
}
