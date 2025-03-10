using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Wabbit.BotClient.Events
{
    public class Event_Modal : IEventHandler<ModalSubmittedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly IRandomMapExt _randomMap;
        private readonly IMapBanExt _banMap;
        private readonly TournamentManager _tournamentManager;

        public Event_Modal(OngoingRounds roundsHolder, IRandomMapExt randomMap, IMapBanExt banMap)
        {
            _roundsHolder = roundsHolder;
            _randomMap = randomMap;
            _banMap = banMap;
            _tournamentManager = new TournamentManager(roundsHolder);
        }

        public async Task HandleEventAsync(DiscordClient sender, ModalSubmittedEventArgs modal)
        {
            // Handle tournament creation modal
            if (modal.Interaction.Data.CustomId.StartsWith("tournament_create_modal_"))
            {
                await HandleTournamentCreateModal(sender, modal);
                return;
            }

            // Handle deck submission modal
            string deck = modal.Values["deck_code"];
            string response;

            await modal.Interaction.DeferAsync();

            if (_roundsHolder.TourneyRounds.Count == 0)
            {
                var user = modal.Interaction.User;
                var round = _roundsHolder.RegularRounds.Where(p => p.Player1 == user || p.Player2 == user).FirstOrDefault();

                if (round is null)
                {
                    response = $"{user.Username} is not a participant of this round";
                    var messageBuilder = new DiscordFollowupMessageBuilder().WithContent(response);
                    await modal.Interaction.CreateFollowupMessageAsync(messageBuilder);
                }
                else
                {
                    if (round.Player1 == user)
                        round.Deck1 = deck;
                    else
                        round.Deck2 = deck;

                    response = $"Deck of {modal.Interaction.User.Username} has been submitted";
                    var builder = new DiscordFollowupMessageBuilder().WithContent(response);
                    var log = await modal.Interaction.CreateFollowupMessageAsync(builder);
                    round.Messages.Add(log);

                    if (!String.IsNullOrEmpty(round.Deck1) && !String.IsNullOrEmpty(round.Deck2))
                    {
                        // Create dropdown for winner selection
                        var options = new List<DiscordSelectComponentOption>()
                        {
                            new(round.Player1?.Username ?? "Player 1", round.Player1?.Username ?? "Player 1"),
                            new(round.Player2?.Username ?? "Player 2", round.Player2?.Username ?? "Player 2"),
                        };
                        DiscordSelectComponent dropdown = new("1v1_winner_dropdown", "Select a winner", options, false, 1, 1);

                        // Clear previous messages
                        var channel = modal.Interaction.Channel;
                        foreach (var message in round.Messages)
                            await channel.DeleteMessageAsync(message);
                        round.Messages.Clear();

                        // Get the random map
                        var map = _randomMap.GetRandomMap();
                        if (map == null)
                        {
                            var followup = new DiscordFollowupMessageBuilder()
                                .WithContent("Decks have been submitted, but no maps found in the random pool")
                                .AddComponents(dropdown);
                            round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                            return;
                        }

                        // Create the embed
                        var embed = new DiscordEmbedBuilder { Title = map.Name };

                        // Handle map thumbnail if available
                        if (map.Thumbnail != null)
                        {
                            if (map.Thumbnail.StartsWith("http"))
                            {
                                // URL thumbnail - use directly
                                embed.ImageUrl = map.Thumbnail;
                                var followup = new DiscordFollowupMessageBuilder()
                                    .WithContent("Decks have been submitted")
                                    .AddEmbed(embed)
                                    .AddComponents(dropdown);
                                round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                            }
                            else
                            {
                                // Local file thumbnail - attach it
                                string relativePath = map.Thumbnail;
                                relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                                       .Replace('/', Path.DirectorySeparatorChar);

                                string baseDirectory = Directory.GetCurrentDirectory();
                                string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

                                Console.WriteLine($"Attempting to access map image at: {fullPath}");

                                if (!File.Exists(fullPath))
                                {
                                    // File not found, send without image
                                    var followup = new DiscordFollowupMessageBuilder()
                                        .WithContent("Decks have been submitted but map image not found")
                                        .AddEmbed(embed)
                                        .AddComponents(dropdown);
                                    round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                                }
                                else
                                {
                                    // Create a followup with file attachment
                                    var followup = new DiscordFollowupMessageBuilder()
                                        .WithContent("Decks have been submitted")
                                        .AddEmbed(embed)
                                        .AddComponents(dropdown);

                                    using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                                    {
                                        string fileName = Path.GetFileName(fullPath);
                                        followup.AddFile(fileName, fileStream);
                                        round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                                    }
                                }
                            }
                        }
                        else
                        {
                            // No thumbnail
                            var followup = new DiscordFollowupMessageBuilder()
                                .WithContent("Decks have been submitted")
                                .AddEmbed(embed)
                                .AddComponents(dropdown);
                            round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                        }
                    }
                }
            }
            else
            {
                Round? tourneyRound = _roundsHolder.TourneyRounds.Where(r => r is not null && r.Teams is not null &&
                    r.Teams.Any(t => t is not null && t.Participants is not null &&
                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == modal.Interaction.User.Id))).FirstOrDefault();
                if (tourneyRound is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Could not find your tournament round"));
                    return;
                }
                var teams = tourneyRound.Teams;

                var team1 = tourneyRound.Teams?.Where(t => t is not null && t.Participants is not null &&
                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == modal.Interaction.User.Id)).FirstOrDefault();
                if (team1 is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Could not find your team"));
                    return;
                }

                var team2 = tourneyRound.Teams?.FirstOrDefault(t => t is not null && t != team1);
                if (team2 is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Could not find opponent team"));
                    return;
                }

                var participant = team1.Participants?.Where(p => p is not null && p.Player is not null && p.Player.Id == modal.Interaction.User.Id).FirstOrDefault();
                if (participant is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Could not find your participant data"));
                    return;
                }

                if (ConfigManager.Config?.Servers is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Server configuration is missing"));
                    return;
                }

                var server = ConfigManager.Config.Servers.FirstOrDefault(s => s is not null && s.ServerId == modal.Interaction.GuildId);
                if (server is null || !server.BotChannelId.HasValue)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Server or bot channel configuration is missing"));
                    return;
                }

                if (modal.Interaction.Guild is null)
                {
                    await modal.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Guild information is missing"));
                    return;
                }

                var tChannel = await modal.Interaction.Guild.GetChannelAsync((ulong)server.BotChannelId);

                participant.Deck = deck;
                var member = (DiscordMember)modal.Interaction.User;

                response = $"Deck code of {member.DisplayName} has been submitted";
                tourneyRound.MsgToDel.Add(await sender.SendMessageAsync(tChannel, response));

                if (teams is not null && teams.All(t => t is not null && t.Participants is not null &&
                    t.Participants.All(p => p is not null && !string.IsNullOrEmpty(p.Deck))))
                {
                    tourneyRound.InGame = true;
                    List<string> maps = tourneyRound.Maps ?? new List<string>();

                    if (tourneyRound.Cycle == 0)
                    {
                        switch (tourneyRound.Length) // To rewrite as suggested?
                        {
                            case 5:
                                var generatedMaps = _banMap.GenerateMapListBo5(tourneyRound.OneVOne,
                                    team1.MapBans ?? new List<string>(),
                                    team2.MapBans ?? new List<string>());
                                maps = tourneyRound.Maps = generatedMaps ?? new List<string>();
                                break;
                            default:
                                var defaultMaps = _banMap.GenerateMapListBo3(tourneyRound.OneVOne,
                                    team1.MapBans ?? new List<string>(),
                                    team2.MapBans ?? new List<string>());
                                maps = tourneyRound.Maps = defaultMaps ?? new List<string>();
                                break;
                        }
                    }

                    var options = new List<DiscordSelectComponentOption>() { new($"{teams.First().Name}", $"{teams.First().Name}"), new($"{teams.Last().Name}", $"{teams.Last().Name}") };
                    var dropdown = new DiscordSelectComponent("winner_dropdown", "Select a winner", options, false, 1, 1);

                    // Get the current map name from the map list
                    string currentMapName = maps[tourneyRound.Cycle];

                    // Find the actual map object from Maps.MapCollection
                    var currentMap = Data.Maps.MapCollection?.FirstOrDefault(m => m.Name == currentMapName);

                    // Create embed with map details
                    var embed = new DiscordEmbedBuilder
                    {
                        Title = currentMapName
                    };

                    // Build message content
                    string messageContent = $"{tourneyRound.Pings} \nAll decks have been submitted. \nThe map for Game {tourneyRound.Cycle + 1} is: **{currentMapName}**\nSelect a winner below after the game";

                    await tChannel.DeleteMessagesAsync(tourneyRound.MsgToDel);
                    tourneyRound.MsgToDel.Clear();

                    // Handle map thumbnail if available
                    if (currentMap?.Thumbnail != null)
                    {
                        if (currentMap.Thumbnail.StartsWith("http"))
                        {
                            // URL thumbnail - use directly
                            embed.ImageUrl = currentMap.Thumbnail;
                            var builder = new DiscordMessageBuilder()
                                .WithContent(messageContent)
                                .AddEmbed(embed)
                                .AddComponents(dropdown);

                            await sender.SendMessageAsync(tChannel, builder);
                        }
                        else
                        {
                            // Local file thumbnail - attach it
                            string relativePath = currentMap.Thumbnail;
                            relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                                   .Replace('/', Path.DirectorySeparatorChar);

                            string baseDirectory = Directory.GetCurrentDirectory();
                            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

                            Console.WriteLine($"Attempting to access tournament map image at: {fullPath}");

                            if (!File.Exists(fullPath))
                            {
                                // File not found, send without image
                                var builder = new DiscordMessageBuilder()
                                    .WithContent(messageContent)
                                    .AddEmbed(embed)
                                    .AddComponents(dropdown);

                                await sender.SendMessageAsync(tChannel, builder);
                            }
                            else
                            {
                                // Create a message with file attachment
                                var builder = new DiscordMessageBuilder()
                                    .WithContent(messageContent)
                                    .AddEmbed(embed)
                                    .AddComponents(dropdown);

                                using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                                {
                                    string fileName = Path.GetFileName(fullPath);
                                    builder.AddFile(fileName, fileStream);
                                    await sender.SendMessageAsync(tChannel, builder);
                                }
                            }
                        }
                    }
                    else
                    {
                        // No thumbnail available
                        var builder = new DiscordMessageBuilder()
                            .WithContent(messageContent)
                            .AddEmbed(embed)
                            .AddComponents(dropdown);

                        await sender.SendMessageAsync(tChannel, builder);
                    }
                }
                var tFollowup = new DiscordFollowupMessageBuilder().WithContent(response); // To edit?
                await modal.Interaction.CreateFollowupMessageAsync(tFollowup);

                await Console.Out.WriteLineAsync(response);
            }
        }

        private async Task HandleTournamentCreateModal(DiscordClient sender, ModalSubmittedEventArgs modal)
        {
            try
            {
                await modal.Interaction.DeferAsync();

                // Parse custom ID to get tournament name and format
                string customId = modal.Interaction.Data.CustomId;
                string[] parts = customId.Split('_');

                if (parts.Length < 5)
                {
                    await modal.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Invalid tournament data. Please try again."));
                    return;
                }

                // Get tournament name and format from custom ID
                string tournamentName = parts[^2]; // Second last part
                tournamentName = tournamentName.Replace("-", "_"); // Convert back dashes to underscores
                string formatStr = parts[^1]; // Last part

                if (!Enum.TryParse<TournamentFormat>(formatStr, out TournamentFormat format))
                {
                    format = TournamentFormat.GroupStageWithPlayoffs; // Default format
                }

                // Get players from the modal input
                string playersInput = modal.Values["players"];

                if (string.IsNullOrWhiteSpace(playersInput))
                {
                    await modal.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("No players were specified. Please try again with at least 2 players."));
                    return;
                }

                // Parse player names - supporting both comma and newline separators
                var playerNames = playersInput
                    .Replace("\r", "")
                    .Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                if (playerNames.Count < 2)
                {
                    await modal.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("At least 2 players are required for a tournament."));
                    return;
                }

                // Convert player names to DiscordMember objects
                var players = new List<DiscordMember>();
                var guild = modal.Interaction.Guild;

                foreach (var playerName in playerNames)
                {
                    DiscordMember? member = null;

                    // Try to parse as mention/ID first
                    if (ulong.TryParse(playerName.Trim('<', '@', '>', '!'), out ulong userId))
                    {
                        try
                        {
                            member = await guild!.GetMemberAsync(userId);
                        }
                        catch
                        {
                            // User ID not found, continue to next check
                        }
                    }

                    // If not found by ID, try by name
                    if (member is null)
                    {
                        // Get all members manually using the guild's REST API
                        var membersList = guild?.Members.Values.ToList();

                        // Now search the list
                        member = membersList?.FirstOrDefault(m =>
                            (m.Username != null && m.Username.Equals(playerName, StringComparison.OrdinalIgnoreCase)) ||
                            (m.DisplayName != null && m.DisplayName.Equals(playerName, StringComparison.OrdinalIgnoreCase)) ||
                            (m.Nickname != null && m.Nickname.Equals(playerName, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (member is not null)
                    {
                        players.Add(member);
                    }
                    else
                    {
                        await modal.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                            .WithContent($"Could not find player: {playerName}"));
                        return;
                    }
                }

                // Create tournament
                var tournament = _tournamentManager.CreateTournament(
                    tournamentName,
                    players,
                    format,
                    modal.Interaction.Channel);

                // Generate and show standings
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);
                using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read);

                var responseBuilder = new DiscordFollowupMessageBuilder()
                    .WithContent($"Tournament '{tournamentName}' has been created with {players.Count} players!")
                    .AddFile("tournament.png", fs);

                await modal.Interaction.CreateFollowupMessageAsync(responseBuilder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tournament modal: {ex.Message}");

                try
                {
                    var messageBuilder = new DiscordFollowupMessageBuilder()
                        .WithContent($"Error creating tournament: {ex.Message}");
                    await modal.Interaction.CreateFollowupMessageAsync(messageBuilder);
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"Error sending error response: {innerEx.Message}");
                }
            }
        }
    }
}
