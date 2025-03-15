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
    /// Handles deck submission modal interactions
    /// </summary>
    public class DeckSubmissionModalHandler : ModalHandlerBase
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly ITournamentStateService _stateService;
        private readonly IMatchStatusService _matchStatusService;
        private readonly ITournamentService _tournamentService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="roundsHolder">Service for accessing ongoing rounds</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        /// <param name="matchStatusService">Service for managing match status display</param>
        /// <param name="tournamentService">Service for accessing tournament data</param>
        public DeckSubmissionModalHandler(
            ILogger<DeckSubmissionModalHandler> logger,
            OngoingRounds roundsHolder,
            ITournamentStateService stateService,
            IMatchStatusService matchStatusService,
            ITournamentService tournamentService)
            : base(logger)
        {
            _roundsHolder = roundsHolder;
            _stateService = stateService;
            _matchStatusService = matchStatusService;
            _tournamentService = tournamentService;
        }

        /// <summary>
        /// Determines if this handler can handle the given modal
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>True if this handler can handle the modal, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            // This handler handles all deck submission modals with the "deck_code" field
            // The customId is not specifically checked since deck modals don't have a consistent prefix
            return true; // Will be checked in HandleAsync by looking for "deck_code" field
        }

        /// <summary>
        /// Handles deck submission modal interactions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling deck submission modal from {User}", e.Interaction.User.Username);

                // Check if this is a deck submission modal by looking for the deck_code field
                if (!e.Values.TryGetValue("deck_code", out var deckCode))
                {
                    _logger.LogInformation("Not a deck submission modal (no deck_code field)");
                    return; // Not a deck submission modal, let another handler process it
                }

                if (string.IsNullOrWhiteSpace(deckCode))
                {
                    await SendErrorResponseAsync(e, "Deck code cannot be empty.", hasBeenDeferred);
                    return;
                }

                // Handle tournament deck submission
                var tournament = _tournamentService.GetAllTournaments()
                    .FirstOrDefault(t => t.Groups.Any(g => g.Matches.Any(m =>
                        m.LinkedRound?.Teams?.Any(team => team.Thread?.Id == e.Interaction.Channel.Id) == true)) ||
                                       t.PlayoffMatches.Any(m =>
                        m.LinkedRound?.Teams?.Any(team => team.Thread?.Id == e.Interaction.Channel.Id) == true));

                if (tournament != null)
                {
                    // Find the match corresponding to this thread
                    var match = tournament.Groups.SelectMany(g => g.Matches)
                        .Concat(tournament.PlayoffMatches)
                        .FirstOrDefault(m => m.LinkedRound?.Teams?.Any(team => team.Thread?.Id == e.Interaction.Channel.Id) == true);

                    if (match != null)
                    {
                        // Find the participant
                        var participant = match.Participants.FirstOrDefault(p =>
                            p.Player is DiscordMember member && member.Id == e.Interaction.User.Id);
                        if (participant is null)
                        {
                            await SendErrorResponseAsync(e, "You are not a participant in this match.", hasBeenDeferred);
                            return;
                        }

                        // Store the deck code
                        if (match.Result is null)
                        {
                            match.Result = new Models.Tournament.MatchResult();
                        }
                        if (match.Result.DeckCodes is null)
                        {
                            match.Result.DeckCodes = new Dictionary<string, Dictionary<string, string>>();
                        }
                        if (!match.Result.DeckCodes.ContainsKey(match.LinkedRound?.Name ?? "unknown"))
                        {
                            match.Result.DeckCodes[match.LinkedRound?.Name ?? "unknown"] = new Dictionary<string, string>();
                        }
                        match.Result.DeckCodes[match.LinkedRound?.Name ?? "unknown"][e.Interaction.User.Id.ToString()] = deckCode;
                        await _stateService.SaveTournamentStateAsync(client);

                        if (match.LinkedRound is null)
                        {
                            await SendErrorResponseAsync(e, "Match round information is missing.", hasBeenDeferred);
                            return;
                        }

                        // Update the match status
                        await _matchStatusService.RecordDeckSubmissionAsync(
                            e.Interaction.Channel,
                            match.LinkedRound,
                            e.Interaction.User.Id,
                            deckCode,
                            1,
                            client);

                        // Send success message
                        await SendResponseAsync(e, "Your deck has been submitted.", hasBeenDeferred, DiscordColor.Green);
                        return;
                    }
                }

                // Handle regular round deck submission
                if (_roundsHolder.RegularRounds.Count > 0)
                {
                    var user = e.Interaction.User;
                    var round = _roundsHolder.RegularRounds
                        .FirstOrDefault(p => p.Player1?.Id == user.Id || p.Player2?.Id == user.Id);

                    if (round is null)
                    {
                        await SendErrorResponseAsync(e, $"{user.Username} is not a participant of this round", hasBeenDeferred);
                        return;
                    }

                    // Store the deck code
                    if (round.Player1?.Id == user.Id)
                        round.Deck1 = deckCode;
                    else
                        round.Deck2 = deckCode;

                    // Send success message
                    await SendResponseAsync(e, $"Deck of {user.Username} has been submitted", hasBeenDeferred, DiscordColor.Green);

                    // Save state after regular round deck submission
                    await _stateService.SaveTournamentStateAsync(client);

                    // Check if both decks have been submitted
                    if (!string.IsNullOrEmpty(round.Deck1) && !string.IsNullOrEmpty(round.Deck2))
                    {
                        await SendResponseAsync(e, "Both decks have been submitted. The match can now begin!", hasBeenDeferred, DiscordColor.Green);
                    }

                    return;
                }

                await SendErrorResponseAsync(e, "Could not find a tournament match or regular round for your deck submission.", hasBeenDeferred);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeckSubmissionModalHandler");
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}