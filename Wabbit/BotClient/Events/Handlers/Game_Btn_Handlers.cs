using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus;
using System.Numerics;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;

namespace Wabbit.BotClient.Events.Handlers
{
    public class Game_Btn_Handlers(OngoingRounds roundsHolder, TournamentManager tournamentManager)
    {
        private readonly OngoingRounds _roundsHolder = roundsHolder;
        private readonly TournamentManager _tournamentManager = tournamentManager;

        private Round? tourneyRound;
        BotConfig.ServerConfig? server;
        ulong? deckChannelId;

        #region Tourney-related

        List<Round.Team>? teams;
        Round.Team? team1;
        Round.Team? team2;
        Round.Participant? participant;

        #endregion

        public async Task<bool> Init(ComponentInteractionCreatedEventArgs e)
        {
            bool initialized = false;

            tourneyRound = _roundsHolder.TourneyRounds.Where(t => t is not null && t.Teams is not null && t.Teams.Any(team => team is not null && team.Participants is not null &&
                        team.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id))).FirstOrDefault();

            server = ConfigManager.Config.Servers.Where(s => s.ServerId == e.Guild.Id).First();
            deckChannelId = server.DeckChannelId;

            if (tourneyRound is not null && tourneyRound.Teams != null)
            {
                teams = tourneyRound.Teams;

                team1 = teams.Where(t => t is not null && t.Participants is not null &&
                            t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id)).FirstOrDefault();

                if (team1 == null)
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Your team was not found"));
                    return initialized;
                }

