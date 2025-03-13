using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.BotClient.Config;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing tournament matches, extracted from TournamentManagementGroup
    /// </summary>
    public class TournamentMatchService : ITournamentMatchService
    {
        private readonly TournamentManager _tournamentManager;
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ILogger<TournamentMatchService> _logger;

        private const int autoDeleteSeconds = 30;

        public TournamentMatchService(
            TournamentManager tournamentManager,
            OngoingRounds ongoingRounds,
            ITournamentGameService tournamentGameService,
            ILogger<TournamentMatchService> logger)
        {
            _tournamentManager = tournamentManager;
            _ongoingRounds = ongoingRounds;
            _tournamentGameService = tournamentGameService;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

            // This implementation delegates to the TournamentGameService
            await _tournamentGameService.HandleMatchCompletion(tournament, match, client);
        }

        /// <inheritdoc/>
        public async Task StartPlayoffMatches(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Starting playoff matches for tournament {tournament.Name}");

            if (tournament.PlayoffMatches == null || !tournament.PlayoffMatches.Any())
            {
                _logger.LogWarning($"No playoff matches to start for tournament {tournament.Name}");
                return;
            }

            // Find matches that need players and have all their participants assigned
            var matchesToStart = tournament.PlayoffMatches
                .Where(m => !m.IsComplete && m.Participants.Count >= 2 &&
                            m.Participants.All(p => p.Player != null))
                .ToList();

            foreach (var match in matchesToStart)
            {
                _logger.LogInformation($"Setting up playoff match: {match.Name}");

                try
                {
                    // Get players as DiscordMembers
                    var participants = match.Participants.Select(p => p.Player).ToList();
                    if (participants.Count < 2)
                    {
                        _logger.LogWarning($"Not enough participants for match {match.Name}");
                        continue;
                    }

                    // Ensure we have DiscordMember objects
                    if (participants[0] is DiscordMember player1 && participants[1] is DiscordMember player2)
                    {
                        // Determine match length - finals can be longer
                        int matchLength = 3; // Default Bo3
                        if (match.Type == TournamentMatchType.Final)
                        {
                            matchLength = 5; // Finals are Bo5 by default
                        }

                        // Create and start the match
                        await CreateAndStart1v1Match(tournament, null, player1, player2, client, matchLength, match);
                    }
                    else
                    {
                        _logger.LogWarning($"Cannot start match - players are not DiscordMember objects");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error starting playoff match {match.Name}");
                }
            }
        }

        /// <inheritdoc/>
        public async Task CreateAndStart1v1Match(
            Tournament tournament,
            Tournament.Group? group,
            DiscordMember player1,
            DiscordMember player2,
            DiscordClient client,
            int matchLength,
            Tournament.Match? existingMatch = null)
        {
            _logger.LogInformation($"Starting CreateAndStart1v1Match for {player1.Username} vs {player2.Username} in tournament '{tournament.Name}'");
            try
            {
                // Find or create a channel to use for the match
                DiscordChannel? channel = tournament.AnnouncementChannel;
                _logger.LogInformation($"Initial channel: {(channel is not null ? $"{channel.Name} (ID: {channel.Id})" : "null")}");

                if (channel is null)
                {
                    var guild = player1.Guild;
                    _logger.LogInformation($"Looking for a channel in guild {guild.Name} (ID: {guild.Id})");

                    // Get the server config to find the bot channel ID
                    var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == guild.Id);
                    if (server != null && server.BotChannelId.HasValue)
                    {
                        try
                        {
                            _logger.LogInformation($"Attempting to use configured bot channel ID: {server.BotChannelId.Value}");
                            channel = await guild.GetChannelAsync(server.BotChannelId.Value);
                            _logger.LogInformation($"Found configured bot channel: {channel.Name}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to get bot channel with ID {server.BotChannelId.Value}: {ex.Message}");
                        }
                    }

                    // Fallback if bot channel ID is not configured or channel not found
                    if (channel is null)
                    {
                        _logger.LogInformation("Fallback: Searching for a general or chat channel");
                        var channels = await guild.GetChannelsAsync();
                        channel = channels.FirstOrDefault(c =>
                            c.Type == DiscordChannelType.Text &&
                            (c.Name.Contains("general", StringComparison.OrdinalIgnoreCase) ||
                             c.Name.Contains("chat", StringComparison.OrdinalIgnoreCase))
                        );

                        // If no suitable channel found, use the first text channel
                        if (channel is null)
                        {
                            _logger.LogInformation("No general/chat channel found, using first text channel");
                            channel = channels.FirstOrDefault(c => c.Type == DiscordChannelType.Text);
                        }
                    }

                    if (channel is not null)
                    {
                        _logger.LogInformation($"Using channel: {channel.Name} (ID: {channel.Id})");
                    }
                    else
                    {
                        _logger.LogWarning($"Could not find a suitable channel for tournament match in guild {guild.Name}");
                        return;
                    }
                }

                // Use existing match or create a new one
                Tournament.Match? match = existingMatch;
                if (match == null)
                {
                    // Create a new match
                    _logger.LogInformation("Creating new tournament match object");
                    match = new Tournament.Match
                    {
                        Name = $"{player1.DisplayName} vs {player2.DisplayName}",
                        Type = TournamentMatchType.GroupStage,
                        BestOf = matchLength,
                        Participants = new List<Tournament.MatchParticipant>
                        {
                            new Tournament.MatchParticipant { Player = player1 },
                            new Tournament.MatchParticipant { Player = player2 }
                        }
                    };

                    // Add match to the group
                    group?.Matches.Add(match);
                    _logger.LogInformation($"Added match to group {group?.Name}");
                }
                else
                {
                    _logger.LogInformation($"Using existing match object: {match.Name}");
                    // Ensure players in the match are set correctly to DiscordMember objects
                    if (match.Participants.Count == 2)
                    {
                        match.Participants[0].Player = player1;
                        match.Participants[1].Player = player2;
                    }
                }

                // Create and start a round for this match
                _logger.LogInformation("Creating Round object for the match");
                var round = new Round
                {
                    Name = match.Name,
                    Length = matchLength,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    Pings = $"{player1.Mention} {player2.Mention}",
                    MsgToDel = new List<DiscordMessage>()
                };

                // Create teams (one player per team)
                var team1 = new Round.Team
                {
                    Name = player1.DisplayName,
                    Participants = new List<Round.Participant> { new Round.Participant { Player = player1 } }
                };

                var team2 = new Round.Team
                {
                    Name = player2.DisplayName,
                    Participants = new List<Round.Participant> { new Round.Participant { Player = player2 } }
                };

                round.Teams.Add(team1);
                round.Teams.Add(team2);

                // Create a private thread for each player
                _logger.LogInformation($"Attempting to create private threads in channel {channel.Name} (ID: {channel.Id})");
                try
                {
                    // Create thread for player 1
                    var thread1 = await channel.CreateThreadAsync(
                            $"Match: {player1.DisplayName} vs {player2.DisplayName} (thread for {player1.DisplayName})",
                        DiscordAutoArchiveDuration.Day,
                        DiscordChannelType.PrivateThread,
                        $"Tournament match thread for {player1.DisplayName}"
                    );

                    _logger.LogInformation($"Successfully created thread for player 1: {thread1.Name} (ID: {thread1.Id})");
                    team1.Thread = thread1;

                    // Add player 1 to their thread
                    _logger.LogInformation($"Adding player 1 to thread: {player1.Username}");
                    await thread1.AddThreadMemberAsync(player1);

                    // Create thread for player 2
                    var thread2 = await channel.CreateThreadAsync(
                        $"Match: {player1.DisplayName} vs {player2.DisplayName} (thread for {player2.DisplayName})",
                        DiscordAutoArchiveDuration.Day,
                        DiscordChannelType.PrivateThread,
                        $"Tournament match thread for {player2.DisplayName}"
                    );

                    _logger.LogInformation($"Successfully created thread for player 2: {thread2.Name} (ID: {thread2.Id})");
                    team2.Thread = thread2;

                    // Add player 2 to their thread
                    _logger.LogInformation($"Adding player 2 to thread: {player2.Username}");
                    await thread2.AddThreadMemberAsync(player2);

                    _logger.LogInformation("Successfully added players to their respective threads");
                }
                catch (Exception threadEx)
                {
                    _logger.LogError(threadEx, "Failed to create or setup private threads");
                    // We still want to continue to create the match even if thread creation fails
                    // so we'll just log the error and continue
                }

                // Create map ban dropdown options
                _logger.LogInformation("Getting map pool for match");
                string?[] maps1v1 = _tournamentManager.GetTournamentMapPool(true).ToArray();

                var options = new List<DiscordSelectComponentOption>();
                foreach (var map in maps1v1)
                {
                    if (map is not null)
                    {
                        var option = new DiscordSelectComponentOption(map, map);
                        options.Add(option);
                    }
                }
                _logger.LogInformation($"Created {options.Count} map options");

                // Create map ban dropdown
                DiscordSelectComponent mapBanDropdown;
                string message;

                switch (matchLength)
                {
                    case 3:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                             "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    case 5:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                        break;
                    default:
                        mapBanDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 1, 1);
                        message = "**Scroll to see all map options!**\n\n" +
                            "Select 1 map to ban. This map will not be played in this match.\n\n" +
                            "**Note:** After making your selection, you'll have a chance to review your choice and confirm or revise it.";
                        break;
                }

                // Create winner selection dropdown
                _logger.LogInformation("Creating game winner selection dropdown for the first game");
                var winnerOptions = new List<DiscordSelectComponentOption>
                {
                    new DiscordSelectComponentOption(
                        $"{player1.DisplayName} wins",
                        $"game_winner:{player1.Id}",
                        $"{player1.DisplayName} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        $"{player2.DisplayName} wins",
                        $"game_winner:{player2.Id}",
                        $"{player2.DisplayName} wins this game"
                    ),
                    new DiscordSelectComponentOption(
                        "Draw",
                        "game_winner:draw",
                        "This game ended in a draw"
                    )
                };

                var winnerDropdown = new DiscordSelectComponent(
                    "tournament_game_winner_dropdown",
                    "Select the winner of this game",
                    winnerOptions,
                    false, 1, 1
                );

                // Create a custom property in the round to track match series state
                round.CustomProperties = new Dictionary<string, object>
                {
                    ["CurrentGame"] = 1,
                    ["Player1Wins"] = 0,
                    ["Player2Wins"] = 0,
                    ["Draws"] = 0,
                    ["MatchLength"] = matchLength,
                    ["Player1Id"] = player1.Id,
                    ["Player2Id"] = player2.Id
                };

                // Send messages to both threads if they were created successfully
                if (team1.Thread is not null)
                {
                    _logger.LogInformation("Sending map bans and instructions to player 1's thread");
                    try
                    {
                        // Send map bans and instructions
                        var dropdownBuilder = new DiscordMessageBuilder()
                            .WithContent($"{player1.Mention}\n\n{message}")
                            .AddComponents(mapBanDropdown);

                        await team1.Thread.SendMessageAsync(dropdownBuilder);

                        // Send match overview
                        var matchOverview = new DiscordEmbedBuilder()
                            .WithTitle($"Match: {player1.DisplayName} vs {player2.DisplayName}")
                            .WithDescription($"Best of {matchLength} series")
                            .AddField("Current Score", "0 - 0", true)
                            .AddField("Game", "1/" + (matchLength > 1 ? matchLength.ToString() : "1"), true)
                            .WithColor(DiscordColor.Blurple);

                        await team1.Thread.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(matchOverview));

                        // Send game winner dropdown
                        var winnerBuilder = new DiscordMessageBuilder()
                            .WithContent("**When this game is complete, select the winner:**")
                            .AddComponents(winnerDropdown);

                        await team1.Thread.SendMessageAsync(winnerBuilder);
                        _logger.LogInformation("Successfully sent components to player 1's thread");
                    }
                    catch (Exception msgEx)
                    {
                        _logger.LogError(msgEx, "Failed to send messages to player 1's thread");
                    }
                }

                // Send similar messages to player 2's thread
                if (team2.Thread is not null)
                {
                    _logger.LogInformation("Sending map bans and instructions to player 2's thread");
                    try
                    {
                        // Send map bans and instructions
                        var dropdownBuilder = new DiscordMessageBuilder()
                            .WithContent($"{player2.Mention}\n\n{message}")
                            .AddComponents(mapBanDropdown);

                        await team2.Thread.SendMessageAsync(dropdownBuilder);

                        // Send match overview
                        var matchOverview = new DiscordEmbedBuilder()
                            .WithTitle($"Match: {player1.DisplayName} vs {player2.DisplayName}")
                            .WithDescription($"Best of {matchLength} series")
                            .AddField("Current Score", "0 - 0", true)
                            .AddField("Game", "1/" + (matchLength > 1 ? matchLength.ToString() : "1"), true)
                            .WithColor(DiscordColor.Blurple);

                        await team2.Thread.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(matchOverview));

                        // Send game winner dropdown
                        var winnerBuilder = new DiscordMessageBuilder()
                            .WithContent("**When this game is complete, select the winner:**")
                            .AddComponents(winnerDropdown);

                        await team2.Thread.SendMessageAsync(winnerBuilder);
                        _logger.LogInformation("Successfully sent components to player 2's thread");
                    }
                    catch (Exception msgEx)
                    {
                        _logger.LogError(msgEx, "Failed to send messages to player 2's thread");
                    }
                }

                // Send announcement message to the channel
                _logger.LogInformation("Sending match announcement to channel");
                try
                {
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"🎮 Match Started: {player1.DisplayName} vs {player2.DisplayName}")
                        .WithDescription($"A new Bo{matchLength} match has started in Group {group?.Name}.")
                        .AddField("Players", $"{player1.Mention} vs {player2.Mention}", false)
                        .WithColor(DiscordColor.Blue)
                        .WithTimestamp(DateTime.Now);

                    await channel.SendMessageAsync(embed);
                    _logger.LogInformation("Successfully sent match announcement");
                }
                catch (Exception announceEx)
                {
                    _logger.LogError(announceEx, "Failed to send match announcement");
                }

                // Add to ongoing rounds and link to tournament match
                _logger.LogInformation("Adding round to ongoing rounds and linking to tournament match");
                _ongoingRounds.TourneyRounds.Add(round);
                match.LinkedRound = round;

                // Save tournament state
                _logger.LogInformation("Saving tournament state");
                await _tournamentManager.SaveTournamentState(client);

                _logger.LogInformation($"Successfully started match {match.Name} in Group {group?.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating 1v1 match between {player1?.DisplayName} and {player2?.DisplayName}");
            }
        }

        /// <inheritdoc/>
        public async Task SetupPlayoffStage(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Setting up playoff stage for tournament {tournament.Name}");
            // Delegate to TournamentManager to set up the playoffs
            _tournamentManager.SetupPlayoffs(tournament);

            // Post updated visualization
            await _tournamentManager.PostTournamentVisualization(tournament, client);

            // Start the playoff matches
            if (tournament.CurrentStage == TournamentStage.Playoffs)
            {
                await StartPlayoffMatches(tournament, client);
            }
        }

        /// <inheritdoc/>
        public int DetermineGroupCount(int playerCount, TournamentFormat format)
        {
            // For non-group formats, return 1
            if (format == TournamentFormat.SingleElimination || format == TournamentFormat.DoubleElimination)
            {
                return 1; // No groups for elimination formats
            }
            else if (format == TournamentFormat.RoundRobin)
            {
                return 1; // Single group for round robin
            }
            else // GroupStageWithPlayoffs
            {
                // Return group count according to the GroupStageFormat specifications
                return playerCount switch
                {
                    < 7 => 1,    // Small tournaments: 1 group
                    7 => 1,      // 1 group of 7
                    8 => 2,      // 2 groups of 4
                    9 => 3,      // 3 groups of 3
                    10 => 2,     // 2 groups of 5
                    11 => 3,     // 3 groups (4, 4, 3)
                    12 => 3,     // 3 groups of 4
                    13 => 3,     // 3 groups (4, 4, 5)
                    14 => 2,     // 2 groups of 7
                    15 => 3,     // 3 groups of 5
                    16 => 4,     // 4 groups of 4
                    17 => 3,     // 3 groups (6, 6, 5)
                    18 => 3,     // 3 groups of 6
                    _ => 4       // Large tournaments: use 4 groups
                };
            }
        }

        /// <inheritdoc/>
        public List<int> GetOptimalGroupSizes(int playerCount, int groupCount)
        {
            // Given the player count and group count, determine the optimal size for each group
            List<int> groupSizes = new List<int>();

            switch (playerCount)
            {
                case 7:
                    groupSizes.Add(7); // 1 group of 7
                    break;
                case 8:
                    groupSizes.Add(4); groupSizes.Add(4); // 2 groups of 4
                    break;
                case 9:
                    groupSizes.Add(3); groupSizes.Add(3); groupSizes.Add(3); // 3 groups of 3
                    break;
                case 10:
                    groupSizes.Add(5); groupSizes.Add(5); // 2 groups of 5
                    break;
                case 11:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(3); // 3 groups (4, 4, 3)
                    break;
                case 12:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); // 3 groups of 4
                    break;
                case 13:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(5); // 3 groups (4, 4, 5)
                    break;
                case 14:
                    groupSizes.Add(7); groupSizes.Add(7); // 2 groups of 7
                    break;
                case 15:
                    groupSizes.Add(5); groupSizes.Add(5); groupSizes.Add(5); // 3 groups of 5
                    break;
                case 16:
                    groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); groupSizes.Add(4); // 4 groups of 4
                    break;
                case 17:
                    groupSizes.Add(6); groupSizes.Add(6); groupSizes.Add(5); // 3 groups (6, 6, 5)
                    break;
                case 18:
                    groupSizes.Add(6); groupSizes.Add(6); groupSizes.Add(6); // 3 groups of 6
                    break;
                default:
                    // For player counts not explicitly defined, distribute players evenly
                    int baseSize = playerCount / groupCount;
                    int remainder = playerCount % groupCount;

                    for (int i = 0; i < groupCount; i++)
                    {
                        // Give the first 'remainder' groups one extra player
                        groupSizes.Add(baseSize + (i < remainder ? 1 : 0));
                    }
                    break;
            }

            return groupSizes;
        }

        /// <inheritdoc/>
        public (int groupWinners, int bestThirdPlace) GetAdvancementCriteria(int playerCount, int groupCount)
        {
            // Return a tuple (groupWinners, bestThirdPlace) that specifies how many top players
            // from each group advance, and how many best third-place players advance

            return playerCount switch
            {
                7 => (4, 0),      // Top 4 advance to playoffs
                8 => (2, 0),      // Top 2 from each group
                9 => (2, 2),      // Top 2 from each group + 2 best third-place
                10 => (2, 0),     // Top 2 from each group
                11 => (2, 2),     // Top 2 from each group + 2 best third-place
                12 => (2, 2),     // Top 2 from each group + 2 best third-place
                13 => (2, 2),     // Top 2 from each group + 2 best third-place
                14 => (4, 0),     // Top 4 from each group
                15 => (2, 2),     // Top 2 from each group + 2 best third-place
                16 => (2, 0),     // Top 2 from each group
                17 => (2, 2),     // Top 2 from each group + 2 best third-place
                18 => (2, 2),     // Top 2 from each group + 2 best third-place
                _ => groupCount < 3 ? (2, 0) : (2, 2) // Default based on group count
            };
        }
    }
}