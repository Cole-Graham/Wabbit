using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using RDGRBotV2.BotClient.Config;
using RDGRBotV2.Misc;
using RDGRBotV2.Models;
using RDGRBotV2.Services.Interfaces;

namespace RDGRBotV2.BotClient.Events
{
    public class Event_Modal(OngoingRounds roundsHolder, IRandomMapExt randomMap, IMapBanExt banMap) : IEventHandler<ModalSubmittedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder = roundsHolder;
        private readonly IRandomMapExt _randomMap = randomMap;
        private readonly IMapBanExt _banMap = banMap;

        public async Task HandleEventAsync(DiscordClient sender, ModalSubmittedEventArgs modal)
        {
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
                        var embed = _randomMap.GenerateRandomMap();

                        var options = new List<DiscordSelectComponentOption>()
                        {
                            new(round.Player1.Username, round.Player1.Username),
                            new(round.Player2.Username, round.Player2.Username),
                        };

                        DiscordSelectComponent dropdown = new("1v1_winner_dropdown", "Select a winner", options, false, 1, 1);

                        var channel = modal.Interaction.Channel;
                        foreach (var message in round.Messages)
                            await channel.DeleteMessageAsync(message);
                        round.Messages.Clear();

                        var followup = new DiscordFollowupMessageBuilder().WithContent("Decks have been submitted").AddEmbed(embed).AddComponents(dropdown);
                        round.Messages.Add(await modal.Interaction.CreateFollowupMessageAsync(followup));
                    }
                }
            }
            else
            {
                Round tourneyRound = _roundsHolder.TourneyRounds.Where(r => r.Teams.Any(t => t.Participants.Any(p => p.Player.Id == modal.Interaction.User.Id))).FirstOrDefault();
                var teams = tourneyRound.Teams;

                var team1 = tourneyRound.Teams.Where(t => t.Participants.Any(p => p.Player.Id == modal.Interaction.User.Id)).First();
                var team2 = tourneyRound.Teams.First(t => t != team1);

                var participant = team1.Participants.Where(p => p.Player.Id == modal.Interaction.User.Id).First();

                var server = ConfigManager.Config.Servers.Where(s => s.ServerId == modal.Interaction.GuildId).First();
                var tChannel = await modal.Interaction.Guild.GetChannelAsync((ulong)server.BotChannelId);

                participant.Deck = deck;
                var member = (DiscordMember)modal.Interaction.User;

                response = $"Deck code of {member.DisplayName} has been submitted";
                tourneyRound.MsgToDel.Add(await sender.SendMessageAsync(tChannel, response));

                if (teams.All(t => t.Participants.All(p => !String.IsNullOrEmpty(p.Deck))))
                {
                    tourneyRound.InGame = true;
                    List<string> maps = tourneyRound.Maps;

                    if (tourneyRound.Cycle == 0)
                    {
                        switch (tourneyRound.Length) // To rewrite as suggested?
                        {
                            case 5:
                                maps = tourneyRound.Maps = _banMap.GenerateMapListBo5(tourneyRound.OneVOne, team1.MapBans, team2.MapBans);
                                break;
                            default:
                                maps = tourneyRound.Maps = _banMap.GenerateMapListBo3(tourneyRound.OneVOne, team1.MapBans, team2.MapBans);
                                break;
                        }
                    }

                    var options = new List<DiscordSelectComponentOption>() { new($"{teams.First().Name}", $"{teams.First().Name}"), new($"{teams.Last().Name}", $"{teams.Last().Name}") };
                    var dropdown = new DiscordSelectComponent("winner_dropdown", "Select a winner", options, false, 1, 1);
                    var builder = new DiscordMessageBuilder()
                        .WithContent($"{tourneyRound.Pings} \nAll decks have been submitted. \nThe map for Game {tourneyRound.Cycle + 1} is: **{maps[tourneyRound.Cycle]}** \nSelect a winner below after the game")
                        .AddComponents(dropdown);

                    await tChannel.DeleteMessagesAsync(tourneyRound.MsgToDel);
                    tourneyRound.MsgToDel.Clear();

                    await sender.SendMessageAsync(tChannel, builder);


                }
                var tFollowup = new DiscordFollowupMessageBuilder().WithContent(response); // To edit?
                await modal.Interaction.CreateFollowupMessageAsync(tFollowup);

                await Console.Out.WriteLineAsync(response);
            }
        }
    }
}
