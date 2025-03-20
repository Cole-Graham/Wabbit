using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;
using Wabbit.BotClient.Config;
using System.Text;
using Wabbit.Data;
using Wabbit.Services.ServiceHelpers;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing scrimmage status and related operations
    /// </summary>
    public class ScrimmageStatusService : IScrimmageStatusService
    {
        private readonly ILogger<ScrimmageStatusService> _logger;
        private readonly DiscordClient _client;
        private readonly IMapService _mapService;
        private readonly OngoingRounds _ongoingRounds;
        private readonly IRandomProvider _randomProvider;

        public ScrimmageStatusService(
            ILogger<ScrimmageStatusService> logger,
            DiscordClient client,
            IMapService mapService,
            OngoingRounds ongoingRounds,
            IRandomProvider randomProvider)
        {
            _logger = logger;
            _client = client;
            _mapService = mapService;
            _ongoingRounds = ongoingRounds;
            _randomProvider = randomProvider;
        }

        /// <inheritdoc/>
        public async Task<Scrimmage> CreateScrimmageAsync(
            DiscordChannel channel,
            DiscordUser player1,
            string? deck1,
            DiscordUser player2,
            string? deck2,
            ScrimmageGameType gameType,
            MatchLength matchLength,
            bool isRated,
            bool useTournamentMapPool)
        {
            // Validate arguments
            if (player1.Id == player2.Id)
            {
                throw new ArgumentException("A player cannot play against themselves");
            }

            // Create a thread for the scrimmage
            var threadChannel = await DiscordUtilities.CreateThreadAsync(
                channel,
                $"{gameType} Scrimmage: {player1.Username} vs {player2.Username}",
                _logger,
                DiscordChannelType.PrivateThread,
                DiscordAutoArchiveDuration.Day);

            if (threadChannel is null)
            {
                throw new InvalidOperationException("Failed to create thread for scrimmage");
            }

            // Create the scrimmage with the required thread property
            var scrimmage = new Scrimmage
            {
                Thread = threadChannel,
                GameType = gameType,
                MatchLength = matchLength,
                IsRated = isRated,
                UseTournamentMapPool = useTournamentMapPool,
                Status = ScrimmageStatus.Created
            };

            // Initialize teams
            scrimmage.TeamA = new ScrimmageTeam { Captain = player1 };
            if (deck1 != null)
                scrimmage.TeamA.SetDeckCode(player1, deck1);

            scrimmage.TeamB = new ScrimmageTeam { Captain = player2 };
            if (deck2 != null)
                scrimmage.TeamB.SetDeckCode(player2, deck2);

            // Post initial status message
            var statusMessage = await UpdateScrimmageStatusAsync(scrimmage);
            scrimmage.StatusMessage = statusMessage;

            // Add scrimmage to ongoing rounds
            _ongoingRounds.ScrimmageRounds.Add(scrimmage);

            // Generate first map using our optimized map selection method
            string mapName = GetRandomMapForScrimmage(scrimmage);
            scrimmage.Maps.Add(mapName);

            _logger.LogInformation($"Created scrimmage between {player1.Username} and {player2.Username} in {channel.Guild.Name}/{channel.Name}");
            return scrimmage;
        }

        /// <inheritdoc/>
        public async Task<DiscordMessage> UpdateScrimmageStatusAsync(Scrimmage scrimmage)
        {
            return await UpdateScrimmageStatusAsync(scrimmage, null);
        }

        /// <summary>
        /// Updates the scrimmage status message with current information, from a specific channel's perspective
        /// </summary>
        /// <param name="scrimmage">The scrimmage to update</param>
        /// <param name="channelId">Optional channel ID to determine team perspective</param>
        /// <returns>The updated status message</returns>
        public async Task<DiscordMessage> UpdateScrimmageStatusAsync(Scrimmage scrimmage, ulong? channelId)
        {
            // Build a more informative title
            string title = $"{GetGameTypeString(scrimmage.GameType)} Scrimmage: {GetMatchLengthString(scrimmage.MatchLength)}";

            // Create a subtitle with player info and game progress
            string subtitle = $"Match: {scrimmage.TeamA.Captain.Username} vs {scrimmage.TeamB.Captain.Username}";

            // Add current game indicator if games have been played
            if (scrimmage.CurrentGameNumber > 0)
            {
                int gamesNeeded = GetGamesToWin(scrimmage.MatchLength);
                int totalGames = gamesNeeded * 2 - 1; // Maximum possible games
                subtitle += $", Game {scrimmage.CurrentGameNumber} of {totalGames}";
            }

            // Create the status embed
            var embed = new DiscordEmbedBuilder()
                .WithTitle(title)
                .WithColor(GetStatusColor(scrimmage.Status))
                .WithFooter($"Created {scrimmage.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");

            // Add match progress bar with subtitle
            string progressBar = GetScrimmageProgressBar(scrimmage);
            embed.WithDescription(subtitle + "\n\n" + progressBar);

            // Add map bans if available
            if (scrimmage.TeamAMapBans.Any() || scrimmage.TeamBMapBans.Any() ||
                scrimmage.TeamAUnconfirmedMapBans.Any() || scrimmage.TeamBUnconfirmedMapBans.Any())
            {
                AddMapBansField(embed, scrimmage, channelId);
            }

            // Add deck submissions with team perspective
            AddDeckSubmissionsWithTeamPerspective(embed, scrimmage, channelId);

            // Add current map if available
            if (scrimmage.Maps.Count > 0)
            {
                string currentMap = scrimmage.Maps[scrimmage.Maps.Count - 1];
                embed.AddField("Current Map", currentMap, true);
            }

            // Add previous game results if any
            if (scrimmage.Results.Count > 0 || scrimmage.Maps.Count > 0)
            {
                // Use shared helper for consistent tree-style game results
                string gameResultsFormatted = GameResultHelpers.FormatGameResults(
                    scrimmage.Results,
                    scrimmage.Maps,
                    scrimmage.TeamA.Captain.Username,
                    scrimmage.TeamB.Captain.Username);

                embed.AddField("🎮 Game Results", gameResultsFormatted, false);
            }

            // Add stage-specific information
            AddStageSpecificInfo(embed, scrimmage, channelId);

            // Additional info for rated matches
            if (scrimmage.IsRated)
            {
                embed.AddField("Rating", "This is a rated match", true);
            }

            // Additional info for tournament map pool
            if (scrimmage.UseTournamentMapPool)
            {
                embed.AddField("Maps", "Using tournament map pool", true);
            }

            // Default to Team A perspective for now
            // This will be enhanced later when we have proper user tracking
            bool isTeamA = true;

            // Create buttons based on the current status/stage and team perspective
            var buttons = CreateStatusButtons(scrimmage, isTeamA);

            // Create message
            var builder = new DiscordMessageBuilder();
            builder.AddEmbed(embed.Build());

            // Add appropriate components based on the current stage
            // Determine which stage we're in using pattern matching
            MatchStage currentStage = DetermineCurrentStage(scrimmage);

            // Add stage-specific components
            if (currentStage == MatchStage.MapBan &&
                scrimmage.Status == ScrimmageStatus.InProgress &&
                !scrimmage.TeamAMapBans.Any() && !scrimmage.TeamBMapBans.Any())
            {
                // In map ban stage, add the map ban dropdown
                var mapBanDropdown = CreateMapBanDropdownAsync(scrimmage);
                builder.AddComponents(mapBanDropdown);
            }
            else if (buttons.Any())
            {
                // For other stages, add the appropriate buttons
                builder.AddComponents(buttons);
            }

            // Send or update the message
            DiscordMessage message;
            if (scrimmage.StatusMessage == null)
            {
                message = await scrimmage.Thread.SendMessageAsync(builder);
            }
            else
            {
                message = await scrimmage.StatusMessage.ModifyAsync(builder);
            }

            return message;
        }

        /// <summary>
        /// Adds a field with map ban information to the embed
        /// </summary>
        private void AddMapBansField(DiscordEmbedBuilder embed, Scrimmage scrimmage, ulong? channelId = null)
        {
            // Determine which team's perspective to show
            // We can enhance this later to actually determine which team the user is on
            // For now, we'll default to Team A's perspective since we don't have user tracking
            bool isTeamA = true;

            // For unconfirmed map bans, we need to handle them separately
            if (isTeamA && scrimmage.TeamAUnconfirmedMapBans.Any())
            {
                // Format from Team A perspective with unconfirmed bans
                string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                    new List<string>(), // Empty list instead of null
                    scrimmage.TeamBMapBans.Any() || scrimmage.TeamBUnconfirmedMapBans.Any(),
                    scrimmage.TeamAUnconfirmedMapBans,
                    scrimmage.MatchLength);

                embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
            }
            else if (!isTeamA && scrimmage.TeamBUnconfirmedMapBans.Any())
            {
                // Format from Team B perspective with unconfirmed bans
                string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                    new List<string>(), // Empty list instead of null
                    scrimmage.TeamAMapBans.Any() || scrimmage.TeamAUnconfirmedMapBans.Any(),
                    scrimmage.TeamBUnconfirmedMapBans,
                    scrimmage.MatchLength);

                embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
            }
            else if (isTeamA && scrimmage.TeamBUnconfirmedMapBans.Any())
            {
                // If Team B has unconfirmed bans but user is Team A
                string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                    scrimmage.TeamAMapBans,
                    true, // Opponent has submitted something (unconfirmed)
                    new List<string>(), // Empty list instead of null
                    scrimmage.MatchLength);

                embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
            }
            else if (!isTeamA && scrimmage.TeamAUnconfirmedMapBans.Any())
            {
                // If Team A has unconfirmed bans but user is Team B
                string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                    scrimmage.TeamBMapBans,
                    true, // Opponent has submitted something (unconfirmed)
                    new List<string>(), // Empty list instead of null
                    scrimmage.MatchLength);

                embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
            }
            else
            {
                // Normal case - format based on team perspective
                if (isTeamA)
                {
                    string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                        scrimmage.TeamAMapBans,
                        scrimmage.TeamBMapBans.Any(),
                        new List<string>(), // Empty list instead of null
                        scrimmage.MatchLength);

                    embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
                }
                else
                {
                    string mapBansFormatted = MapBanHelpers.FormatTeamPerspectiveBans(
                        scrimmage.TeamBMapBans,
                        scrimmage.TeamAMapBans.Any(),
                        new List<string>(), // Empty list instead of null
                        scrimmage.MatchLength);

                    embed.AddField("🗺️ Map Bans", mapBansFormatted, false);
                }
            }
        }

        /// <summary>
        /// Adds deck submission information to the embed with team perspective
        /// </summary>
        private void AddDeckSubmissionsWithTeamPerspective(
            DiscordEmbedBuilder embed,
            Scrimmage scrimmage,
            ulong? channelId = null)
        {
            var deckBuilder = new StringBuilder();

            // Determine which team's perspective to show
            // For now, we'll default to Team A's perspective since we don't have user tracking
            // In the future, enhance this to use channelId to figure out which team the user is on
            bool isTeamA = true;

            // TODO: When we have more information about the viewer, use channelId to determine team
            // if (channelId.HasValue && someWayToKnowIfChannelBelongsToTeamB)
            // {
            //     isTeamA = false;
            // }

            if (isTeamA)
            {
                // Team A perspective
                if (!string.IsNullOrEmpty(scrimmage.TeamA.DeckName))
                {
                    deckBuilder.AppendLine("My Team Deck: ✅ Submitted");
                    deckBuilder.AppendLine($"Deck code: `{scrimmage.TeamA.DeckName}`");
                }
                else
                {
                    deckBuilder.AppendLine("My Team Deck: Not submitted yet");
                    deckBuilder.AppendLine("Use `/scrimmage submit_deck` to submit your deck");
                }

                // Opponent's deck status (Team B) - don't show code
                deckBuilder.AppendLine();
                if (!string.IsNullOrEmpty(scrimmage.TeamB.DeckName))
                {
                    deckBuilder.AppendLine("Opponent Deck: ✅ Submitted");
                }
                else
                {
                    deckBuilder.AppendLine("Opponent Deck: ⏳ Waiting for submission");
                }
            }
            else
            {
                // Team B perspective
                if (!string.IsNullOrEmpty(scrimmage.TeamB.DeckName))
                {
                    deckBuilder.AppendLine("My Team Deck: ✅ Submitted");
                    deckBuilder.AppendLine($"Deck code: `{scrimmage.TeamB.DeckName}`");
                }
                else
                {
                    deckBuilder.AppendLine("My Team Deck: Not submitted yet");
                    deckBuilder.AppendLine("Use `/scrimmage submit_deck` to submit your deck");
                }

                // Opponent's deck status (Team A) - don't show code
                deckBuilder.AppendLine();
                if (!string.IsNullOrEmpty(scrimmage.TeamA.DeckName))
                {
                    deckBuilder.AppendLine("Opponent Deck: ✅ Submitted");
                }
                else
                {
                    deckBuilder.AppendLine("Opponent Deck: ⏳ Waiting for submission");
                }
            }

            embed.AddField("🃏 Deck Submissions", deckBuilder.ToString().Trim(), false);
        }

        /// <summary>
        /// Creates a progress bar indicating the current stage of the scrimmage
        /// </summary>
        private string GetScrimmageProgressBar(Scrimmage scrimmage)
        {
            // Determine which stage we're in using pattern matching
            MatchStage currentStage = scrimmage.Status switch
            {
                ScrimmageStatus.Completed => MatchStage.Completed,
                ScrimmageStatus.Created => MatchStage.Created,
                ScrimmageStatus.InProgress when scrimmage.TeamAScore > 0 || scrimmage.TeamBScore > 0 => MatchStage.GameResults,
                ScrimmageStatus.InProgress when !string.IsNullOrEmpty(scrimmage.TeamA.DeckName) || !string.IsNullOrEmpty(scrimmage.TeamB.DeckName) => MatchStage.DeckSubmission,
                ScrimmageStatus.InProgress => MatchStage.MapBan,
                _ => MatchStage.Created
            };

            // Format the status message based on current status
            string statusMsg = scrimmage.Status switch
            {
                ScrimmageStatus.Created => "Match created. Waiting for players to ready up.",
                ScrimmageStatus.InProgress => "Match in progress.",
                ScrimmageStatus.Completed => $"Match completed. {(scrimmage.TeamAScore > scrimmage.TeamBScore ? scrimmage.TeamA.Captain.Username : scrimmage.TeamB.Captain.Username)} wins!",
                _ => "Unknown status."
            };

            // Creating a progress bar with the standardized helper
            var progressBuilder = new StringBuilder();
            progressBuilder.AppendLine(statusMsg);
            progressBuilder.AppendLine();

            // Use the shared helper to create the progress bar
            progressBuilder.AppendLine(StatusEmbedHelpers.CreateStageProgressBar(currentStage));

            return progressBuilder.ToString();
        }

        /// <summary>
        /// Adds stage-specific information to the embed
        /// </summary>
        private void AddStageSpecificInfo(DiscordEmbedBuilder embed, Scrimmage scrimmage, ulong? channelId = null)
        {
            // Determine which stage we're in using pattern matching
            MatchStage currentStage = scrimmage.Status switch
            {
                ScrimmageStatus.Completed => MatchStage.Completed,
                ScrimmageStatus.Created => MatchStage.Created,
                ScrimmageStatus.InProgress when scrimmage.TeamAScore > 0 || scrimmage.TeamBScore > 0 => MatchStage.GameResults,
                ScrimmageStatus.InProgress when !string.IsNullOrEmpty(scrimmage.TeamA.DeckName) || !string.IsNullOrEmpty(scrimmage.TeamB.DeckName) => MatchStage.DeckSubmission,
                ScrimmageStatus.InProgress => MatchStage.MapBan,
                _ => MatchStage.Created
            };

            // Get context-sensitive instructions based on the user's team
            // For now, default to Team A's perspective
            bool isTeamA = true;
            string instructions = GetContextSensitiveInstructions(scrimmage, currentStage, isTeamA, channelId);

            if (!string.IsNullOrEmpty(instructions))
            {
                embed.AddField("📝 Instructions", instructions, false);
            }
        }

        /// <summary>
        /// Generates context-sensitive instructions based on team perspective and match stage
        /// </summary>
        private string GetContextSensitiveInstructions(
            Scrimmage scrimmage,
            MatchStage currentStage,
            bool isTeamA = true,
            ulong? channelId = null)
        {
            // TODO: When we have more information about the viewer, use channelId to determine team
            // if (channelId.HasValue && someWayToKnowIfChannelBelongsToTeamB)
            // {
            //     isTeamA = false;
            // }

            switch (currentStage)
            {
                case MatchStage.Created:
                    return "Both players need to ready up to begin the match.";

                case MatchStage.MapBan:
                    if (isTeamA && scrimmage.TeamAUnconfirmedMapBans.Any())
                        return "Review your map ban selections above and choose to confirm or revise them.";
                    else if (!isTeamA && scrimmage.TeamBUnconfirmedMapBans.Any())
                        return "Review your map ban selections above and choose to confirm or revise them.";
                    else if (isTeamA && !scrimmage.TeamAMapBans.Any())
                        return "Select maps to ban using the dropdown below, ordered by priority.";
                    else if (!isTeamA && !scrimmage.TeamBMapBans.Any())
                        return "Select maps to ban using the dropdown below, ordered by priority.";
                    else if (isTeamA && !scrimmage.TeamBMapBans.Any())
                        return "Waiting for your opponent to select their map bans.";
                    else if (!isTeamA && !scrimmage.TeamAMapBans.Any())
                        return "Waiting for your opponent to select their map bans.";
                    else
                        return "Both teams have completed map bans. Moving to deck submission stage.";

                case MatchStage.DeckSubmission:
                    if (isTeamA && string.IsNullOrEmpty(scrimmage.TeamA.DeckName))
                        return "Submit your deck using `/scrimmage submit_deck`.";
                    else if (!isTeamA && string.IsNullOrEmpty(scrimmage.TeamB.DeckName))
                        return "Submit your deck using `/scrimmage submit_deck`.";
                    else if (isTeamA && string.IsNullOrEmpty(scrimmage.TeamB.DeckName))
                        return "Waiting for your opponent to submit their deck.";
                    else if (!isTeamA && string.IsNullOrEmpty(scrimmage.TeamA.DeckName))
                        return "Waiting for your opponent to submit their deck.";
                    else
                        return "Both teams have submitted decks. The match will begin soon.";

                case MatchStage.GameResults:
                    return "Play your game and report the result using the buttons below.";

                case MatchStage.Completed:
                    return $"Match completed. {(scrimmage.TeamAScore > scrimmage.TeamBScore ? scrimmage.TeamA.Captain.Username : scrimmage.TeamB.Captain.Username)} wins!";

                default:
                    return StatusEmbedHelpers.GetStageInstructions(currentStage);
            }
        }

        /// <summary>
        /// Creates interactive buttons based on the scrimmage stage and team state
        /// </summary>
        /// <param name="scrimmage">The scrimmage to create buttons for</param>
        /// <param name="isTeamA">Whether to show components for Team A (true) or Team B (false)</param>
        /// <returns>List of components appropriate for the current team and stage</returns>
        private List<DiscordComponent> CreateStatusButtons(Scrimmage scrimmage, bool isTeamA = true)
        {
            var buttons = new List<DiscordComponent>();
            string scrimmageId = scrimmage.GetHashCode().ToString();

            // Skip buttons for completed matches
            if (scrimmage.Status == ScrimmageStatus.Completed)
            {
                return buttons;
            }

            // Always include refresh button for in-progress and created matches
            if (scrimmage.Status != ScrimmageStatus.Cancelled)
            {
                buttons.Add(ComponentHelpers.CreateRefreshButton(scrimmageId));
            }

            // Handle buttons based on current status and stage
            switch (scrimmage.Status)
            {
                case ScrimmageStatus.Created:
                    // Ready up buttons - available to both teams
                    buttons.Add(ComponentHelpers.CreateReadyButton(scrimmageId));
                    break;

                case ScrimmageStatus.InProgress:
                    // Determine current stage
                    MatchStage currentStage = DetermineCurrentStage(scrimmage);

                    switch (currentStage)
                    {
                        case MatchStage.MapBan:
                            // For map ban stage, show dropdown only if this team hasn't confirmed bans yet
                            bool hasConfirmedBans = isTeamA
                                ? scrimmage.TeamAMapBans.Any()
                                : scrimmage.TeamBMapBans.Any();

                            bool hasUnconfirmedBans = isTeamA
                                ? scrimmage.TeamAUnconfirmedMapBans.Any()
                                : scrimmage.TeamBUnconfirmedMapBans.Any();

                            if (hasUnconfirmedBans)
                            {
                                // If team has unconfirmed bans, only show confirm/revise buttons
                                // These are added elsewhere, so we don't add them here
                                _logger.LogInformation($"Team {(isTeamA ? "A" : "B")} has unconfirmed map bans, showing confirm/revise buttons");
                            }
                            else if (!hasConfirmedBans)
                            {
                                // If team hasn't submitted bans at all, show the ban selection button
                                var selectMapsButton = new DiscordButtonComponent(
                                    DiscordButtonStyle.Primary,
                                    $"select_maps_{scrimmageId}",
                                    "Select Maps to Ban");

                                buttons.Add(selectMapsButton);
                                _logger.LogInformation($"Team {(isTeamA ? "A" : "B")} hasn't submitted map bans, showing selection button");
                            }
                            else
                            {
                                // Team has already confirmed bans, just show refresh button
                                // And show waiting message in instructions
                                _logger.LogInformation($"Team {(isTeamA ? "A" : "B")} has confirmed map bans, showing refresh button only");
                            }
                            break;

                        case MatchStage.DeckSubmission:
                            // For deck submission, only show submit button if this team hasn't submitted
                            bool hasSubmittedDeck = isTeamA
                                ? !string.IsNullOrEmpty(scrimmage.TeamA.DeckName)
                                : !string.IsNullOrEmpty(scrimmage.TeamB.DeckName);

                            if (!hasSubmittedDeck)
                            {
                                buttons.Add(ComponentHelpers.CreateSubmitDeckButton(scrimmageId));
                                _logger.LogInformation($"Team {(isTeamA ? "A" : "B")} hasn't submitted deck, showing submission button");
                            }
                            else
                            {
                                // Team already submitted, just show refresh button
                                _logger.LogInformation($"Team {(isTeamA ? "A" : "B")} already submitted deck, showing refresh button only");
                            }
                            break;

                        case MatchStage.GameResults:
                            // Show game winner buttons to both teams
                            buttons.AddRange(GameResultHelpers.CreateWinnerButtons(
                                scrimmage.TeamA.Captain.Username,
                                scrimmage.TeamB.Captain.Username,
                                scrimmageId));

                            // Add replay submission button
                            buttons.Add(ComponentHelpers.CreateReplaySubmissionButton(
                                scrimmage.CurrentGameNumber - 1,
                                scrimmageId));

                            _logger.LogInformation("Showing game winner and replay submission buttons");
                            break;
                    }
                    break;
            }

            return buttons;
        }

        /// <summary>
        /// Determines the current stage of a scrimmage based on its state
        /// </summary>
        private MatchStage DetermineCurrentStage(Scrimmage scrimmage)
        {
            return scrimmage.Status switch
            {
                ScrimmageStatus.Completed => MatchStage.Completed,
                ScrimmageStatus.Created => MatchStage.Created,
                ScrimmageStatus.Cancelled => MatchStage.Completed, // Treat cancelled as completed for UI
                ScrimmageStatus.InProgress when scrimmage.TeamAScore > 0 || scrimmage.TeamBScore > 0 => MatchStage.GameResults,
                ScrimmageStatus.InProgress when !string.IsNullOrEmpty(scrimmage.TeamA.DeckName) || !string.IsNullOrEmpty(scrimmage.TeamB.DeckName) => MatchStage.DeckSubmission,
                ScrimmageStatus.InProgress => MatchStage.MapBan,
                _ => MatchStage.Created
            };
        }

        /// <summary>
        /// Creates a map ban dropdown component for the specified scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to create the dropdown for</param>
        /// <returns>The map ban dropdown component</returns>
        public DiscordComponent CreateMapBanDropdownAsync(Scrimmage scrimmage)
        {
            // Get available maps based on the map pool
            List<string> availableMaps = GetScrimmageMapPool(scrimmage);

            // Filter out maps that have already been played
            availableMaps = availableMaps.Where(m => !scrimmage.Maps.Contains(m)).ToList();

            // Use the shared helper to create the map ban dropdown
            return MapBanHelpers.CreateMapBanDropdown(availableMaps, scrimmage.MatchLength, scrimmage.GetHashCode().ToString());
        }

        /// <summary>
        /// Gets the appropriate map pool for a scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to get the map pool for</param>
        /// <returns>A list of map names for the scrimmage</returns>
        private List<string> GetScrimmageMapPool(Scrimmage scrimmage)
        {
            bool isOneVOne = scrimmage.GameType == ScrimmageGameType.OneVOne;
            bool useTournamentMaps = scrimmage.UseTournamentMapPool;
            string mapSize = isOneVOne ? "1v1" : "2v2";

            // Default fallback maps if we can't find a proper map pool
            var fallbackMaps = new List<string> { "Default Map", "Fallback Map 1", "Fallback Map 2" };

            try
            {
                // Use MapService helper method to get maps by pool type and size
                var availableMaps = _mapService.GetMapsByPoolTypeAndSize(useTournamentMaps, mapSize);

                _logger.LogInformation($"Retrieved {(useTournamentMaps ? "tournament" : "casual")} map pool for {mapSize} scrimmage: {availableMaps.Count} maps");

                // Return the map pool, or fallback maps if empty
                return availableMaps.Count > 0 ? availableMaps : fallbackMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving map pool for scrimmage");
                return fallbackMaps;
            }
        }

        /// <summary>
        /// Gets a random map for the next game in a scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage to get a random map for</param>
        /// <returns>A random map name</returns>
        private string GetRandomMapForScrimmage(Scrimmage scrimmage)
        {
            try
            {
                // Get the base map pool
                var mapPool = GetScrimmageMapPool(scrimmage);

                // If there are no maps, return a default map
                if (mapPool.Count == 0)
                {
                    _logger.LogWarning("Map pool is empty, returning Default Map");
                    return "Default Map";
                }

                // Get banned maps - combine both teams' banned maps
                var bannedMaps = new HashSet<string>();
                bannedMaps.UnionWith(scrimmage.TeamAMapBans);
                bannedMaps.UnionWith(scrimmage.TeamBMapBans);

                // Get played maps
                var playedMaps = new HashSet<string>(scrimmage.Maps);

                // Filter out banned and played maps
                var availableMaps = mapPool
                    .Where(map => !bannedMaps.Contains(map) && !playedMaps.Contains(map))
                    .ToList();

                _logger.LogInformation($"Found {availableMaps.Count} available maps for next game (excluded {bannedMaps.Count} banned maps and {playedMaps.Count} played maps)");

                // If no maps are available after filtering, try using maps that aren't banned
                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available after filtering, using unbanned maps");
                    availableMaps = mapPool
                        .Where(map => !bannedMaps.Contains(map))
                        .ToList();

                    // If there are still no maps, use the full map pool
                    if (availableMaps.Count == 0)
                    {
                        _logger.LogWarning("No unbanned maps available, using full map pool");
                        availableMaps = mapPool;
                    }
                }

                // Select a random map using the injected random provider for consistency
                int index = _randomProvider.Instance.Next(availableMaps.Count);
                return availableMaps[index];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map for scrimmage");
                return "Default Map";
            }
        }

        /// <summary>
        /// Creates confirm and revise buttons for map ban selections
        /// </summary>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="teamNumber">Team number (1 for Team A, 2 for Team B)</param>
        /// <returns>List of buttons for map ban confirmation</returns>
        public List<DiscordComponent> CreateMapBanConfirmButtons(Scrimmage scrimmage, int teamNumber)
        {
            return MapBanHelpers.CreateMapBanConfirmButtons(teamNumber.ToString(), scrimmage.GetHashCode().ToString());
        }

        /// <summary>
        /// Updates a message with map ban confirmation buttons
        /// </summary>
        /// <param name="message">The message to update</param>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="teamNumber">Team number (1 for Team A, 2 for Team B)</param>
        /// <returns>The updated message</returns>
        public async Task<DiscordMessage> UpdateWithMapBanConfirmButtonsAsync(
            DiscordMessage message,
            Scrimmage scrimmage,
            int teamNumber)
        {
            // Create the embed based on current scrimmage state
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"{GetGameTypeString(scrimmage.GameType)} Scrimmage: {GetMatchLengthString(scrimmage.MatchLength)}")
                .WithColor(GetStatusColor(scrimmage.Status))
                .WithDescription(GetScrimmageProgressBar(scrimmage));

            // Add map ban information
            AddMapBansField(embed, scrimmage);

            // Add instruction for confirming map bans
            embed.AddField("Confirm Your Map Bans",
                "Please review your map ban selections above and confirm or revise them.",
                false);

            // Create confirm/revise buttons
            var buttons = CreateMapBanConfirmButtons(scrimmage, teamNumber);

            // Update the message
            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(embed.Build())
                .AddComponents(buttons);

            return await message.ModifyAsync(messageBuilder);
        }

        /// <summary>
        /// Records a map ban selection for a team
        /// </summary>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="teamNumber">Team number (1 for Team A, 2 for Team B)</param>
        /// <param name="bannedMaps">List of maps to ban in priority order</param>
        /// <returns>True if the map ban was recorded successfully</returns>
        public async Task<bool> RecordMapBanAsync(Scrimmage scrimmage, int teamNumber, List<string> bannedMaps)
        {
            if (teamNumber != 1 && teamNumber != 2)
                throw new ArgumentException("Team number must be 1 or 2", nameof(teamNumber));

            if (bannedMaps == null || !bannedMaps.Any())
                throw new ArgumentException("Must provide at least one map to ban", nameof(bannedMaps));

            // Store the unconfirmed bans based on team number
            if (teamNumber == 1)
            {
                scrimmage.TeamAUnconfirmedMapBans.Clear();
                scrimmage.TeamAUnconfirmedMapBans.AddRange(bannedMaps);
            }
            else
            {
                scrimmage.TeamBUnconfirmedMapBans.Clear();
                scrimmage.TeamBUnconfirmedMapBans.AddRange(bannedMaps);
            }

            // Update the status message
            await UpdateScrimmageStatusAsync(scrimmage);

            // After updating the status message with the unconfirmed bans,
            // show confirmation buttons (confirm/revise)
            if (scrimmage.StatusMessage is not null)
            {
                await UpdateWithMapBanConfirmButtonsAsync(
                    scrimmage.StatusMessage,
                    scrimmage,
                    teamNumber);
            }

            return true;
        }

        /// <summary>
        /// Confirms map bans for a team
        /// </summary>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="teamNumber">Team number (1 for Team A, 2 for Team B)</param>
        /// <returns>True if the map bans were confirmed successfully</returns>
        public async Task<bool> ConfirmMapBansAsync(Scrimmage scrimmage, int teamNumber)
        {
            if (teamNumber != 1 && teamNumber != 2)
                throw new ArgumentException("Team number must be 1 or 2", nameof(teamNumber));

            // Get the unconfirmed bans for the specified team
            List<string> unconfirmedBans = teamNumber == 1
                ? scrimmage.TeamAUnconfirmedMapBans
                : scrimmage.TeamBUnconfirmedMapBans;

            if (unconfirmedBans == null || !unconfirmedBans.Any())
                throw new InvalidOperationException($"No unconfirmed map bans for Team {(teamNumber == 1 ? "A" : "B")}");

            // Transfer unconfirmed bans to confirmed bans
            if (teamNumber == 1)
            {
                scrimmage.TeamAMapBans.Clear();
                scrimmage.TeamAMapBans.AddRange(unconfirmedBans);
                scrimmage.TeamAUnconfirmedMapBans.Clear();
            }
            else
            {
                scrimmage.TeamBMapBans.Clear();
                scrimmage.TeamBMapBans.AddRange(unconfirmedBans);
                scrimmage.TeamBUnconfirmedMapBans.Clear();
            }

            // Check if both teams have confirmed their map bans
            if (scrimmage.TeamAMapBans.Any() && scrimmage.TeamBMapBans.Any())
            {
                // If both teams have confirmed, move to deck submission stage
                // and set status to InProgress if it's not already
                if (scrimmage.Status != ScrimmageStatus.InProgress)
                {
                    scrimmage.Status = ScrimmageStatus.InProgress;
                }
            }

            // Update the status message with the confirmed bans
            await UpdateScrimmageStatusAsync(scrimmage);

            return true;
        }

        /// <summary>
        /// Submits a deck code for a player in the scrimmage
        /// </summary>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="player">The player</param>
        /// <param name="deckCode">The deck code to submit</param>
        /// <returns>True if the deck was submitted successfully</returns>
        public async Task<bool> SubmitDeckCodeAsync(Scrimmage scrimmage, DiscordUser player, string deckCode)
        {
            // Find which team the player is on
            bool isTeamA = scrimmage.TeamA.Captain.Id == player.Id ||
                           scrimmage.TeamA.Members.Any(m => m.Id == player.Id);

            bool isTeamB = scrimmage.TeamB.Captain.Id == player.Id ||
                           scrimmage.TeamB.Members.Any(m => m.Id == player.Id);

            if (!isTeamA && !isTeamB)
                throw new ArgumentException("Player is not part of this scrimmage", nameof(player));

            // Store the deck code for the appropriate team/player
            var team = isTeamA ? scrimmage.TeamA : scrimmage.TeamB;
            team.SetDeckCode(player, deckCode);

            // If both captains have submitted deck codes, advance to game results stage
            if (!string.IsNullOrEmpty(scrimmage.TeamA.DeckName) && !string.IsNullOrEmpty(scrimmage.TeamB.DeckName))
            {
                // Make sure status is InProgress
                if (scrimmage.Status != ScrimmageStatus.InProgress)
                {
                    scrimmage.Status = ScrimmageStatus.InProgress;
                }
            }

            // Update the status message with the deck information
            await UpdateScrimmageStatusAsync(scrimmage);

            return true;
        }

        /// <summary>
        /// Creates a game winner dropdown for reporting game results
        /// </summary>
        /// <param name="scrimmage">The scrimmage to create the dropdown for</param>
        /// <param name="gameNumber">The current game number (0-based)</param>
        /// <returns>The game winner dropdown message</returns>
        public async Task<DiscordMessage> CreateGameWinnerDropdownAsync(Scrimmage scrimmage, int gameNumber)
        {
            // Use the helper to create the dropdown component
            var dropdown = GameResultHelpers.CreateGameWinnerDropdown(
                scrimmage.TeamA.Captain.Username,
                scrimmage.TeamB.Captain.Username,
                gameNumber,
                scrimmage.GetHashCode().ToString());

            // Create a message with the dropdown
            var message = await scrimmage.Thread.SendMessageAsync(
                new DiscordMessageBuilder()
                    .WithContent($"🎮 **Game {gameNumber + 1}:** Please select the winner")
                    .AddComponents(dropdown));

            return message;
        }

        /// <summary>
        /// Creates a button for submitting a replay file
        /// </summary>
        /// <param name="scrimmage">The scrimmage</param>
        /// <param name="gameNumber">The game number (0-based)</param>
        /// <returns>The replay submission button</returns>
        public DiscordButtonComponent CreateReplaySubmissionButton(Scrimmage scrimmage, int gameNumber)
        {
            return ComponentHelpers.CreateReplaySubmissionButton(gameNumber, scrimmage.GetHashCode().ToString());
        }

        /// <inheritdoc/>
        public async Task<bool> CompleteScrimmageAsync(Scrimmage scrimmage)
        {
            scrimmage.Status = ScrimmageStatus.Completed;
            scrimmage.CompletedAt = DateTimeOffset.UtcNow;

            await UpdateScrimmageStatusAsync(scrimmage);

            // Send completion message
            var message = await scrimmage.Thread.SendMessageAsync(
                "This scrimmage has been marked as completed. Thanks for playing!");
            scrimmage.Messages.Add(message);

            _logger.LogInformation("Scrimmage between {Player1} and {Player2} completed",
                scrimmage.TeamA.Captain.Username, scrimmage.TeamB.Captain.Username);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> CancelScrimmageAsync(Scrimmage scrimmage)
        {
            // Set status to Cancelled but treat it as Completed for UI purposes
            scrimmage.Status = ScrimmageStatus.Cancelled;
            scrimmage.CompletedAt = DateTimeOffset.UtcNow;

            await UpdateScrimmageStatusAsync(scrimmage);

            // Send cancellation message
            var message = await scrimmage.Thread.SendMessageAsync(
                "This scrimmage has been cancelled.");
            scrimmage.Messages.Add(message);

            _logger.LogInformation("Scrimmage between {Player1} and {Player2} cancelled",
                scrimmage.TeamA.Captain.Username, scrimmage.TeamB.Captain.Username);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RecordGameResultAsync(Scrimmage scrimmage, int winningPlayer)
        {
            // Validate arguments
            if (winningPlayer != 1 && winningPlayer != 2)
            {
                throw new ArgumentException("Winning player must be 1 or 2");
            }

            // Add the result to the Results list
            scrimmage.Results.Add(winningPlayer);

            // Update scores
            if (winningPlayer == 1)
            {
                scrimmage.TeamAScore++;
            }
            else
            {
                scrimmage.TeamBScore++;
            }

            // Check if the match is completed
            if (GameResultHelpers.IsMatchComplete(scrimmage.TeamAScore, scrimmage.TeamBScore, scrimmage.MatchLength))
            {
                scrimmage.Status = ScrimmageStatus.Completed;
                string winnerName = GameResultHelpers.GetMatchWinnerName(
                    scrimmage.TeamAScore,
                    scrimmage.TeamBScore,
                    scrimmage.TeamA.Captain.Username,
                    scrimmage.TeamB.Captain.Username,
                    scrimmage.MatchLength) ?? "Unknown";

                _logger.LogInformation($"Match completed: {winnerName} wins");
            }
            else
            {
                // If the match wasn't already in progress, mark it as such
                if (scrimmage.Status != ScrimmageStatus.InProgress)
                {
                    scrimmage.Status = ScrimmageStatus.InProgress;
                }
            }

            // Update status message
            await UpdateScrimmageStatusAsync(scrimmage);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> AdvanceToNextGameAsync(Scrimmage scrimmage)
        {
            // Generate a new map using the map pool
            string mapName = GetRandomMapForScrimmage(scrimmage);

            // Add the new map to the list
            scrimmage.Maps.Add(mapName);

            // Increment current game number
            scrimmage.CurrentGameNumber++;

            // Update status message
            await UpdateScrimmageStatusAsync(scrimmage);

            // Create a game winner dropdown for the new game
            await CreateGameWinnerDropdownAsync(scrimmage, scrimmage.CurrentGameNumber - 1);

            // Log
            _logger.LogInformation($"Advanced to next game between {scrimmage.TeamA.Captain.Username} and {scrimmage.TeamB.Captain.Username} with map {mapName}");

            return true;
        }

        /// <inheritdoc/>
        public Task<Scrimmage?> GetScrimmageByThreadIdAsync(ulong threadId)
        {
            var scrimmage = _ongoingRounds.GetScrimmageByThreadIdOrDefault(threadId);
            return Task.FromResult(scrimmage);
        }

        /// <summary>
        /// Helper method to get a color based on scrimmage status
        /// </summary>
        private DiscordColor GetStatusColor(ScrimmageStatus status)
        {
            // Map the scrimmage status to a match stage first
            MatchStage stage = status switch
            {
                ScrimmageStatus.Completed => MatchStage.Completed,
                ScrimmageStatus.Created => MatchStage.Created,
                ScrimmageStatus.InProgress => MatchStage.MapBan, // Default to MapBan for in-progress
                _ => MatchStage.Created
            };

            // Then use the shared helper to get the standardized color
            return StatusEmbedHelpers.GetStageColor(stage);
        }

        /// <summary>
        /// Helper method to convert GameType to a readable string
        /// </summary>
        private string GetGameTypeString(ScrimmageGameType gameType) => gameType switch
        {
            ScrimmageGameType.OneVOne => "1v1",
            ScrimmageGameType.TwoVTwo => "2v2",
            ScrimmageGameType.ThreeVThree => "3v3",
            ScrimmageGameType.FourVFour => "4v4",
            _ => "Unknown"
        };

        /// <summary>
        /// Helper method to convert MatchLength to a readable string
        /// </summary>
        private string GetMatchLengthString(MatchLength matchLength) => StatusEmbedHelpers.GetMatchLengthString(matchLength);

        /// <summary>
        /// Helper method to get the number of games needed to win a match
        /// </summary>
        private int GetGamesToWin(MatchLength matchLength) => StatusEmbedHelpers.GetGamesToWin(matchLength);

        /// <summary>
        /// Check if a user has admin privileges for scrimmage management
        /// </summary>
        public async Task<bool> HasScrimmageAdminPrivilegesAsync(ulong userId)
        {
            try
            {
                // Get the guild ID from the first server in config
                var guildId = ConfigManager.Config?.Servers?.FirstOrDefault()?.ServerId ?? 0;
                if (guildId == 0)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - no guild ID configured");
                    return false;
                }

                var guild = await _client.GetGuildAsync(guildId);
                if (guild is null)
                {
                    _logger.LogWarning($"Cannot check Discord permissions for user {userId} - guild not found");
                    return false;
                }

                DiscordMember? member;
                try
                {
                    member = await guild.GetMemberAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Cannot check Discord permissions for user {userId} - member not found");
                    return false;
                }

                // Check if user is the server owner or has Administrator or moderator permissions
                return member.IsOwner ||
                       member.Permissions.HasFlag(DiscordPermission.Administrator) ||
                       member.Permissions.HasFlag(DiscordPermission.ManageGuild) ||
                       member.Permissions.HasFlag(DiscordPermission.ModerateMembers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking scrimmage admin privileges for user {userId}");
                return false;
            }
        }

        /// <summary>
        /// Check if a user can manage a specific scrimmage
        /// </summary>
        public async Task<bool> CanManageScrimmageAsync(Scrimmage scrimmage, ulong userId)
        {
            // Check if user has admin privileges
            if (await HasScrimmageAdminPrivilegesAsync(userId))
                return true;

            // Check if the user is a participant in the scrimmage
            return scrimmage.TeamA.Captain.Id == userId || scrimmage.TeamB.Captain.Id == userId;
        }
    }
}