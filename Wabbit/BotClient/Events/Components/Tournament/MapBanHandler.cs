using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles map ban-related component interactions
    /// </summary>
    public class MapBanHandler : ComponentHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly ITournamentMapService _mapService;
        private readonly ITournamentMatchService _matchService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITournamentManagerService _tournamentManager;
        private readonly IMatchStatusService _matchStatusService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="mapService">Service for accessing map data</param>
        /// <param name="matchService">Service for accessing match data</param>
        /// <param name="scopeFactory">Factory for creating service scopes</param>
        /// <param name="tournamentManager">Service for managing tournaments</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        public MapBanHandler(
            ILogger<MapBanHandler> logger,
            ITournamentStateService stateService,
            OngoingRounds roundsHolder,
            ITournamentMapService mapService,
            ITournamentMatchService matchService,
            IServiceScopeFactory scopeFactory,
            ITournamentManagerService tournamentManager,
            IMatchStatusService matchStatusService)
            : base(logger, stateService)
        {
            _roundsHolder = roundsHolder;
            _mapService = mapService;
            _matchService = matchService;
            _scopeFactory = scopeFactory;
            _tournamentManager = tournamentManager;
            _matchStatusService = matchStatusService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId == "map_ban_dropdown" ||
                   customId.StartsWith("confirm_map_bans_") ||
                   customId.StartsWith("revise_map_bans_") ||
                   customId.StartsWith("ban_map_") ||
                   customId.StartsWith("pick_map_");
        }

        /// <summary>
        /// Handles map ban-related component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                switch (e.Id)
                {
                    case "map_ban_dropdown":
                        await HandleMapBanDropdownAsync(client, e, hasBeenDeferred);
                        break;
                    case string s when s.StartsWith("confirm_map_bans_"):
                        await HandleConfirmMapBansAsync(client, e, hasBeenDeferred);
                        break;
                    case string s when s.StartsWith("revise_map_bans_"):
                        await HandleReviseMapBansAsync(client, e, hasBeenDeferred);
                        break;
                    case string s when s.StartsWith("ban_map_"):
                        await HandleMapBanAsync(client, e, hasBeenDeferred);
                        break;
                    case string s when s.StartsWith("pick_map_"):
                        await HandleMapPickAsync(client, e, hasBeenDeferred);
                        break;
                    default:
                        await SendErrorResponseAsync(e, "Unsupported map ban operation", hasBeenDeferred);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MapBanHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred while processing map ban: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles map ban dropdown selection
        /// </summary>
        private async Task HandleMapBanDropdownAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Get the tournament match thread data
                if (!_roundsHolder.TourneyRounds.Any(r => r.Teams?.Any(t => t.Thread?.Id == e.Channel.Id) ?? false) ||
                    !await EnsureValidTournamentThreadAsync(e, GetRoundFromChannel(e.Channel.Id), hasBeenDeferred))
                {
                    return;
                }

                var round = GetRoundFromChannel(e.Channel.Id);
                if (round is null)
                {
                    await SendErrorResponseAsync(e, "Could not find associated tournament round", hasBeenDeferred);
                    return;
                }

                var teams = round.Teams;
                var teamForMapBan = teams?.Where(t => t is not null && t.Participants is not null &&
                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id)).FirstOrDefault();

                if (teamForMapBan is null)
                {
                    await SendErrorResponseAsync(e, $"You're not allowed to interact with this component as you are not part of a team in this match.", hasBeenDeferred);
                    return;
                }

                // Try to acknowledge the interaction if it hasn't been already
                if (!hasBeenDeferred)
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        // Interaction might already be acknowledged
                        _logger.LogWarning(ex, "Could not defer interaction in HandleMapBanDropdownAsync");
                    }
                }

                if (e.Channel is not null && e.Values is not null)
                {
                    List<string> bannedMaps = e.Values.ToList();

                    // Validate map bans
                    var (isValid, validatedBans, errorMessage) = _mapService.ValidateMapBans(bannedMaps, round.OneVOne);
                    if (!isValid)
                    {
                        await SendErrorResponseAsync(e, errorMessage ?? "Invalid map ban selection", hasBeenDeferred);
                        return;
                    }

                    // Store map bans temporarily
                    teamForMapBan.MapBans = validatedBans;

                    // Use the match status service to record the map bans in the centralized status embed
                    await _matchStatusService.RecordMapBanAsync(e.Channel, round, teamForMapBan.Name ?? "Unknown Team", validatedBans, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleMapBanDropdownAsync");
                await SendErrorResponseAsync(e, $"An error occurred while processing map bans: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles confirming map ban selections
        /// </summary>
        private async Task HandleConfirmMapBansAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Get the tournament match thread data
                var round = GetRoundFromChannel(e.Channel.Id);
                if (round is null || !await EnsureValidTournamentThreadAsync(e, round, hasBeenDeferred))
                {
                    return;
                }

                var teams = round.Teams;
                string teamName = e.Id.Replace("confirm_map_bans_", "");

                // Find the team based on the team name from the button ID
                var team = teams?.FirstOrDefault(t => t?.Name == teamName);
                if (team is null)
                {
                    await SendErrorResponseAsync(e, "Could not find team for map ban confirmation", hasBeenDeferred);
                    return;
                }

                // Verify user is part of the team
                bool isUserInTeam = team.Participants?.Any(p => p?.Player?.Id == e.User.Id) ?? false;
                if (!isUserInTeam)
                {
                    await SendErrorResponseAsync(e, "You can only confirm map bans for your own team", hasBeenDeferred);
                    return;
                }

                // Try to acknowledge the interaction if it hasn't been already
                if (!hasBeenDeferred)
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        // Interaction might already be acknowledged
                        _logger.LogWarning(ex, "Could not defer confirmation interaction in HandleConfirmMapBansAsync");
                    }
                }

                // Check if both teams have submitted their bans
                bool allTeamsSubmitted = teams?.All(t => t?.MapBans?.Any() == true) ?? false;

                if (allTeamsSubmitted)
                {
                    // Update the match status to move to the next stage (deck submission)
                    if (e.Channel is not null)
                    {
                        await _matchStatusService.UpdateToDeckSubmissionStageAsync(e.Channel, round, client);
                    }

                    // Log the successful map ban completion
                    if (e.Channel is not null)
                    {
                        _logger.LogInformation($"Map bans completed for match in channel {e.Channel.Id}");
                    }

                    // Save both tournament state and tournament data files
                    await _stateService.SaveTournamentStateAsync(client);

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var tournamentManager = scope.ServiceProvider.GetRequiredService<ITournamentManagerService>();
                        await tournamentManager.SaveAllDataAsync();
                    }
                }
                else
                {
                    // Update the match status to show this team's bans are confirmed
                    if (e.Channel is not null)
                    {
                        await _matchStatusService.UpdateToMapBanStageAsync(e.Channel, round, client);
                    }

                    // Send an ephemeral message to the user
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Your map bans have been confirmed. Waiting for the other team to submit their bans.")
                        .AsEphemeral(true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleConfirmMapBansAsync");
                await SendErrorResponseAsync(e, $"An error occurred while confirming map bans: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles revising map ban selections
        /// </summary>
        private async Task HandleReviseMapBansAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Get the tournament match thread data
                var round = GetRoundFromChannel(e.Channel.Id);
                if (round is null || !await EnsureValidTournamentThreadAsync(e, round, hasBeenDeferred))
                {
                    return;
                }

                var teams = round.Teams;
                string teamName = e.Id.Replace("revise_map_bans_", "");

                // Find the team based on the team name from the button ID
                var team = teams?.FirstOrDefault(t => t?.Name == teamName);
                if (team is null)
                {
                    await SendErrorResponseAsync(e, "Could not find team for map ban revision", hasBeenDeferred);
                    return;
                }

                // Verify user is part of the team
                bool isUserInTeam = team.Participants?.Any(p => p?.Player?.Id == e.User.Id) ?? false;
                if (!isUserInTeam)
                {
                    await SendErrorResponseAsync(e, "You can only revise map bans for your own team", hasBeenDeferred);
                    return;
                }

                // Try to acknowledge the interaction if it hasn't been already
                if (!hasBeenDeferred)
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                        hasBeenDeferred = true;
                    }
                    catch (Exception ex)
                    {
                        // Interaction might already be acknowledged
                        _logger.LogWarning(ex, "Could not defer revision interaction in HandleReviseMapBansAsync");
                    }
                }

                // Clear the team's map bans
                team.MapBans?.Clear();

                // Update the match status to show the map ban UI again
                if (e.Channel is not null)
                {
                    await _matchStatusService.UpdateToMapBanStageAsync(e.Channel, round, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleReviseMapBansAsync");
                await SendErrorResponseAsync(e, $"An error occurred while revising map bans: {ex.Message}", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Get a round based on the channel ID
        /// </summary>
        private Round? GetRoundFromChannel(ulong channelId)
        {
            return _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                r.Teams?.Any(t => t?.Thread?.Id == channelId) ?? false);
        }

        /// <summary>
        /// Handles map ban interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleMapBanAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // This method is kept for future implementation of direct map banning functionality
            // Currently, the application uses the dropdown-based workflow implemented above
            await SendErrorResponseAsync(e, "This map ban method is no longer supported. Please use the dropdown selection method.", hasBeenDeferred);
        }

        /// <summary>
        /// Handles map pick interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleMapPickAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            // This method is kept for future implementation of map picking functionality
            // Currently, map picks are managed through a different workflow
            await SendErrorResponseAsync(e, "Map picking functionality is coming soon.", hasBeenDeferred);
        }

        /// <summary>
        /// Ensures the interaction is in a valid tournament thread
        /// </summary>
        private async Task<bool> EnsureValidTournamentThreadAsync(ComponentInteractionCreatedEventArgs e, Round? round, bool hasBeenDeferred)
        {
            if (round is null)
            {
                await SendErrorResponseAsync(e, "This interaction must be used in a tournament match thread.", hasBeenDeferred);
                return false;
            }

            // Use DiscordChannelType instead of ChannelType
            if (e.Channel.Type is not DiscordChannelType.PrivateThread &&
                e.Channel.Type is not DiscordChannelType.PublicThread)
            {
                await SendErrorResponseAsync(e, "This interaction must be used in a tournament match thread.", hasBeenDeferred);
                return false;
            }

            return true;
        }
    }
}