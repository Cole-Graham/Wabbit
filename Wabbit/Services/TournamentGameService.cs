using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for handling tournament game logic, separate from command handling
    /// </summary>
    public class TournamentGameService : ITournamentGameService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentRepositoryService _repositoryService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentMapService _mapService;
        private readonly ILogger<TournamentGameService> _logger;
        private readonly IMatchStatusService _matchStatusService;
        private readonly IServiceScopeFactory _scopeFactory;

        // Track matches being processed to prevent concurrent updates
        private readonly HashSet<string> _processingMatches = new HashSet<string>();

        public TournamentGameService(
            OngoingRounds ongoingRounds,
            ITournamentRepositoryService repositoryService,
            ITournamentStateService stateService,
            ITournamentPlayoffService playoffService,
            ITournamentMapService mapService,
            ILogger<TournamentGameService> logger,
            IMatchStatusService matchStatusService,
            IServiceScopeFactory scopeFactory)
        {
            _ongoingRounds = ongoingRounds;
            _repositoryService = repositoryService;
            _stateService = stateService;
            _playoffService = playoffService;
            _mapService = mapService;
            _logger = logger;
            _matchStatusService = matchStatusService;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Handles game result selection and advances the match series
        /// </summary>
        public async Task HandleGameResultAsync(Round round, DiscordChannel thread, string winnerId, DiscordClient client)
        {
            try
            {
                // Update the match status with the game result
                await _matchStatusService.UpdateMatchStatusAsync(thread, round, client);

                // If the match is not complete, select the next map
                if (!round.IsCompleted)
                {
                    await _matchStatusService.UpdateMapInformationAsync(thread, round, client);
                }
                else
                {
                    // Match is complete, finalize it
                    await _matchStatusService.FinalizeMatchAsync(thread, round, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling game result");
                await thread.SendMessageAsync("An error occurred while processing the game result. Please contact an administrator.");
            }
        }

        /// <summary>
        /// Handles match completion, including scheduling new matches or advancing tournaments
        /// </summary>
        public async Task HandleMatchCompletion(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            try
            {
                string matchId = match.Name ?? Guid.NewGuid().ToString();

                // Prevent concurrent processing of the same match
                lock (_processingMatches)
                {
                    if (_processingMatches.Contains(matchId))
                    {
                        _logger.LogWarning($"Match {matchId} is already being processed. Skipping.");
                        return;
                    }
                    _processingMatches.Add(matchId);
                }

                try
                {
                    _logger.LogInformation($"Handling completion of match {match.Name} in tournament {tournament.Name}");

                    // Update next match if defined
                    if (match.NextMatch is not null && match.Result?.Winner is not null)
                    {
                        // Find the next match and add the winner to it
                        var nextMatch = match.NextMatch;

                        // Create a new participant for the next match
                        var advancingParticipant = new Tournament.MatchParticipant
                        {
                            Player = match.Result.Winner
                        };

                        // Add the advancing participant if they're not already there
                        if (!nextMatch.Participants.Any(p => p.Player == match.Result.Winner))
                        {
                            nextMatch.Participants.Add(advancingParticipant);
                        }
                    }

                    // Save tournament state
                    await _stateService.SaveTournamentStateAsync(client);

                    // Check if tournament is complete
                    bool allPlayoffMatchesComplete = true;
                    if (tournament.PlayoffMatches != null && tournament.PlayoffMatches.Any())
                    {
                        allPlayoffMatchesComplete = tournament.PlayoffMatches.All(m => m.IsComplete);
                    }

                    if (allPlayoffMatchesComplete && tournament.CurrentStage == TournamentStage.Playoffs)
                    {
                        // Find the final match to determine the winner
                        var finalMatch = tournament.PlayoffMatches?
                            .OrderByDescending(m => m.DisplayPosition)
                            .FirstOrDefault();

                        if (finalMatch?.Result?.Winner != null)
                        {
                            tournament.IsComplete = true;
                            // Set the tournament stage to Complete
                            tournament.CurrentStage = TournamentStage.Complete;

                            _logger.LogInformation($"Tournament {tournament.Name} completed with winner: {finalMatch.Result.Winner}");

                            // Get winner display name
                            string winnerName = "Unknown";
                            if (finalMatch.Result.Winner is DiscordMember member)
                            {
                                winnerName = member.DisplayName ?? member.Username;
                            }
                            else if (finalMatch.Result.Winner is DiscordUser user)
                            {
                                winnerName = user.Username;
                            }

                            // Announce the champion in the announcement channel if available
                            if (tournament.AnnouncementChannel is not null)
                            {
                                try
                                {
                                    var winnerEmbed = new DiscordEmbedBuilder()
                                        .WithTitle($"🏆 Tournament Champion: {tournament.Name}")
                                        .WithDescription($"**{winnerName}** has won the tournament!")
                                        .WithColor(DiscordColor.Gold)
                                        .WithTimestamp(DateTime.Now);

                                    await tournament.AnnouncementChannel.SendMessageAsync(winnerEmbed);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error announcing tournament winner: {ex.Message}");
                                }
                            }

                            // Generate final standings visualization
                            try
                            {
                                await Wabbit.Misc.TournamentVisualization.GenerateStandingsImage(tournament, client, _stateService);
                                _logger.LogInformation($"Generated final standings visualization for tournament {tournament.Name}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error generating final standings visualization: {ex.Message}");
                            }

                            // Archive all tournament threads
                            try
                            {
                                if (BotClient.Config.ConfigManager.Config?.Tournament?.AutoArchiveThreads == true)
                                {
                                    using (var scope = _scopeFactory.CreateScope())
                                    {
                                        var tournamentService = scope.ServiceProvider.GetService<ITournamentService>();
                                        if (tournamentService is not null)
                                        {
                                            await tournamentService.ArchiveAllTournamentThreadsAsync(tournament, client);
                                            _logger.LogInformation($"Archived all threads for completed tournament {tournament.Name}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error archiving tournament threads");
                            }
                        }
                    }

                    // Save tournament data
                    await _repositoryService.SaveTournamentsAsync();

                    // If tournament is still ongoing, check for playoff start
                    if (!tournament.IsComplete)
                    {
                        // If all groups are complete, set up playoffs (if not already in playoffs)
                        bool allGroupsComplete = tournament.Groups.All(g => g.IsComplete);
                        if (allGroupsComplete && tournament.CurrentStage == TournamentStage.Groups)
                        {
                            _playoffService.SetupPlayoffs(tournament);
                            tournament.CurrentStage = TournamentStage.Playoffs;

                            _logger.LogInformation($"Setting up playoffs for tournament {tournament.Name}");

                            // Save changes after playoff setup
                            await _repositoryService.SaveTournamentsAsync();
                        }
                    }
                }
                finally
                {
                    // Release the lock on this match
                    lock (_processingMatches)
                    {
                        _processingMatches.Remove(matchId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling match completion: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets available maps for the next game in a match
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>List of available map names</returns>
        public List<string> GetAvailableMapsForNextGame(Round round)
        {
            if (round == null)
            {
                _logger.LogWarning("Cannot get available maps: round is null");
                return new List<string>();
            }

            try
            {
                _logger.LogInformation($"Getting available maps for next game in {round.Name}");

                // Get the initial map pool based on game type (1v1 or team)
                var initialMapPool = _mapService.GetTournamentMapPool(round.OneVOne);

                // Get all banned maps from both teams
                var bannedMaps = new HashSet<string>();

                // Add maps from global bans stored in CustomProperties
                if (round.CustomProperties.ContainsKey("BannedMaps") && round.CustomProperties["BannedMaps"] is List<string> globalBannedMaps)
                {
                    foreach (var map in globalBannedMaps)
                    {
                        bannedMaps.Add(map);
                    }
                }

                // Add team-specific bans
                if (round.Teams != null)
                {
                    foreach (var team in round.Teams)
                    {
                        if (team?.MapBans != null)
                        {
                            foreach (var map in team.MapBans)
                            {
                                bannedMaps.Add(map);
                            }
                        }
                    }
                }

                // Filter out banned maps
                var availableMaps = initialMapPool.Where(map => !bannedMaps.Contains(map)).ToList();

                // For games after the first, also remove already played maps
                if (round.Maps != null && round.Maps.Count > 0)
                {
                    availableMaps = availableMaps.Where(map => !round.Maps.Contains(map)).ToList();

                    _logger.LogInformation($"Filtered out {round.Maps.Count} already played maps");
                }

                _logger.LogInformation($"Found {availableMaps.Count} available maps for next game");

                // If no maps available (all banned or played), return the initial pool with a warning
                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available after filtering. Using initial map pool.");
                    return initialMapPool;
                }

                return availableMaps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available maps for next game");
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets a random map for the next game, considering banned and played maps
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>A random map name, or null if no maps are available</returns>
        public string? GetRandomMapForNextGame(Round round)
        {
            if (round == null)
            {
                _logger.LogWarning("Cannot get random map: round is null");
                return null;
            }

            try
            {
                var availableMaps = GetAvailableMapsForNextGame(round);

                if (availableMaps.Count == 0)
                {
                    _logger.LogWarning("No maps available for random selection");
                    return null;
                }

                // Select a random map
                var random = new Random();
                int index = random.Next(availableMaps.Count);
                string selectedMap = availableMaps[index];

                _logger.LogInformation($"Randomly selected map: {selectedMap}");
                return selectedMap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map for next game");
                return null;
            }
        }
    }
}