using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using RDGRBotV2.BotClient.Config;
using RDGRBotV2.Misc;
using RDGRBotV2.Models;

namespace RDGRBotV2.BotClient.Events
{
    public class Event_Button(OngoingRounds roundsHolder) : IEventHandler<ComponentInteractionCreatedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder = roundsHolder;

        public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            List<Round> tourneyRounds = _roundsHolder.TourneyRounds;
            Round? round = tourneyRounds.Where(t => t.Teams.Any(t => t.Participants.Any(p => p.Player.Id == e.User.Id))).FirstOrDefault();
            var server = ConfigManager.Config.Servers.Where(s => s.ServerId == e.Guild.Id).First();
            var deckChannelId = server.DeckChannelId;

            if (round is not null)
            {
                List<Round.Team> teams = round.Teams;
                Round.Team team1 = round.Teams.Where(t => t.Participants.Any(p => p.Player.Id == e.User.Id)).First();
                Round.Team team2 = round.Teams.Where(t => t.Name != team1.Name).First();
                Round.Participant participant = team1.Participants.Where(p => p.Player.Id == e.User.Id).First();

                switch (e.Id)
                {
                    case "btn_deck_0":
                        if (round is not null)
                        {
                            if (round.InGame == true)
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
                        break;

                    case "btn_deck_1":
                        if (round is not null)
                        {
                            if (round.InGame == true)
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
                        break;

                    case "winner_dropdown":
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                        var winner = teams.Where(t => t.Name == e.Values[0]).First();
                        winner.Wins += 1;
                        var looser = teams.Where(t => t.Name != e.Values[0]).First();

                        round.InGame = false;

                        var map = round.Maps[round.Cycle];
                        round.Cycle += 1;

                        var embedResult = new DiscordEmbedBuilder()
                        {
                            Title = $"**{round.Name}** - Game {round.Cycle} Results"
                        };
                        embedResult.AddField("Map", map);

                        foreach (var team in teams)
                            embedResult.AddField(team.Name, team.Wins.ToString());

                        await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embedResult));

                        var embedDeck = new DiscordEmbedBuilder()
                        {
                            Title = $"**{round.Name}**, Game {round.Cycle}"
                        };
                        
                        foreach (var team in teams)
                        {
                            foreach (var p in team.Participants)
                                embedDeck.AddField(p.Player.DisplayName, p.Deck);
                        }

                        var deckChannel = await e.Guild.GetChannelAsync((ulong)deckChannelId);
                        await sender.SendMessageAsync(deckChannel, embedDeck);

                        int scoreLimit;

                        switch (round.Length)
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

                            await team1.Thread.DeleteAsync();
                            await team2.Thread.DeleteAsync();

                            _roundsHolder.TourneyRounds.Remove(round);
                        }
                        else
                        {
                            List<DiscordThreadChannel> threads = [team1.Thread, team2.Thread];

                            foreach (var p in team1.Participants)
                            {
                                p.Deck = null;
                                await sender.SendMessageAsync(team1.Thread, $"{p.Player.Mention} Please submit your deck for Game {round.Cycle + 1}");
                            }
                            foreach (var p in team2.Participants)
                            {
                                p.Deck = null;
                                await sender.SendMessageAsync(team2.Thread, $"{p.Player.Mention} Please submit your deck for Game {round.Cycle + 1}");
                            }
                        }
                        break;

                    case "map_ban_dropdown":
                        if (round is not null)
                        {
                            var team = teams.Where(t => t.Participants.Where(p => p.Player.Id == e.User.Id).Any()).FirstOrDefault();
                            if (team is null)
                                await sender.SendMessageAsync(e.Channel, $"{e.User.Mention} you're not allowed to interact with this component");
                            else
                            {
                                await e.Interaction.DeferAsync();
                                var channel = await sender.GetChannelAsync(e.Channel.Id);
                                var dropdown = await channel.GetMessageAsync(e.Message.Id);

                                List<string> bannedMaps = e.Values.ToList();
                                team.MapBans = bannedMaps;

                                var btn = new DiscordButtonComponent(DiscordButtonStyle.Primary, $"btn_deck_{teams.IndexOf(team)}", "Deck");
                                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().AddComponents(btn).WithContent("Maps have been selected \nPress the button to submit your deck"));

                                var botChannel = await e.Guild.GetChannelAsync((ulong)server.BotChannelId);
                                round.MsgToDel.Add(await sender.SendMessageAsync(botChannel, $"{team.Name} has finished map ban procedure"));

                                var embed = new DiscordEmbedBuilder()
                                {
                                    Title = "Map bans"
                                };

                                foreach (var m in bannedMaps)
                                    embed.AddField($"Map {bannedMaps.IndexOf(m) + 1}", m);

                                await dropdown.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
                            }
                        }
                        else
                            await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("You're not a participant of this round"));

                        break;
                }
            }
            else
            {
                switch (e.Id)
                {
                    case "btn_deck":
                        var modal = new DiscordInteractionResponseBuilder();
                        modal.WithTitle("Enter a deck code")
                            .WithCustomId("deck_modal")
                            .AddComponents(new DiscordTextInputComponent("Deck code", "deck_code", required: true));

                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
                        break;
                    case "1v1_winner_dropdown":
                        await e.Interaction.DeferAsync();
                        var regularRound = _roundsHolder.RegularRounds.Where(p => p.Player1 == e.User || p.Player2 == e.User).FirstOrDefault();
                        if (regularRound is null)
                        {
                            await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent($"{e.User.Mention} you're not a participant of this game"));
                        }
                        else
                        {
                            var winner = e.Values[0];
                            var embed = new DiscordEmbedBuilder()
                            {
                                Title = "Deck codes"
                            };
                            embed.AddField(regularRound.Player1.Username, regularRound.Deck1);
                            embed.AddField(regularRound.Player2.Username, regularRound.Deck2);
                            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent($"The winner of this round is {winner}").AddEmbed(embed));
                            await e.Channel.DeleteMessageAsync(regularRound.Messages.First());
                            _roundsHolder.RegularRounds.Remove(regularRound);
                        }
                        break;
                }
            }

            
        }
    }
}
