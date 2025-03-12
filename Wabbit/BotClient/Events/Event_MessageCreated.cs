using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Wabbit.BotClient.Events
{
    public class Event_MessageCreated : IEventHandler<MessageCreatedEventArgs>
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly TournamentManager _tournamentManager;

        public Event_MessageCreated(OngoingRounds ongoingRounds, TournamentManager tournamentManager)
        {
            _ongoingRounds = ongoingRounds;
            _tournamentManager = tournamentManager;
        }

        public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
        {
            // Ignore messages from bots
            if (e.Author.IsBot)
                return;

            // Check if this is a response to the tournament creation message
            if (e.Message.ReferencedMessage is not null)
            {
                // Check if the referenced message is from the bot and contains "Tournament creation started"
                if (e.Message.ReferencedMessage.Author?.Id == sender.CurrentUser.Id &&
                    e.Message.ReferencedMessage.Content != null &&
                    e.Message.ReferencedMessage.Content.Contains("Tournament creation started"))
                {
                    await HandleTournamentCreation(sender, e);
                    return;
                }
            }

            // Check if this is in the signup channel
            var serverConfig = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == e.Guild?.Id);
            if (serverConfig?.SignupChannelId == e.Channel?.Id)
            {
                // Handle interactions in the signup channel
                // This could include special commands or reactions
            }
        }

        private async Task HandleTournamentCreation(DiscordClient sender, MessageCreatedEventArgs e)
        {
            try
            {
                if (e.Message.ReferencedMessage == null || e.Message.ReferencedMessage.Content == null)
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
                GameType gameType = GameType.OneVsOne; // Default game type

                // Check if seeding is mentioned
                bool useSeeding = referencedContent.Contains("Seeding is enabled", StringComparison.OrdinalIgnoreCase);

                // Try to find the format in the message content
                if (referencedContent.Contains("format:"))
                {
                    // Parse format string
                    int formatStartIdx = referencedContent.IndexOf("format:") + "format:".Length;
                    int formatEndIdx = referencedContent.IndexOf('\'', formatStartIdx + 1);
                    if (formatStartIdx > 0 && formatEndIdx > formatStartIdx)
                    {
                        string formatStr = referencedContent.Substring(formatStartIdx, formatEndIdx - formatStartIdx).Trim();
                        formatStr = formatStr.Trim('\'', ' ');

                        if (Enum.TryParse<TournamentFormat>(formatStr, out var parsedFormat))
                        {
                            format = parsedFormat;
                        }
                    }
                }

                // Try to find the game type in the message content
                if (referencedContent.Contains("game type:"))
                {
                    if (referencedContent.Contains("2v2"))
                    {
                        gameType = GameType.TwoVsTwo;
                    }
                }

                // Parse mentions to get player list
                List<DiscordMember> players = new List<DiscordMember>();
                Dictionary<DiscordMember, int> playerSeeds = new Dictionary<DiscordMember, int>();

                foreach (var user in e.Message.MentionedUsers)
                {
                    try
                    {
                        var member = await e.Guild.GetMemberAsync(user.Id);
                        if (member is not null)
                        {
                            players.Add(member);

                            // If seeding is enabled, check for a number after the mention
                            if (useSeeding)
                            {
                                string messageContent = e.Message.Content;
                                string mentionPattern = $"<@!?{user.Id}>";
                                var match = Regex.Match(messageContent, mentionPattern);

                                if (match.Success)
                                {
                                    int mentionEnd = match.Index + match.Length;
                                    if (mentionEnd < messageContent.Length)
                                    {
                                        // Look for a number after the mention
                                        string afterMention = messageContent.Substring(mentionEnd);
                                        var numberMatch = Regex.Match(afterMention, @"^\s*(\d+)");

                                        if (numberMatch.Success && int.TryParse(numberMatch.Groups[1].Value, out int seedValue) && seedValue > 0)
                                        {
                                            playerSeeds[member] = seedValue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting member {user.Id}: {ex.Message}");
                        // Continue with other members
                    }
                }

                if (players.Count < 3)
                {
                    await e.Channel.SendMessageAsync("At least 3 players are required to create a tournament.");
                    return;
                }

                // Create the tournament
                var tournament = await _tournamentManager.CreateTournament(
                    tournamentName,
                    players,
                    format,
                    e.Channel,
                    gameType,
                    useSeeding ? playerSeeds : null);

                // Build response message
                string seedInfo = "";
                if (useSeeding && playerSeeds.Any())
                {
                    seedInfo = "\n**Seeded Players:**\n" + string.Join("\n",
                        playerSeeds.OrderBy(s => s.Value)
                                  .Select(s => $"• {s.Key.DisplayName}: Seed #{s.Value}"));
                }

                await e.Channel.SendMessageAsync($"Tournament '{tournamentName}' created successfully with {players.Count} participants! (Format: {format}, Game Type: {(gameType == GameType.OneVsOne ? "1v1" : "2v2")}){seedInfo}");

                // Generate and send the standings image
                try
                {
                    string imagePath = await TournamentVisualization.GenerateStandingsImage(tournament, sender);

                    using (var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                    {
                        var messageBuilder = new DiscordMessageBuilder()
                            .WithContent($"📊 **{tournament.Name}** Initial Standings")
                            .AddFile(Path.GetFileName(imagePath), fileStream);

                        await e.Channel.SendMessageAsync(messageBuilder);
                    }
                }
                catch (Exception ex)
                {
                    await e.Channel.SendMessageAsync($"Tournament created, but failed to generate standings image: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                await e.Channel.SendMessageAsync($"Error creating tournament: {ex.Message}");
                Console.WriteLine($"Error in HandleTournamentCreation: {ex}");
            }
        }
    }
}