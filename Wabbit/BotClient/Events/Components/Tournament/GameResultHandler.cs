using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handles game result-related component interactions
    /// </summary>
    public class GameResultHandler : ComponentHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly ITournamentGameService _gameService;
        private readonly ITournamentMatchService _matchService;
        private readonly IMatchStatusService _matchStatusService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="gameService">Service for accessing game data</param>
        /// <param name="matchService">Service for accessing match data</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        public GameResultHandler(
            ILogger<GameResultHandler> logger,
            ITournamentStateService stateService,
            OngoingRounds roundsHolder,
            ITournamentGameService gameService,
            ITournamentMatchService matchService,
            IMatchStatusService matchStatusService)
            : base(logger, stateService)
        {
            _roundsHolder = roundsHolder;
            _gameService = gameService;
            _matchService = matchService;
            _matchStatusService = matchStatusService;
        }

        /// <summary>
        /// Determines if this handler can handle the given component
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>True if this handler can handle the component, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId == "tournament_game_winner_dropdown" ||
                   customId.StartsWith("game_winner_") ||
                   customId.StartsWith("game_result_");
        }

        /// <summary>
        /// Handles game result-related component interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            _logger.LogInformation("Handling game result component: {ComponentId}", e.Id);

            if (e.Id == "tournament_game_winner_dropdown")
            {
                await HandleGameWinnerDropdown(client, e, hasBeenDeferred);
            }
            else if (e.Id.StartsWith("game_winner_"))
            {
                // This would be implemented if there were game_winner_ custom ID buttons/dropdowns
                await SendErrorResponseAsync(e, "This type of game result submission is not currently supported.", hasBeenDeferred);
            }
            else if (e.Id.StartsWith("game_result_"))
            {
                await SendErrorResponseAsync(e, "This type of game result submission is not currently supported.", hasBeenDeferred);
            }
        }

        /// <summary>
        /// Handles game winner dropdown interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        private async Task HandleGameWinnerDropdown(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Only try to defer if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // First identify the thread/channel and associated round
                if (e.Channel is null || e.Channel.Type is not DiscordChannelType.PrivateThread)
                {
                    await SendErrorResponseAsync(e, "This dropdown should only be used in tournament match threads", hasBeenDeferred);
                    return;
                }

                // Find the tournament round associated with this thread
                var tournamentRound = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                    r.Teams != null && r.Teams.Any(t => t.Thread is not null && t.Thread.Id == e.Channel.Id));

                if (tournamentRound == null)
                {
                    await SendErrorResponseAsync(e, "Could not find tournament round for this thread", hasBeenDeferred);
                    return;
                }

                // Check if we're in the correct stage for game results
                if (tournamentRound.CurrentStage != MatchStage.GameResults)
                {
                    string errorMessage = tournamentRound.CurrentStage == MatchStage.MapBan
                        ? "Game results recording is not yet available. Please complete the map ban stage first."
                        : tournamentRound.CurrentStage == MatchStage.DeckSubmission
                            ? "Game results recording is not yet available. Please complete the deck submission stage first."
                            : "Game results recording is not available at this time.";

                    await SendErrorResponseAsync(e, errorMessage, hasBeenDeferred);
                    return;
                }

                if (e.Values == null || !e.Values.Any())
                {
                    await SendErrorResponseAsync(e, "No winner selected", hasBeenDeferred);
                    return;
                }

                // Process game result
                string selectedValue = e.Values[0];

                // Parse the winner ID from the value format "game_winner:userId" or "game_winner:draw"
                if (!selectedValue.StartsWith("game_winner:"))
                {
                    await SendErrorResponseAsync(e, "Invalid selection format", hasBeenDeferred);
                    return;
                }

                string winnerId = selectedValue.Substring("game_winner:".Length);

                try
                {
                    // Use the injected TournamentGameService 
                    await _gameService.HandleGameResultAsync(tournamentRound, e.Channel, winnerId, client);

                    // Determine the appropriate response message based on the winner
                    string resultMessage;
                    string winnerName = winnerId; // Default to the raw winnerId

                    if (winnerId.Equals("draw", StringComparison.OrdinalIgnoreCase))
                    {
                        resultMessage = "Game result recorded: **Draw**";
                        winnerName = "draw";
                    }
                    else if (ulong.TryParse(winnerId, out ulong winnerUserId))
                    {
                        // Try to get the user from the client
                        try
                        {
                            var winnerUser = await client.GetUserAsync(winnerUserId);
                            winnerName = winnerUser?.Username ?? $"User {winnerUserId}";
                        }
                        catch
                        {
                            winnerName = $"User {winnerUserId}";
                        }
                        resultMessage = $"Game result recorded: **{winnerName}** wins";
                    }
                    else
                    {
                        resultMessage = "Game result recorded";
                    }

                    // Update match status with game result
                    try
                    {
                        // Only proceed if channel is available
                        if (e.Channel is not null)
                        {
                            // Use the cycle property to determine game number, or default to 1 if not available
                            int gameNumber = tournamentRound.Cycle > 0 ? tournamentRound.Cycle : 1;

                            await _matchStatusService.RecordGameResultAsync(
                                e.Channel,
                                tournamentRound,
                                winnerName,
                                gameNumber,
                                client);

                            _logger.LogInformation($"Updated match status for game result in thread {e.Channel.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ulong channelId = e.Channel?.Id ?? 0;
                        _logger.LogError(ex,
                            $"Failed to update match status for game result in thread {channelId}");
                    }

                    // Send a success message - visible to all participants
                    if (hasBeenDeferred)
                    {
                        // Use the channel messaging directly for success messages to ensure they're visible to everyone
                        if (e.Channel is not null)
                        {
                            await e.Channel.SendMessageAsync(new DiscordMessageBuilder()
                                .WithContent(resultMessage));
                        }

                        // Success response to interaction should explain it's visible for all
                        await e.Interaction.EditOriginalResponseAsync(
                            new DiscordWebhookBuilder().WithContent(
                                "Game result recorded. All participants can see the result in the channel."));
                    }
                    else
                    {
                        // If not deferred (unusual case), send directly to channel
                        if (e.Channel is not null)
                        {
                            await e.Channel.SendMessageAsync(resultMessage);
                        }
                    }

                    // Log success
                    _logger.LogInformation("Successfully processed game result with winner {WinnerId}", winnerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling game result");
                    await SendErrorResponseAsync(e, $"Error processing game result: {ex.Message}", hasBeenDeferred);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in HandleGameWinnerDropdown");

                // Send a more generic error message
                await SendErrorResponseAsync(e, $"An unexpected error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}