using DSharpPlus;
using DSharpPlus.EventArgs;
using System.Threading.Tasks;
using System.Collections.Generic;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using System;
using System.Linq;
using DSharpPlus.Entities;

namespace Wabbit.BotClient.Events
{
    public class Event_MessageCreated : IEventHandler<MessageCreatedEventArgs>
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly TournamentManager _tournamentManager;

        public Event_MessageCreated(OngoingRounds ongoingRounds)
        {
            _ongoingRounds = ongoingRounds;
            _tournamentManager = new TournamentManager(ongoingRounds);
        }

        public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
        {
            // Ignore messages from bots
            if (e.Author.IsBot)
                return;

            // Check if this is a deck submission (replying to a message that starts with 'Please enter your deck code')
            if (e.Message.ReferencedMessage is not null &&
                e.Message.ReferencedMessage.Content is not null &&
                e.Message.ReferencedMessage.Content.Contains("Please enter your deck code"))
            {
                await HandleDeckSubmission(sender, e);
                return;
            }

            // Check if this is a response to the tournament creation message
            if (e.Message.ReferencedMessage is not null)
            {
                // Check if the referenced message is from the bot and contains "Tournament creation started"
                if (e.Message.ReferencedMessage.Author is not null &&
                    e.Message.ReferencedMessage.Content is not null &&
                    e.Message.ReferencedMessage.Author.Id == sender.CurrentUser.Id &&
                    e.Message.ReferencedMessage.Content.Contains("Tournament creation started"))
                {
                    await HandleTournamentCreation(sender, e);
                    return;
                }
            }

            // Check if this is in the signup channel
            var serverConfig = ConfigManager.Config?.Servers?.FirstOrDefault(s => s.ServerId == e.Guild?.Id);
            if (serverConfig?.SignupChannelId == e.Channel?.Id)
            {
                // Handle interactions in the signup channel
                // This could include special commands or reactions
            }
        }

        private async Task HandleDeckSubmission(DiscordClient sender, MessageCreatedEventArgs e)
        {
            try
            {
                Console.WriteLine($"Processing deck submission from {e.Author.Username} ({e.Author.Id})");

                // Extract the deck code from the message
                string deckCode = e.Message.Content.Trim();

                if (string.IsNullOrWhiteSpace(deckCode))
                {
                    await e.Channel.SendMessageAsync($"{e.Author.Mention} Your deck code seems to be empty. Please send a valid deck code.");
                    return;
                }

                // Find the tournament round
                var round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                    r.Teams is not null &&
                    r.Teams.Any(t => t.Thread?.Id == e.Channel.Id));

                if (round == null)
                {
                    await e.Channel.SendMessageAsync($"{e.Author.Mention} Could not find an active tournament round for this channel.");
                    return;
                }

                // Find the team and participant
                var team = round.Teams.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                if (team == null)
                {
                    await e.Channel.SendMessageAsync($"{e.Author.Mention} Could not find your team data.");
                    return;
                }

                var participant = team.Participants?.FirstOrDefault(p =>
                    p is not null && p.Player is not null && p.Player.Id == e.Author.Id);

                if (participant == null)
                {
                    await e.Channel.SendMessageAsync($"{e.Author.Mention} Could not find your participant data in this tournament.");
                    return;
                }

                // Create confirmation buttons
                var confirmButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Success,
                    $"confirm_deck_{e.Author.Id}",
                    "Confirm Deck Code");

