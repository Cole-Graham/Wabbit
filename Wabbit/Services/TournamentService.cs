using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Implementation of ITournamentService for general tournament operations
    /// </summary>
    public class TournamentService : ITournamentService
    {
        private readonly ILogger<TournamentService> _logger;
        private readonly ITournamentManagerService _tournamentManagerService;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentPlayoffService _playoffService;

        /// <summary>
        /// Constructor
        /// </summary>
        public TournamentService(
            ILogger<TournamentService> logger,
            ITournamentManagerService tournamentManagerService,
            ITournamentGroupService groupService,
            ITournamentStateService stateService,
            ITournamentPlayoffService playoffService)
        {
            _logger = logger;
            _tournamentManagerService = tournamentManagerService;
            _groupService = groupService;
            _stateService = stateService;
            _playoffService = playoffService;
        }

        /// <summary>
        /// Creates a new tournament
        /// </summary>
        public async Task<Tournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            TournamentFormat format,
            DiscordChannel announcementChannel,
            GameType gameType = GameType.OneVsOne,
            Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            _logger.LogInformation($"Creating tournament {name} with {players.Count} players");

            return await _tournamentManagerService.CreateTournamentAsync(
                name,
                players,
                format,
                announcementChannel,
                gameType,
                playerSeeds);
        }

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        public Tournament? GetTournament(string name)
        {
            return _tournamentManagerService.GetTournament(name);
        }

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        public List<Tournament> GetAllTournaments()
        {
            return _tournamentManagerService.GetAllTournaments();
        }

        /// <summary>
        /// Deletes a tournament
        /// </summary>
        public async Task DeleteTournamentAsync(string name, DiscordClient? client = null)
        {
            _logger.LogInformation($"Deleting tournament {name}");
            await _tournamentManagerService.DeleteTournamentAsync(name, client);
        }

        /// <summary>
        /// Posts tournament visualization to the announcement channel
        /// </summary>
        public async Task PostTournamentVisualizationAsync(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Posting visualization for tournament {tournament.Name}");
            await _tournamentManagerService.PostTournamentVisualizationAsync(tournament, client);
        }

        /// <summary>
        /// Starts a tournament
        /// </summary>
        public async Task StartTournamentAsync(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Starting tournament {tournament.Name}");

            // Create groups using the group service
            if (tournament.Format == TournamentFormat.GroupStageWithPlayoffs ||
                tournament.Format == TournamentFormat.RoundRobin)
            {
                // Get players from all groups
                List<DiscordMember> players = new List<DiscordMember>();

                // Extract players from all group participants
                foreach (var group in tournament.Groups)
                {
                    foreach (var participant in group.Participants)
                    {
                        if (participant.Player is DiscordMember member)
                        {
                            players.Add(member);
                        }
                    }
                }

                // Determine group count
                int groupCount = _groupService.DetermineGroupCount(players.Count, tournament.Format);

                // Create seeding dictionary from participant seed values
                Dictionary<DiscordMember, int>? playerSeeds = null;
                if (players.Count > 0)
                {
                    playerSeeds = new Dictionary<DiscordMember, int>();
                    foreach (var group in tournament.Groups)
                    {
                        foreach (var participant in group.Participants)
                        {
                            if (participant.Player is DiscordMember member && participant.Seed > 0)
                            {
                                playerSeeds[member] = participant.Seed;
                            }
                        }
                    }
                }

                // Create groups - now using correct parameter count
                _groupService.CreateGroups(tournament, players, playerSeeds);
            }

            // Current stage to playoffs
            tournament.CurrentStage = TournamentStage.Playoffs;

            // Save tournament state
            await _stateService.SaveTournamentStateAsync(client);

            // Generate tournament visualization
            await PostTournamentVisualizationAsync(tournament, client);

            // Additional tournament startup steps are now handled by individual services
            // Future enhancements: Add tournament visualization and match scheduling implementation
        }

        /// <summary>
        /// Archives threads for a completed match
        /// </summary>
        public async Task ArchiveThreadsAsync(Tournament.Match match, DiscordClient client, TimeSpan? archiveDuration = null)
        {
            if (match is null || match.LinkedRound is null)
            {
                _logger.LogWarning("Cannot archive threads: match or linked round is null");
                return;
            }

            _logger.LogInformation($"Archiving threads for match: {match.Name}");

            // Get the archival duration from config or use the provided one
            TimeSpan actualDuration;
            if (archiveDuration.HasValue)
            {
                actualDuration = archiveDuration.Value;
            }
            else
            {
                // Use the config setting or default to 24 hours
                int archivalHours = BotClient.Config.ConfigManager.Config?.Tournament?.ThreadArchivalHours ?? 24;
                actualDuration = TimeSpan.FromHours(archivalHours);
            }

            // Get the round from the match
            var round = match.LinkedRound;

            // For each team in the round, archive their thread
            if (round.Teams is not null)
            {
                foreach (var team in round.Teams)
                {
                    try
                    {
                        if (team.Thread is not null)
                        {
                            // Update the auto-archive duration and add a notification message
                            await SetThreadArchivalDuration(team.Thread, actualDuration);

                            // Format the time in a human-readable way
                            string durationText = FormatDurationForDisplay(actualDuration);

                            // Send a notification message
                            await team.Thread.SendMessageAsync(new DiscordMessageBuilder()
                                .WithContent($"📂 **Thread Archival Notice** 📂\n" +
                                            $"This thread will be automatically archived after {durationText} of inactivity. " +
                                            $"All match history will remain accessible.")
                                .WithAllowedMentions([])); // No mentions
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error archiving thread for team '{team.Name}'");
                    }
                }
            }
        }

        /// <summary>
        /// Archives all threads for a completed tournament
        /// </summary>
        public async Task ArchiveAllTournamentThreadsAsync(Tournament tournament, DiscordClient client, TimeSpan? archiveDuration = null)
        {
            if (tournament is null)
            {
                _logger.LogWarning("Cannot archive tournament threads: tournament is null");
                return;
            }

            _logger.LogInformation($"Archiving all threads for tournament: {tournament.Name}");

            // Archive all group stage matches
            foreach (var group in tournament.Groups)
            {
                foreach (var match in group.Matches)
                {
                    if (match.IsComplete)
                    {
                        await ArchiveThreadsAsync(match, client, archiveDuration);
                    }
                }
            }

            // Archive all playoff matches
            foreach (var match in tournament.PlayoffMatches)
            {
                if (match.IsComplete)
                {
                    await ArchiveThreadsAsync(match, client, archiveDuration);
                }
            }
        }

        /// <summary>
        /// Sets the auto-archive duration for a Discord thread
        /// </summary>
        private async Task SetThreadArchivalDuration(DiscordThreadChannel thread, TimeSpan duration)
        {
            try
            {
                // Convert TimeSpan to DiscordAutoArchiveDuration
                DiscordAutoArchiveDuration discordDuration = ConvertToDiscordArchiveDuration(duration);

                // Modify the thread auto-archive duration
                await thread.ModifyAsync(x => x.AutoArchiveDuration = discordDuration);

                _logger.LogInformation($"Set auto-archive duration for thread '{thread.Name}' to {discordDuration}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting archive duration for thread '{thread.Name}'");
            }
        }

        /// <summary>
        /// Converts a TimeSpan to the closest DiscordAutoArchiveDuration
        /// </summary>
        private DiscordAutoArchiveDuration ConvertToDiscordArchiveDuration(TimeSpan timeSpan)
        {
            // Convert to total hours
            int hours = (int)timeSpan.TotalHours;

            // Discord only supports specific durations: 1 hour, 24 hours, 3 days (72 hours), 7 days (168 hours)
            return hours switch
            {
                < 24 => DiscordAutoArchiveDuration.Hour,
                < 72 => DiscordAutoArchiveDuration.Day,
                < 168 => DiscordAutoArchiveDuration.ThreeDays,
                _ => DiscordAutoArchiveDuration.Week
            };
        }

        /// <summary>
        /// Formats a TimeSpan into a human-readable string for display
        /// </summary>
        private string FormatDurationForDisplay(TimeSpan timeSpan)
        {
            // Format the duration based on its length
            return timeSpan.TotalHours switch
            {
                < 1 => $"{timeSpan.Minutes} minutes",
                < 24 => $"{timeSpan.Hours} hours",
                < 48 => "1 day",
                _ => $"{timeSpan.Days} days"
            };
        }

        /// <summary>
        /// Updates the tournament display in Discord
        /// </summary>
        /// <param name="tournament">The tournament to update</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public async Task UpdateTournamentDisplayAsync(Tournament tournament)
        {
            if (tournament == null || tournament.AnnouncementChannel is null)
            {
                _logger.LogWarning("Cannot update tournament display: tournament or announcement channel is null");
                return;
            }

            try
            {
                // Generate tournament status content
                string content = GenerateTournamentStatusContent(tournament);

                // Create the message builder
                var builder = new DiscordMessageBuilder()
                    .WithContent(content);

                // Add admin controls if appropriate
                AddAdminControls(tournament, builder);

                // Find the existing status message if any
                var statusMessageId = GetTournamentStatusMessageId(tournament);
                if (statusMessageId != 0)
                {
                    try
                    {
                        // Try to edit the existing message
                        var message = await tournament.AnnouncementChannel.GetMessageAsync(statusMessageId);
                        await message.ModifyAsync(builder);
                        _logger.LogInformation("Updated tournament display for {TournamentName}", tournament.Name);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not update existing tournament message, will create a new one");
                        // Continue to create a new message
                    }
                }

                // Create a new status message
                var newMessage = await tournament.AnnouncementChannel.SendMessageAsync(builder);
                SaveTournamentStatusMessageId(tournament, newMessage.Id);
                _logger.LogInformation("Created new tournament display for {TournamentName}", tournament.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tournament display for {TournamentName}", tournament?.Name ?? "unknown");
            }
        }

        /// <summary>
        /// Adds admin controls to tournament display if appropriate
        /// </summary>
        /// <param name="tournament">The tournament to add controls for</param>
        /// <param name="builder">The message builder to add components to</param>
        private void AddAdminControls(Tournament tournament, DiscordMessageBuilder builder)
        {
            try
            {
                // Don't add controls if tournament is complete
                if (tournament.IsComplete)
                {
                    return;
                }

                var adminComponents = new List<DiscordComponent>();

                // Check if we should offer third place match creation
                // This button only shows if:
                // 1. The tournament is in playoff stage
                // 2. Semifinals are completed
                // 3. No third place match exists yet
                if (tournament.CurrentStage == TournamentStage.Playoffs &&
                    _playoffService.AreSemifinalsCompleted(tournament) &&
                    _playoffService.CanCreateThirdPlaceMatch(tournament))
                {
                    adminComponents.Add(new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"admin_create_third_place_{tournament.Name}",
                        "Create Third Place Match (Admin)",
                        emoji: new DiscordComponentEmoji("🏅")));
                }

                // Add more admin controls here as needed

                // Only add the row if we have any admin components
                if (adminComponents.Any())
                {
                    builder.AddComponents(adminComponents);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding admin controls to tournament {TournamentName}", tournament?.Name ?? "unknown");
                // Don't throw, as this is an optional feature
            }
        }

        /// <summary>
        /// Generates tournament status content
        /// </summary>
        private string GenerateTournamentStatusContent(Tournament tournament)
        {
            // This is a placeholder - in a real implementation, you would generate
            // a detailed status message based on the tournament state
            return $"**{tournament.Name}** - {tournament.CurrentStage} Stage\n" +
                   $"Format: {tournament.Format}\n" +
                   $"Status: {(tournament.IsComplete ? "Complete" : "In Progress")}";
        }

        /// <summary>
        /// Gets the tournament status message ID
        /// </summary>
        private ulong GetTournamentStatusMessageId(Tournament tournament)
        {
            // In a real implementation, you would retrieve this from tournament data
            // For now, we'll use a placeholder approach
            if (tournament.CustomProperties != null &&
                tournament.CustomProperties.TryGetValue("StatusMessageId", out var messageIdObj) &&
                messageIdObj is ulong messageId)
            {
                return messageId;
            }
            return 0;
        }

        /// <summary>
        /// Saves the tournament status message ID
        /// </summary>
        private void SaveTournamentStatusMessageId(Tournament tournament, ulong messageId)
        {
            // In a real implementation, you would save this to tournament data
            // For now, we'll use a placeholder approach
            if (tournament.CustomProperties == null)
            {
                tournament.CustomProperties = new Dictionary<string, object>();
            }
            tournament.CustomProperties["StatusMessageId"] = messageId;
        }
    }
}