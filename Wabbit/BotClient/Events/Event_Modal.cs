using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.Text.RegularExpressions;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events
{
    public class Event_Modal : IEventHandler<ModalSubmittedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly IRandomMapExt _randomMap;
        private readonly IMapBanExt _banMap;
        private readonly TournamentManager _tournamentManager;

        public Event_Modal(OngoingRounds roundsHolder, IRandomMapExt randomMap, IMapBanExt banMap, TournamentManager tournamentManager)
        {
            _roundsHolder = roundsHolder;
            _randomMap = randomMap;
            _banMap = banMap;
            _tournamentManager = tournamentManager;
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

                    // Save state after regular round deck submission
                    _tournamentManager.SaveTournamentState();

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

                // Store the deck code
                participant.Deck = deck;

                // Also store in deck history with the current map if available
                if (tourneyRound.Maps != null && tourneyRound.Maps.Count > 0)
                {
                    // Get the current map based on the cycle
                    int mapIndex = Math.Min(tourneyRound.Cycle, tourneyRound.Maps.Count - 1);
                    if (mapIndex >= 0 && mapIndex < tourneyRound.Maps.Count)
                    {
                        string currentMap = tourneyRound.Maps[mapIndex];
                        participant.DeckHistory[currentMap] = deck;
                    }
                }

                var member = (DiscordMember)modal.Interaction.User;

                response = $"Deck code of {member.DisplayName} has been submitted";
                tourneyRound.MsgToDel.Add(await sender.SendMessageAsync(tChannel, response));

                // Save tournament state immediately after deck submission
                _tournamentManager.SaveTournamentState();

                if (teams is not null && teams.All(t => t is not null && t.Participants is not null &&
                    t.Participants.All(p => p is not null && !string.IsNullOrEmpty(p.Deck))))
                {
                    tourneyRound.InGame = true;

                    // Get tournament map pool if maps are not already set
                    if (tourneyRound.Maps == null || !tourneyRound.Maps.Any())
                    {
                        // Get tournament map pool
                        var tournamentMapPool = _tournamentManager.GetTournamentMapPool(tourneyRound.OneVOne);

                        // Filter out maps that have already been played
                        var playedMaps = new List<string>();
                        var activeRounds = _tournamentManager.ConvertRoundsToState(_roundsHolder.TourneyRounds);
                        foreach (var activeRound in activeRounds)
                        {
                            playedMaps.AddRange(activeRound.PlayedMaps);
                        }

                        // Remove played maps from the pool
                        var availableMaps = tournamentMapPool.Except(playedMaps).ToList();

                        // If we don't have enough maps, reset the played maps
                        if (availableMaps.Count < tourneyRound.Length)
                        {
                            Console.WriteLine($"Warning: Not enough unplayed maps in the pool. Resetting played maps.");
                            availableMaps = tournamentMapPool;
                        }

                        // Generate maps based on the round length and map bans
                        switch (tourneyRound.Length)
                        {
                            case 5:
                                tourneyRound.Maps = _banMap.GenerateMapListBo5(tourneyRound.OneVOne,
                                    team1.MapBans ?? new List<string>(),
                                    team2.MapBans ?? new List<string>(),
                                    availableMaps) ?? new List<string>();
                                break;
                            case 3:
                                tourneyRound.Maps = _banMap.GenerateMapListBo3(tourneyRound.OneVOne,
                                    team1.MapBans ?? new List<string>(),
                                    team2.MapBans ?? new List<string>(),
                                    availableMaps) ?? new List<string>();
                                break;
                            case 1:
                                tourneyRound.Maps = _banMap.GenerateMapListBo1(tourneyRound.OneVOne,
                                    team1.MapBans ?? new List<string>(),
                                    team2.MapBans ?? new List<string>(),
                                    availableMaps) ?? new List<string>();
                                break;
                            default:
                                tourneyRound.Maps = _randomMap.GetRandomMaps(tourneyRound.OneVOne, tourneyRound.Length);
                                break;
                        }
                    }

                    // Save tournament state
                    _tournamentManager.SaveTournamentState();

                    // Rest of the existing code...
                }
                var tFollowup = new DiscordFollowupMessageBuilder().WithContent(response); // To edit?
                await modal.Interaction.CreateFollowupMessageAsync(tFollowup);

                await Console.Out.WriteLineAsync(response);
            }

            // Save state after deck submission
            _tournamentManager.SaveTournamentState();
        }

        private async Task HandleTournamentCreateModal(DiscordClient sender, ModalSubmittedEventArgs modal)
        {
            await modal.Interaction.DeferAsync();

            try
            {
                // Extract tournament name from modal ID
                string tournamentName = modal.Interaction.Data.CustomId.Replace("tournament_create_modal_", "");

                // Extract players from the input
                string playersText = modal.Values["players"];

                // Extract user mentions using regex
                var userMentionRegex = new Regex(@"<@!?(\d+)>");
                var matches = userMentionRegex.Matches(playersText);

                List<DiscordMember> players = [];

                if (modal.Interaction.Guild is null)
                {
                    await modal.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder().WithContent("Guild information is missing."));
                    return;
                }

                // Get DiscordMember objects for all mentioned users
                foreach (Match match in matches)
                {
                    if (ulong.TryParse(match.Groups[1].Value, out ulong userId))
                    {
                        try
                        {
                            var member = await modal.Interaction.Guild.GetMemberAsync(userId);
                            if (member is not null)
                            {
                                players.Add(member);
                            }
                        }
                        catch
                        {
                            // User might not be in the server or other issue
                            continue;
                        }
                    }
                }

                if (players.Count < 3)
                {
                    await modal.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder().WithContent(
                            "At least 3 players are required to create a tournament. Please mention valid server members."));
                    return;
                }

                // Create the tournament
                var tournament = _tournamentManager.CreateTournament(
                    tournamentName,
                    players,
                    TournamentFormat.GroupStageWithPlayoffs,
                    modal.Interaction.Channel);

                // Add to ongoing tournaments
                _roundsHolder.Tournaments.Add(tournament);

                // Generate and send standings image
                string imagePath = TournamentVisualization.GenerateStandingsImage(tournament);

                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 Tournament Created: {tournament.Name}")
                    .WithDescription($"Format: Group Stage + Playoffs\nPlayers: {players.Count}\nGroups: {tournament.Groups.Count}")
                    .WithColor(DiscordColor.Green)
                    .WithFooter("Use /tournament_manager show_standings to view the current standings");

                // Send the tournament info with the standings image
                var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                var messageBuilder = new DiscordFollowupMessageBuilder()
                    .AddEmbed(embed)
                    .AddFile(Path.GetFileName(imagePath), fileStream);

                await modal.Interaction.CreateFollowupMessageAsync(messageBuilder);

                // Notify all players
                var notificationEmbed = new DiscordEmbedBuilder()
                    .WithTitle($"🏆 You've been added to tournament: {tournament.Name}")
                    .WithDescription("Check the tournament channel for details and your group assignments.")
                    .WithColor(DiscordColor.Green);

                foreach (var player in players)
                {
                    try
                    {
                        await player.SendMessageAsync(notificationEmbed);
                    }
                    catch
                    {
                        // Player may have DMs disabled, continue anyway
                    }
                }
            }
            catch (Exception ex)
            {
                await modal.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder().WithContent($"Failed to create tournament: {ex.Message}"));
            }
        }
    }
}

