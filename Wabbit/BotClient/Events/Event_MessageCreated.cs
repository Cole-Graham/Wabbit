using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Wabbit.BotClient.Events
{
    public class Event_MessageCreated : IEventHandler<MessageCreatedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly ILogger<Event_MessageCreated> _logger;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentManagerService _tournamentManagerService;

        public Event_MessageCreated(
            OngoingRounds roundsHolder,
            ILogger<Event_MessageCreated> logger,
            ITournamentStateService stateService,
            ITournamentManagerService tournamentManagerService)
        {
            _roundsHolder = roundsHolder;
            _logger = logger;
            _stateService = stateService;
            _tournamentManagerService = tournamentManagerService;
        }

        public Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Ignore bot messages
                    if (e.Author.IsBot) return;

                    // Only process in thread channels which are likely for tournament matches
                    if (e.Channel.Type != DiscordChannelType.PrivateThread) return;

                    // Check if this channel is a tournament thread
                    var round = _roundsHolder.TourneyRounds?.FirstOrDefault(r =>
                        r.Teams is not null &&
                        r.Teams.Any(t => t.Thread is not null && t.Thread.Id == e.Channel.Id));

                    if (round is null) return;

                    // Find the team associated with this thread
                    var team = round.Teams?.FirstOrDefault(t => t.Thread is not null && t.Thread.Id == e.Channel.Id);
                    if (team is null) return;

                    // Check if the message author is a participant in this team
                    var participant = team.Participants?.FirstOrDefault(p =>
                        p is not null && p.Player is not null && p.Player.Id == e.Author.Id);

                    if (participant is null) return;

                    // Fix the handling of recent messages
                    // Check if this is in response to a deck submission prompt
                    // Look for recent messages from the bot asking for deck codes
                    var recentMessages = new List<DiscordMessage>();
                    await foreach (var message in e.Channel.GetMessagesAsync(10)) // Get last 10 messages
                    {
                        recentMessages.Add(message);
                    }

                    bool isDeckSubmission = recentMessages.Any(m =>
                        m?.Author?.IsBot == true &&
                        m?.Content != null &&
                        m.Content.Contains("Please enter your deck code") &&
                        m.Content.Contains(e.Author.Mention));

                    if (!isDeckSubmission) return;

                    // This appears to be a valid deck submission
                    string deckCode = e.Message.Content.Trim();

                    // Store as temporary deck code
                    participant.TempDeckCode = deckCode;

                    // Create confirmation message with buttons
                    var confirmEmbed = new DiscordEmbedBuilder()
                        .WithTitle("Confirm Deck Code")
                        .WithDescription("Please review your deck code and confirm if it's correct:")
                        .AddField("Deck Code", deckCode)
                        .WithColor(DiscordColor.Orange);

                    var confirmBtn = new DiscordButtonComponent(
                        DiscordButtonStyle.Success,
                        $"confirm_deck_{e.Author.Id}",
                        "Confirm");

                    var reviseBtn = new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"revise_deck_{e.Author.Id}",
                        "Revise");

                    // Send the confirmation message
                    await e.Channel.SendMessageAsync(
                        new DiscordMessageBuilder()
                            .WithContent($"{e.Author.Mention} Please review your deck code submission:")
                            .AddEmbed(confirmEmbed)
                            .AddComponents(confirmBtn, reviseBtn));

                    // Log the submission
                    _logger.LogInformation($"Received deck code from {e.Author.Username} in thread {e.Channel.Name}");

                    // Save tournament state to preserve the temp deck code
                    await _stateService.SaveTournamentStateAsync(sender);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message for deck submission");
                }
            });

            return Task.CompletedTask;
        }
    }
}