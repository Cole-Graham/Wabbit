using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus;
using Wabbit.Misc;
using Wabbit.Data;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbit.Services;
using Wabbit.BotClient.Attributes;

namespace Wabbit.BotClient.Commands
{
    [Command("Tournament")]
    [RequireWhitelistedRole]
    public class TournamentGroup
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentGroup> _logger;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentService _tournamentService;
        private readonly ITournamentMatchService _tournamentMatchService;
        private readonly IMatchStatusService _matchStatusService;

        public TournamentGroup(
            OngoingRounds ongoingRounds,
            ILogger<TournamentGroup> logger,
            ITournamentStateService stateService,
            ITournamentService tournamentService,
            ITournamentMatchService tournamentMatchService,
            IMatchStatusService matchStatusService)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;
            _stateService = stateService;
            _tournamentService = tournamentService;
            _tournamentMatchService = tournamentMatchService;
            _matchStatusService = matchStatusService;
        }

        [Command("submit_deck")]
        [Description("Submit a deck code for your current tournament match")]
        public async Task SubmitDeck(
            CommandContext context,
            [Description("Your deck code")] string deckCode)
        {
            await context.DeferResponseAsync();

            try
            {
                _logger.LogInformation($"User {context.User.Username} (ID: {context.User.Id}) submitting deck code in channel {context.Channel.Name} (ID: {context.Channel.Id})");

                // Check if used in a private thread (tournament matches use private threads)
                if (context.Channel.Type != DiscordChannelType.PrivateThread)
                {
                    _logger.LogWarning($"Deck submission attempted in non-thread channel type: {context.Channel.Type}");
                    await context.EditResponseAsync("This command can only be used in tournament match threads.");
                    return;
                }

                // Find the tournament round for this thread
                var round = _ongoingRounds.TourneyRounds?.FirstOrDefault(r =>
                    r.Teams is not null &&
                    r.Teams.Any(t => t.Thread is not null && t.Thread.Id == context.Channel.Id));

                if (round is null)
                {
                    _logger.LogWarning($"No tournament round found for thread: {context.Channel.Id}");
                    await context.EditResponseAsync("No active tournament round found for this channel.");
                    return;
                }

                // Check if current stage allows deck submission
                if (round.CurrentStage != MatchStage.DeckSubmission)
                {
                    string stageMessage = round.CurrentStage == MatchStage.MapBan
                        ? "Map bans must be completed before deck submission."
                        : "Deck submission is no longer available.";
                    await context.EditResponseAsync($"Cannot submit deck in the current match stage. {stageMessage}");
                    return;
                }

                // Find the team associated with this thread
                var team = round.Teams?.FirstOrDefault(t => t.Thread is not null && t.Thread.Id == context.Channel.Id);
                if (team is null)
                {
                    _logger.LogWarning($"No team found for thread: {context.Channel.Id} in round: {round.Name}");
                    await context.EditResponseAsync("Could not find the team associated with this thread.");
                    return;
                }

                // Check if the user is a participant in this team
                var participant = team.Participants?.FirstOrDefault(p =>
                    p is not null && p.Player is not null && p.Player.Id == context.User.Id);

                if (participant is null)
                {
                    _logger.LogWarning($"User {context.User.Username} (ID: {context.User.Id}) is not a participant in team: {team.Name}");
                    await context.EditResponseAsync("You are not a participant in this tournament match.");
                    return;
                }

                // Check if a game is in progress
                if (round.InGame == true)
                {
                    _logger.LogWarning($"Deck submission attempted while game is in progress for round: {round.Name}");
                    await context.EditResponseAsync("Game is in progress. Deck submission is disabled.");
                    return;
                }

                // Use MatchStatusService to record the deck submission
                // This will handle storing the deck code, updating UI with confirm/revise buttons, and state saving
                int gameNumber = round.Maps?.Count ?? 0;
                await _matchStatusService.RecordDeckSubmissionAsync(
                    context.Channel,
                    round,
                    context.User.Id,
                    deckCode,
                    gameNumber,
                    context.Client);


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting deck code for user {context.User.Username} (ID: {context.User.Id})");
                await context.EditResponseAsync($"Error submitting deck: {ex.Message}");
            }
        }

        #region Service

        private class RoundLength : IChoiceProvider
        {
            private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> length =
                [
                    new DiscordApplicationCommandOptionChoice("Bo1", 1),
                    new DiscordApplicationCommandOptionChoice("Bo3", 3),
                    new DiscordApplicationCommandOptionChoice("Bo5", 5)
                ];

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
                ValueTask.FromResult(length);
        }

        #endregion
    }
}
