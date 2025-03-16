using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.BotClient.Config;
using System.IO;
using Wabbit.Data;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing tournament matches, extracted from TournamentManagementGroup
    /// </summary>
    public class TournamentMatchService : ITournamentMatchService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentMapService _mapService;
        private readonly ILogger<TournamentMatchService> _logger;
        private readonly IMatchStatusService _matchStatusService;
        private readonly ITournamentMatchOperationsService _matchOperations;

        private const int autoDeleteSeconds = 30;
        private const int mapThumbnailDurationMinutes = 5;

        public TournamentMatchService(
            OngoingRounds ongoingRounds,
            ITournamentGameService tournamentGameService,
            ITournamentPlayoffService playoffService,
            ITournamentStateService stateService,
            ITournamentMapService mapService,
            ILogger<TournamentMatchService> logger,
            IMatchStatusService matchStatusService,
            ITournamentMatchOperationsService matchOperations)
        {
            _ongoingRounds = ongoingRounds ?? throw new ArgumentNullException(nameof(ongoingRounds));
            _tournamentGameService = tournamentGameService ?? throw new ArgumentNullException(nameof(tournamentGameService));
            _playoffService = playoffService ?? throw new ArgumentNullException(nameof(playoffService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _matchStatusService = matchStatusService ?? throw new ArgumentNullException(nameof(matchStatusService));
            _matchOperations = matchOperations ?? throw new ArgumentNullException(nameof(matchOperations));
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
            try
            {
                if (!_matchOperations.ValidateMatchCreation(tournament, player1, player2))
                {
                    _logger.LogError("Match creation validation failed");
                    return;
                }

                Tournament.Match match;
                if (existingMatch != null)
                {
                    match = existingMatch;
                }
                else
                {
                    match = _matchOperations.CreateMatch(
                        $"{player1.DisplayName} vs {player2.DisplayName}",
                        group != null ? TournamentMatchType.GroupStage : TournamentMatchType.Quarterfinal,
                        matchLength,
                        player1,
                        player2,
                        group);

                    if (group == null)
                    {
                        tournament.PlayoffMatches ??= new List<Tournament.Match>();
                        tournament.PlayoffMatches.Add(match);
                    }
                }

                // Create the Round object
                var round = new Round
                {
                    Name = match.Name,
                    Length = matchLength,
                    OneVOne = true,
                    Teams = new List<Round.Team>(),
                    TournamentId = tournament?.Name,
                    MsgToDel = new List<DiscordMessage>(),
                    TournamentRound = true
                };

                // Set group stage match information
                if (match.Type == TournamentMatchType.GroupStage && group is not null)
                {
                    // Calculate total matches per player in this group
                    int groupSize = group.Participants?.Count ?? 0;
                    int totalMatchesPerPlayer = Math.Max(0, groupSize - 1); // Each player plays against every other player once

                    // Calculate match number for this specific match in player1's sequence
                    int player1MatchesPlayed = 0;

                    if (player1 is not null && group.Matches is not null)
                    {
                        player1MatchesPlayed = group.Matches.Count(m =>
                            m?.Participants?.Any(p =>
                                p?.Player != null &&
                                player1 is not null &&
                                p.Player.ToString() == player1.Id.ToString() &&
                                m != match) ?? false);
                    }

                    round.GroupStageMatchNumber = player1MatchesPlayed + 1;
                    round.TotalGroupStageMatches = totalMatchesPerPlayer;
                }

                // Initialize CustomProperties if needed
                if (round.CustomProperties is null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }

                // Store the match in the round's custom properties
                round.CustomProperties["TournamentMatch"] = match;
                round.CustomProperties["CreatedAt"] = DateTime.Now;
                round.CustomProperties["RoundId"] = Guid.NewGuid().ToString();

                // Create map ban dropdown options
                _logger.LogInformation("Getting map pool for match");
                string[] maps1v1 = _mapService.GetTournamentMapPool(true).ToArray();

                // Initialize the Maps collection
                round.Maps = new List<string>();

                // Create teams
                var team1 = new Round.Team
                {
                    Name = player1?.DisplayName ?? "Player 1",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player1 }
                    },
                    MapBans = new List<string>()
                };

                var team2 = new Round.Team
                {
                    Name = player2?.DisplayName ?? "Player 2",
                    Participants = new List<Round.Participant>
                    {
                        new Round.Participant { Player = player2 }
                    },
                    MapBans = new List<string>()
                };

                // Add teams to round
                round.Teams.Add(team1);
                round.Teams.Add(team2);

                // Set up metadata for the match
                if (round.CustomProperties is not null)
                {
                    round.CustomProperties["Player1Name"] = player1?.DisplayName ?? "Player 1";
                    round.CustomProperties["Player2Name"] = player2?.DisplayName ?? "Player 2";
                    round.CustomProperties["Player1Score"] = 0;
                    round.CustomProperties["Player2Score"] = 0;
                    round.CustomProperties["Player1Wins"] = 0;
                    round.CustomProperties["Player2Wins"] = 0;
                    round.CustomProperties["Draws"] = 0;
                    round.CustomProperties["MatchLength"] = matchLength;
                    round.CustomProperties["Player1Id"] = player1?.Id ?? 0;
                    round.CustomProperties["Player2Id"] = player2?.Id ?? 0;
                }

                // Add the round to ongoing rounds
                _ongoingRounds.TourneyRounds.Add(round);

                // Link the round to the match
                match.LinkedRound = round;

                // Initialize the match status system
                try
                {
                    if (tournament?.AnnouncementChannel is null)
                    {
                        _logger.LogWarning($"No announcement channel found for tournament {tournament?.Name}");
                        return;
                    }
                    await _matchStatusService.CreateNewMatchStatusAsync(tournament.AnnouncementChannel, round, client);
                    await _matchStatusService.UpdateToMapBanStageAsync(tournament.AnnouncementChannel, round, client);
                    _logger.LogInformation($"Match status initialized for match {match.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to initialize match status for match {match.Name}");
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating match");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task UpdateMatchResultAsync(
            Tournament? tournament,
            Tournament.Match? match,
            DiscordMember? winner,
            int winnerScore,
            int loserScore)
        {
            if (tournament == null || match == null || winner is null)
            {
                _logger.LogError("Cannot update match result: one or more parameters are null");
                return;
            }

            await _matchOperations.UpdateMatchResultAsync(tournament, match, winner, winnerScore, loserScore);

            // Handle match completion
            await HandleMatchCompletion(tournament, match, null);
        }

        /// <inheritdoc/>
        public async Task HandleMatchCompletion(
            Tournament tournament,
            Tournament.Match match,
            DiscordClient? client)
        {
            _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

            // Save tournament state
            if (client != null)
            {
                await _stateService.SaveTournamentStateAsync(client);

                // Generate a new visualization
                try
                {
                    await Misc.TournamentVisualization.GenerateStandingsImage(tournament, client, _stateService);
                    _logger.LogInformation($"Generated updated standings visualization for tournament {tournament.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error generating standings visualization for tournament {tournament.Name}: {ex.Message}");
                }
            }

            // Check if tournament is complete
            bool allPlayoffMatchesComplete = tournament.PlayoffMatches != null &&
                tournament.PlayoffMatches.Count > 0 &&
                tournament.PlayoffMatches.All(m => m.IsComplete);

            if (tournament.CurrentStage == TournamentStage.Playoffs && allPlayoffMatchesComplete)
            {
                tournament.CurrentStage = TournamentStage.Complete;
                tournament.IsComplete = true;
                _logger.LogInformation($"Tournament {tournament.Name} is now complete");
            }
        }

        /// <inheritdoc/>
        public async Task ArchiveMatchThreadsAsync(
            Tournament.Match match,
            DiscordClient client,
            TimeSpan? archiveDuration = null)
        {
            try
            {
                _logger.LogInformation($"Archiving threads for match {match.Name}");

                // Convert TimeSpan to DiscordAutoArchiveDuration
                var archiveDurationEnum = archiveDuration?.TotalMinutes switch
                {
                    <= 60 => DiscordAutoArchiveDuration.Hour,
                    <= 1440 => DiscordAutoArchiveDuration.Day,
                    <= 10080 => DiscordAutoArchiveDuration.Week,
                    _ => DiscordAutoArchiveDuration.Week // Default to a week for longer durations
                };

                // Find and archive team threads
                if (match.LinkedRound?.Teams != null)
                {
                    foreach (var team in match.LinkedRound.Teams)
                    {
                        try
                        {
                            if (team.Thread is not null)
                            {
                                await team.Thread.ModifyAsync(props =>
                                {
                                    props.Locked = true;
                                    props.AutoArchiveDuration = archiveDurationEnum;
                                });
                                _logger.LogInformation($"Archived team thread {team.Thread.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error archiving team thread: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error archiving match threads: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if both players have submitted their decks for the current game
        /// </summary>
        private bool AreDeckSubmissionsComplete(Round round, int gameNumber)
        {
            if (round.CustomProperties?.ContainsKey("DeckCodes") != true)
                return false;

            var deckCodes = round.CustomProperties["DeckCodes"] as Dictionary<string, Dictionary<string, string>>;
            if (deckCodes == null)
                return false;

            // Check if both players have submitted decks for this game
            int submissionCount = 0;
            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is null) continue;

                    string userId = participant.Player.Id.ToString();
                    if (deckCodes.Any(dc => dc.Value.ContainsKey(userId)))
                    {
                        submissionCount++;
                    }
                }
            }

            return submissionCount >= 2;
        }

        /// <summary>
        /// Sends a map thumbnail that will be automatically deleted after a specified duration
        /// </summary>
        private async Task SendMapThumbnailAsync(DiscordChannel channel, string mapName, DiscordClient client)
        {
            try
            {
                // Get the map data from Maps collection
                var map = Maps.MapCollection?.FirstOrDefault(m =>
                    string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));

                if (map?.Thumbnail is null)
                {
                    _logger.LogWarning($"No thumbnail found for map: {mapName}");
                    return;
                }

                // Create the base embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🗺️ Next Map: {mapName}")
                    .WithColor(new DiscordColor(75, 181, 67))
                    .WithFooter("This message will be automatically deleted in 5 minutes.");

                // Create the webhook builder
                var webhookBuilder = new DiscordWebhookBuilder().AddEmbed(embed);

                if (map.Thumbnail.StartsWith("http"))
                {
                    // It's a URL, use it directly
                    embed.ImageUrl = map.Thumbnail;
                }
                else
                {
                    // It's a local file, we need to attach it
                    string relativePath = map.Thumbnail;

                    // Normalize path separators
                    relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);

                    // Construct the full path
                    string baseDirectory = Directory.GetCurrentDirectory();
                    string fullPath = Path.Combine(baseDirectory, relativePath);

                    if (!File.Exists(fullPath))
                    {
                        _logger.LogWarning($"Thumbnail file not found at: {fullPath}");
                        return;
                    }

                    // Add the file as an attachment
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    webhookBuilder.AddFile(Path.GetFileName(fullPath), fileStream);
                }

                // Send the thumbnail message
                var messageBuilder = new DiscordMessageBuilder(webhookBuilder);
                var thumbnailMessage = await channel.SendMessageAsync(messageBuilder);

                // Schedule message deletion
                _ = Task.Delay(TimeSpan.FromMinutes(mapThumbnailDurationMinutes))
                    .ContinueWith(async _ =>
                    {
                        try
                        {
                            await thumbnailMessage.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting map thumbnail message");
                        }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending map thumbnail");
            }
        }

        /// <summary>
        /// Handles deck submission and reveals map if both players have submitted
        /// </summary>
        public async Task HandleDeckSubmissionAsync(Round round, DiscordChannel channel, DiscordClient client)
        {
            try
            {
                // Get the current game number
                int currentGame = round.Maps?.Count ?? 0;

                // Check if both players have submitted their decks
                if (AreDeckSubmissionsComplete(round, currentGame))
                {
                    _logger.LogInformation($"Both players have submitted decks for game {currentGame + 1}. Revealing map...");

                    // Get available maps
                    var availableMaps = _mapService.GetAvailableMapsForNextGame(round);
                    if (availableMaps.Count == 0)
                    {
                        _logger.LogWarning("No maps available for next game");
                        return;
                    }

                    // Select a random map
                    var random = new Random();
                    string nextMap = availableMaps[random.Next(availableMaps.Count)];

                    // Add the map to the round's map list
                    if (round.Maps is null)
                    {
                        round.Maps = new List<string>();
                    }
                    round.Maps.Add(nextMap);

                    // Update the match status with the new map
                    await _matchStatusService.UpdateMapInformationAsync(channel, round, client);

                    // Send map thumbnail in a separate message
                    await SendMapThumbnailAsync(channel, nextMap, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling deck submission");
            }
        }

        /// <inheritdoc/>
        public async Task SetupPlayoffStage(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Setting up playoff stage for tournament {tournament.Name}");

            // Delegate playoff setup to the specialized playoff service
            await _playoffService.SetupPlayoffsAsync(tournament, client);

            // Post updated visualization through appropriate service
            // This could be an ITournamentVisualizationService or similar
            if (tournament.AnnouncementChannel is not null)
            {
                _logger.LogInformation("Tournament playoff stage ready for visualization");
            }

            // Start the playoff matches
            if (tournament.CurrentStage == TournamentStage.Playoffs)
            {
                await _playoffService.StartPlayoffMatchesAsync(tournament, client);
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

        /// <summary>
        /// Determines the match type based on context
        /// </summary>
        private TournamentMatchType DetermineMatchType(Tournament.Group? group, Tournament.Match? existingMatch)
        {
            if (existingMatch?.IsTiebreakerMatch == true)
            {
                return TournamentMatchType.GroupStageTiebreaker;
            }
            return group is not null ? TournamentMatchType.GroupStage : TournamentMatchType.Quarterfinal;
        }

        /// <summary>
        /// Determines if the match should use Best-of-3 format
        /// </summary>
        private bool ShouldUseBestOfThree(Tournament.Group? group, Tournament.Match? existingMatch)
        {
            // Tiebreaker matches are always Bo3
            if (existingMatch?.IsTiebreakerMatch == true)
            {
                return true;
            }

            // Group stage matches are Bo1, playoffs are Bo3
            return group is null || existingMatch?.Type == TournamentMatchType.GroupStageTiebreaker;
        }

        // ... other methods ...
    }
}