                team2 = tourneyRound.Teams.Where(t => t is not null && t.Name is not null && t.Name != team1.Name).FirstOrDefault();
                if (team2 == null)
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Opponent team was not found"));
                    return initialized;
                }

                participant = team1.Participants?.Where(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id).FirstOrDefault();
                if (participant == null)
                {
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().WithContent("Your participant data was not found"));
                    return initialized;
                }
            }

            initialized = true;
            return initialized;
        }

        #region Tournament_Events

        public async Task HandleTournamentDeckButtonClick(ComponentInteractionCreatedEventArgs e)
        {
            if (await Init(e))
            {
                if (tourneyRound is not null)
                {
                    if (tourneyRound.InGame == true)
                    {
                        var response = new DiscordInteractionResponseBuilder().WithContent("Game is in progress. Deck submitting is disabled");
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, response);
                    }
                    else
                    {
                        var modal = new DiscordInteractionResponseBuilder()
                        {
                            Title = "Ernter a deck code",
                            CustomId = "deck_modal"
                        };
                        modal.AddComponents(new DiscordTextInputComponent("Deck code", "deck_code", required: true));

                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
                    }
                }
                else
                {
                    var followup = new DiscordFollowupMessageBuilder().WithContent($"You're not a participant of this round");
                    await e.Interaction.CreateFollowupMessageAsync(followup);
                }
            }
        }

        public async Task HandleTournamentWinnerDropdown(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            if (await Init(e) && teams is not null && tourneyRound is not null)
            {
                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                if (e.Values == null || e.Values.Length == 0)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("No winner selected"));
                    return;
                }

                var winner = teams.Where(t => t is not null && t.Name is not null && t.Name == e.Values[0]).FirstOrDefault();
                if (winner == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Winner team not found"));
                    return;
                }

                winner.Wins += 1;
                var loser = teams.Where(t => t is not null && t.Name is not null && t.Name != e.Values[0]).FirstOrDefault();
                if (loser == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Loser team not found"));
                    return;
                }

                tourneyRound.InGame = false;

                if (tourneyRound.Maps == null || tourneyRound.Cycle >= tourneyRound.Maps.Count)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Map data is missing or invalid"));
                    return;
                }

                var map = tourneyRound.Maps[tourneyRound.Cycle];
                tourneyRound.Cycle += 1;

                var embedResult = new DiscordEmbedBuilder()
                {
                    Title = $"**{tourneyRound.Name ?? "Round"}** - Game {tourneyRound.Cycle} Results"
                };
                embedResult.AddField("Map", map ?? "Unknown");

                foreach (var team in teams)
                    embedResult.AddField(team?.Name ?? "Unknown Team", team?.Wins.ToString() ?? "0");

                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embedResult));

                var embedDeck = new DiscordEmbedBuilder()
                {
                    Title = $"**{tourneyRound.Name ?? "Round"}**, Game {tourneyRound.Cycle}"
                };

                foreach (var team in teams)
                {
                    if (team is not null && team.Participants is not null)
                    {
                        foreach (var p in team.Participants)
                        {
                            if (p is not null && p.Player is not null)
                                embedDeck.AddField(p.Player.DisplayName ?? "Unknown Player", p.Deck ?? "No deck");
                        }
                    }
                }

                if (e.Guild is not null && deckChannelId.HasValue)
                {
                    var deckChannel = await e.Guild.GetChannelAsync((ulong)deckChannelId);
                    if (deckChannel is not null)
                        await sender.SendMessageAsync(deckChannel, embedDeck);
                }

                int scoreLimit;

                switch (tourneyRound.Length)
                {
                    default:
                        scoreLimit = 1;
                        break;
                    case 3:
                        scoreLimit = 2;
                        break;
                    case 5:
                        scoreLimit = 3;
                        break;
                }

                if (winner.Wins == scoreLimit)
                {
                    var followup = new DiscordFollowupMessageBuilder().WithContent("Round is concluded");
                    await e.Interaction.CreateFollowupMessageAsync(followup);

                    if (team1 is not null && team1.Thread is not null)
                        await team1.Thread.DeleteAsync();
                    if (team2 is not null && team2.Thread is not null)
                        await team2.Thread.DeleteAsync();

                    _roundsHolder.TourneyRounds?.Remove(tourneyRound);
                }
                else
                {
                    if (team1 is not null && team1.Thread is not null && team2 is not null && team2.Thread is not null)
                    {
                        List<DiscordThreadChannel> threads = [team1.Thread, team2.Thread];

                        if (team1.Participants != null)
                        {
                            foreach (var p in team1.Participants)
                            {
                                if (p is not null)
                                {
                                    p.Deck = null;
                                    if (p.Player is not null)
                                        await sender.SendMessageAsync(team1.Thread, $"{p.Player.Mention} Please submit your deck for Game {tourneyRound.Cycle + 1}");
                                }
                            }
                        }

                        if (team2.Participants != null)
                        {
                            foreach (var p in team2.Participants)
                            {
                                if (p is not null)
                                {
                                    p.Deck = null;
                                    if (p.Player is not null)
                                        await sender.SendMessageAsync(team2.Thread, $"{p.Player.Mention} Please submit your deck for Game {tourneyRound.Cycle + 1}");
                                }
                            }
                        }
                    }
                }

                // Track the played map
                string currentMapName = tourneyRound.Maps[tourneyRound.Cycle - 1];

                // Save tournament state to record the played map
                _tournamentManager.SaveTournamentState();
            }
        }

        public async Task HandleMapBanClick(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            if (await Init(e) && server is not null && tourneyRound is not null && teams is not null)
            {
                var teamForMapBan = teams.Where(t => t is not null && t.Participants is not null &&
                                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id)).FirstOrDefault();

                if (teamForMapBan == null)
                {
                    await sender.SendMessageAsync(e.Channel, $"{e.User.Mention} you're not allowed to interact with this component");
                }
                else
                {
                    await e.Interaction.DeferAsync();

                    if (e.Channel is not null)
                    {
                        var channel = await sender.GetChannelAsync(e.Channel.Id);
                        if (channel is not null && e.Message is not null)
                        {
                            var dropdown = await channel.GetMessageAsync(e.Message.Id);
                            if (dropdown is not null && e.Values is not null)
                            {
                                List<string> bannedMaps = e.Values.ToList();
                                teamForMapBan.MapBans = bannedMaps;

                                var btn = new DiscordButtonComponent(DiscordButtonStyle.Primary, $"btn_deck_{teams.IndexOf(teamForMapBan)}", "Deck");
                                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().AddComponents(btn).WithContent("Maps have been selected \nPress the button to submit your deck"));

                                if (e.Guild is not null && server.BotChannelId.HasValue)
                                {
                                    var botChannel = await e.Guild.GetChannelAsync((ulong)server.BotChannelId);
                                    if (botChannel is not null)
                                    {
                                        var msg = await sender.SendMessageAsync(botChannel, $"{teamForMapBan.Name ?? "A team"} has finished map ban procedure");
                                        if (msg is not null && tourneyRound.MsgToDel is not null)
                                            tourneyRound.MsgToDel.Add(msg);
                                    }
                                }

                                var embed = new DiscordEmbedBuilder()
                                {
                                    Title = "Map bans"
                                };

                                foreach (var m in bannedMaps)
                                    embed.AddField($"Map {bannedMaps.IndexOf(m) + 1}", m ?? "Unknown");

                                await dropdown.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
                            }
                        }
                    }
                }

                // Save tournament state
                _tournamentManager.SaveTournamentState();
            }
        }

        #endregion

        #region Non-Tournament_Events

        public async Task HandleRegularDeckButtonClick(ComponentInteractionCreatedEventArgs e)
        {
            if (await Init(e))
            {
                //await e.Interaction.DeferAsync();

                var modal = new DiscordInteractionResponseBuilder();
                modal.WithTitle("Enter a deck code")
                    .WithCustomId("deck_modal")
                    .AddComponents(new DiscordTextInputComponent("Deck code", "deck_code", required: true));

                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
            }
        }

        public async Task HandleRegular1v1WinnerDropdown(ComponentInteractionCreatedEventArgs e)
        {
            if (await Init(e))
            {
                await e.Interaction.DeferAsync();

                if (_roundsHolder.RegularRounds == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Regular rounds are not initialized"));
                    return;
                }

                var regularRound = _roundsHolder.RegularRounds.Where(p => p is not null && p.Player1 is not null && p.Player2 is not null &&
                    (p.Player1.Id == e.User.Id || p.Player2.Id == e.User.Id)).FirstOrDefault();

                if (regularRound == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent($"{e.User.Mention} you're not a participant of this game"));
                }
                else
                {
                    if (e.Values == null || e.Values.Length == 0)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("No winner selected"));
                        return;
                    }

                    if (regularRound.Player1 is null || regularRound.Player2 is null)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Round player object was null. Aborting"));
                        return;
                    }

                    var winner = e.Values[0];

                    var embed = new DiscordEmbedBuilder()
                    {
                        Title = "Deck codes"
                    };

                    if (regularRound.Player1 is not null)
                        embed.AddField(regularRound.Player1.Username ?? "Player 1", regularRound.Deck1 ?? "No deck");

                    if (regularRound.Player2 is not null)
                        embed.AddField(regularRound.Player2.Username ?? "Player 2", regularRound.Deck2 ?? "No deck");

                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent($"The winner of this round is {winner}").AddEmbed(embed));

                    if (regularRound.Messages is not null && regularRound.Messages.Any() && e.Channel is not null)
                    {
                        await e.Channel.DeleteMessageAsync(regularRound.Messages.First());
                        _roundsHolder.RegularRounds.Remove(regularRound);
                    }
                }
            }
        }

        #endregion
    }
}
