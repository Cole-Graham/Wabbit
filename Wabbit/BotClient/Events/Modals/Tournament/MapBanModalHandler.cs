using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Modals.Base;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Modals.Tournament
{
    /// <summary>
    /// Handles map ban modal submissions
    /// </summary>
    public class MapBanModalHandler : ModalHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly ITournamentMapService _mapService;
        private readonly ITournamentMatchService _matchService;
        private readonly IMatchStatusService _matchStatusService;
        private readonly ITournamentStateService _stateService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="mapService">Service for accessing map data</param>
        /// <param name="matchService">Service for accessing match data</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        public MapBanModalHandler(
            ILogger<MapBanModalHandler> logger,
            OngoingRounds roundsHolder,
            ITournamentMapService mapService,
            ITournamentMatchService matchService,
            IMatchStatusService matchStatusService,
            ITournamentStateService stateService)
            : base(logger)
        {
            _roundsHolder = roundsHolder;
            _mapService = mapService;
            _matchService = matchService;
            _matchStatusService = matchStatusService;
            _stateService = stateService;
        }

        /// <summary>
        /// Determines if this handler can handle the given modal
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>True if this handler can handle the modal, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("map_ban_modal_");
        }

        /// <summary>
        /// Handles map ban modal submissions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling map ban modal from {User}", e.Interaction.User.Username);

                // Get the tournament match thread data
                var round = GetRoundFromChannel(e.Interaction.Channel.Id);
                if (round is null)
                {
                    await SendErrorResponseAsync(e, "Could not find associated tournament round", hasBeenDeferred);
                    return;
                }

                // Check if we're in the correct stage for map bans
                if (round.CurrentStage != MatchStage.MapBan)
                {
                    string errorMessage = round.CurrentStage == MatchStage.DeckSubmission
                        ? "Map bans are no longer available. The match has progressed to deck submission."
                        : round.CurrentStage == MatchStage.GameResults
                            ? "Map bans are no longer available. The match has progressed to game results."
                            : "Map bans are not available at this time.";

                    await SendErrorResponseAsync(e, errorMessage, hasBeenDeferred);
                    return;
                }

                var teams = round.Teams;
                var teamForMapBan = teams?.Where(t => t is not null && t.Participants is not null &&
                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.Interaction.User.Id)).FirstOrDefault();

                if (teamForMapBan is null)
                {
                    await SendErrorResponseAsync(e, "You're not allowed to submit map bans as you are not part of a team in this match.", hasBeenDeferred);
                    return;
                }

                // Get map ban selections from the modal
                var bannedMaps = new List<string>();
                for (int i = 0; i < (round.Length == 5 ? 2 : 3); i++)
                {
                    if (e.Values.TryGetValue($"map_ban_{i}", out var mapValue) && !string.IsNullOrWhiteSpace(mapValue))
                    {
                        bannedMaps.Add(mapValue);
                    }
                }

                // Validate the number of bans
                int requiredBans = round.Length == 5 ? 2 : 3;
                if (bannedMaps.Count != requiredBans)
                {
                    await SendErrorResponseAsync(e, $"Please select exactly {requiredBans} maps to ban.", hasBeenDeferred);
                    return;
                }

                // Store map bans temporarily
                teamForMapBan.MapBans = bannedMaps;

                // Use the match status service to record the map bans in the centralized status embed
                await _matchStatusService.RecordMapBanAsync(e.Interaction.Channel, round, teamForMapBan.Name ?? "Unknown Team", bannedMaps, client);

                // Create confirmation embed with clear priority order
                var confirmEmbed = new DiscordEmbedBuilder()
                {
                    Title = "Review Map Ban Selections",
                    Description = "Please review your map ban selections below. The order represents your ban priority.\n\n**You can change your selections by clicking the Revise button.**",
                    Color = DiscordColor.Orange
                };

                for (int i = 0; i < bannedMaps.Count; i++)
                {
                    confirmEmbed.AddField($"Priority #{i + 1}", bannedMaps[i] ?? "Unknown", true);
                }

                // Add confirmation and revision buttons
                var confirmBtn = new DiscordButtonComponent(DiscordButtonStyle.Success, $"confirm_map_bans_{teams?.IndexOf(teamForMapBan) ?? -1}", "Confirm Selections");
                var reviseBtn = new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"revise_map_bans_{teams?.IndexOf(teamForMapBan) ?? -1}", "Revise Selections");

                // Send a new ephemeral message to the user with the confirmation options
                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent($"Please confirm your map ban selections:")
                        .AddEmbed(confirmEmbed)
                        .AddComponents(confirmBtn, reviseBtn)
                        .AsEphemeral(true));

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MapBanModalHandler.HandleAsync");
                await SendErrorResponseAsync(e, $"An error occurred while processing map ban selections: {ex.Message}", hasBeenDeferred);
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
    }
}