                var reviseButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"revise_deck_{e.Author.Id}",
                    "Revise Deck Code");

                // Send confirmation message with the submitted deck code and buttons
                var confirmationEmbed = new DiscordEmbedBuilder()
                    .WithTitle("Deck Code Submission")
                    .WithDescription($"You've submitted the following deck code:\n```\n{deckCode}\n```\nPlease confirm your submission or revise if needed.")
                    .WithColor(DiscordColor.Orange);

                var confirmationMessage = await e.Channel.SendMessageAsync(
                    new DiscordMessageBuilder()
                        .WithContent($"{e.Author.Mention} Please review your deck code submission.")
                        .AddEmbed(confirmationEmbed)
                        .AddComponents(confirmButton, reviseButton));

                // Store the deck code temporarily in a property on the participant
                participant.TempDeckCode = deckCode;

                // We'll handle the confirmation/revision in the component interaction event
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing deck submission: {ex.Message}");
                await e.Channel.SendMessageAsync($"{e.Author.Mention} An error occurred while processing your deck submission: {ex.Message}");
            }
        }

        private async Task HandleTournamentCreation(DiscordClient sender, MessageCreatedEventArgs e)
        {
            try
            {
                // Ensure referenced message exists
                if (e.Message.ReferencedMessage is null || e.Message.ReferencedMessage.Content is null)
                {
                    await e.Channel.SendMessageAsync("Referenced message not found or has no content.");
                    return;
                }

                // Get the tournament name from the referenced message
                string referencedContent = e.Message.ReferencedMessage.Content;
                int nameStartIndex = referencedContent.IndexOf('\'') + 1;
                int nameEndIndex = referencedContent.IndexOf('\'', nameStartIndex);

                if (nameStartIndex <= 0 || nameEndIndex <= 0 || nameEndIndex <= nameStartIndex)
                {
                    await e.Channel.SendMessageAsync("Failed to parse tournament name from the message.");
                    return;
                }

                string tournamentName = referencedContent.Substring(nameStartIndex, nameEndIndex - nameStartIndex);

                // Check if mentions are present
                if (e.Message.MentionedUsers.Count == 0)
                {
                    await e.Channel.SendMessageAsync("No players were mentioned. Please mention the players who will participate in the tournament.");
                    return;
                }

                // Extract the format from the referenced message
                TournamentFormat format = TournamentFormat.GroupStageWithPlayoffs; // Default format

                try
                {
                    // Try to find the format in the message content
                    if (referencedContent.Contains("format:"))
                    {
                        // Parse format from the message
                        int formatStartIdx = referencedContent.IndexOf("format:") + "format:".Length;
                        int formatEndIdx = referencedContent.IndexOf('\"', formatStartIdx + 1);
                        if (formatStartIdx > 0 && formatEndIdx > formatStartIdx)
                        {
                            string formatStr = referencedContent.Substring(formatStartIdx, formatEndIdx - formatStartIdx).Trim();
                            // Remove quotes if present
                            formatStr = formatStr.Trim('"', ' ');

                            if (Enum.TryParse<TournamentFormat>(formatStr, out var parsedFormat))
                            {
                                format = parsedFormat;
                            }
                        }
                    }
                }
                catch
                {
                    // If we can't parse the format, just use the default
                    format = TournamentFormat.GroupStageWithPlayoffs;
                }

                // Parse mentions to get player list
                List<DiscordMember> players = [];

                foreach (var user in e.Message.MentionedUsers)
                {
                    var member = await e.Guild.GetMemberAsync(user.Id);
                    players.Add(member);
                }

                if (players.Count < 2)
                {
                    await e.Channel.SendMessageAsync("At least 2 players are required to create a tournament.");
                    return;
                }

                // Create the tournament
                var tournament = _tournamentManager.CreateTournament(tournamentName, players, format, e.Channel);

                await e.Channel.SendMessageAsync($"Tournament '{tournamentName}' created successfully with {players.Count} participants!");

                // Generate and send the standings image
                try
                {
                    string imagePath = await TournamentVisualization.GenerateStandingsImage(tournament, sender);

                    var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                    var messageBuilder = new DiscordMessageBuilder()
                        .WithContent($"📊 **{tournament.Name}** Initial Standings")
                        .AddFile(Path.GetFileName(imagePath), fileStream);

                    await e.Channel.SendMessageAsync(messageBuilder);
                }
                catch (Exception ex)
                {
                    await e.Channel.SendMessageAsync($"Tournament created, but failed to generate standings image: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                await e.Channel.SendMessageAsync($"Error creating tournament: {ex.Message}");
            }
        }
    }
}