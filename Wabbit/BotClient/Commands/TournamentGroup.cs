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

        public TournamentGroup(
            OngoingRounds ongoingRounds,
            ILogger<TournamentGroup> logger,
            ITournamentStateService stateService,
            ITournamentService tournamentService,
            ITournamentMatchService tournamentMatchService)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;
            _stateService = stateService;
            _tournamentService = tournamentService;
            _tournamentMatchService = tournamentMatchService;
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

                // Store as temporary deck code
                participant.TempDeckCode = deckCode;
                _logger.LogInformation($"Temporary deck code stored for user {context.User.Username} (ID: {context.User.Id})");

                // Create confirmation message with buttons
                var confirmEmbed = new DiscordEmbedBuilder()
                    .WithTitle("Confirm Deck Code")
                    .WithDescription("Please review your deck code and confirm if it's correct:")
                    .AddField("Deck Code", deckCode)
                    .WithColor(DiscordColor.Orange);

                var confirmBtn = new DiscordButtonComponent(
                    DiscordButtonStyle.Success,
                    $"confirm_deck_{context.User.Id}",
                    "Confirm");

                var reviseBtn = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"revise_deck_{context.User.Id}",
                    "Revise");

                // Send the confirmation message
                await context.EditResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent($"{context.User.Mention} Please review your deck code submission:")
                        .AddEmbed(confirmEmbed)
                        .AddComponents(confirmBtn, reviseBtn));

                // Save the tournament state
                await _stateService.SaveTournamentStateAsync(context.Client);
                _logger.LogInformation($"Tournament state saved after deck submission for user {context.User.Username} (ID: {context.User.Id})");
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
