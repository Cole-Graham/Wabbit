using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using RDGRBotV2.Misc;
using RDGRBotV2.Data;
using RDGRBotV2.Models;
using System.ComponentModel;

namespace RDGRBotV2.BotClient.Commands
{
    [Command("Tournament")]
    public class TournamentGroup(OngoingRounds ongoingRounds)
    {
        private readonly OngoingRounds _ongoingRounds = ongoingRounds;

        [Command("2v2")]
        [Description("Launch 2v2 tournament round")]
        public async Task Start2v2Round(CommandContext context, [Description("Round length")][SlashChoiceProvider<RoundLength>] int length,
            [Description("Player 1")] DiscordUser Player1, [Description("Player 2")] DiscordUser Player2,
            [Description("Player 3")] DiscordUser Player3, [Description("Player 4")] DiscordUser Player4)
        {
            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0) // Null ref safeguard
            {
                await context.EditResponseAsync("Map collection is empty. Aborting");
                return;
            }

            var channel = context.Channel;

            await context.DeferResponseAsync();

            // Length kvp

            string pings = $"{Player1.Mention} {Player2.Mention} {Player3.Mention} {Player4.Mention}";

            Round round = new()
            {
                Name = "Round", // Placeholder
                Length = length,
                OneVOne = false,
                Teams = [],
                Pings = pings
            };
            Round.Team team1 = new();
            Round.Team team2 = new();

            List<DiscordMember> players = [await context.Guild.GetMemberAsync(Player1.Id), await context.Guild.GetMemberAsync(Player2.Id), await context.Guild.GetMemberAsync(Player3.Id), await context.Guild.GetMemberAsync(Player4.Id),];
            foreach (var player in players)
            {
                Round.Participant participant = new() { Player = player };
                if (players.IndexOf(player) < 2)
                    team1.Participants.Add(participant);
                else
                    team2.Participants.Add(participant);
            }

            team1.Name = $"{players[0].DisplayName}/{players[1].DisplayName}";
            team2.Name = $"{players[2].DisplayName}/{players[3].DisplayName}";

            round.Teams.Add(team1);
            round.Teams.Add(team2);

            _ongoingRounds.TourneyRounds.Add(round);

            

            string?[] maps2v2 = Maps.MapCollection.Where(m => m.Size == "2v2").Select(m => m.Name).ToArray();

            var options = new List<DiscordSelectComponentOption>();
            foreach (var map in maps2v2)
            {
                var option = new DiscordSelectComponentOption(map, map);
                options.Add(option);
            }

            DiscordSelectComponent dropdown;
            string message;

            switch (round.Length)
            {
                case 3:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "Choose 3 maps to ban in order of priority. Only 2 maps from each team will be banned, leaving" +
                            " 4 remaining maps. One of the 3rd maps selected will be randomly banned in case both teams ban the same map." +
                            " You will not know which maps were banned by your opponent, and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "Choose 2 maps to ban in order of priority. Only 3 maps will be banned, leaving 5 remaining maps." +
                        "One of the 2nd maps selected by each team will be randomly banned. You will not know which maps were banned by your opponent," +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                default:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "Select 3 maps to ban";
                    break;
            }

            var dropdownBuilder = new DiscordMessageBuilder()
                .WithContent(message)
                .AddComponents(dropdown);

            foreach (var team in round.Teams)
            {
                var thread = await channel.CreateThreadAsync(team.Name, DiscordAutoArchiveDuration.Day, DiscordChannelType.PrivateThread);
                team.Thread = thread;

                await thread.SendMessageAsync(dropdownBuilder);
                foreach (var participant in team.Participants)
                    await thread.AddThreadMemberAsync(participant.Player);
            }
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round sequence commenced"));
        }

        [Command("1v1")]
        [Description("Launch 1v1 tournament round")]
        public async Task Start1v1Round(CommandContext context, [Description("Round length")][SlashChoiceProvider<RoundLength>] int length,
            [Description("Player 1")] DiscordUser Player1, [Description("Player 2")] DiscordUser Player2)
        {
            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0) // Null ref safeguard
            {
                await context.EditResponseAsync("Map collection is empty. Aborting");
                return;
            }

            var channel = context.Channel;

            await context.DeferResponseAsync();

            // Length kvp

            string pings = $"{Player1.Mention} {Player2.Mention}";

            Round round = new()
            {
                Name = "Round", // Placeholder
                Length = length,
                OneVOne = true,
                Teams = [],
                Pings = pings
            };
            Round.Team team1 = new();
            Round.Team team2 = new();

            List<DiscordMember> players = [await context.Guild.GetMemberAsync(Player1.Id), await context.Guild.GetMemberAsync(Player2.Id)];

            string?[] maps1v1 = Maps.MapCollection.Where(m => m.Size == "1v1").Select(m => m.Name).ToArray();

            var options = new List<DiscordSelectComponentOption>();
            foreach (var map in maps1v1)
            {
                var option = new DiscordSelectComponentOption(map, map);
                options.Add(option);
            }

            DiscordSelectComponent dropdown;
            string message;

            switch (round.Length)
            {
                case 3:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "Choose 3 maps to ban in order of priority. Only 2 maps from each team will be banned, leaving" +
                        " 4 remaining maps. One of the 3rd maps selected will be randomly banned in case both teams ban the same map." +
                        " You will not know which maps were banned by your opponent, and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "Choose 2 maps to ban in order of priority. Only 3 maps will be banned, leaving 5 remaining maps." +
                        "One of the 2nd maps selected by each team will be randomly banned. You will not know which maps were banned by your opponent," +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                default: // to edit
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "Select 3 maps to ban";
                    break;
            }

            var dropdownBuilder = new DiscordMessageBuilder()
                .WithContent(message)
                .AddComponents(dropdown);

            foreach (var player in players)
            {
                Round.Team team = new() { Name = player.DisplayName };
                Round.Participant participant = new() { Player = player };
                team.Participants.Add(participant);

                round.Teams.Add(team);

                var thread = await channel.CreateThreadAsync(player.DisplayName, DiscordAutoArchiveDuration.Day, DiscordChannelType.PrivateThread);
                team.Thread = thread;

                await thread.SendMessageAsync(dropdownBuilder);
                await thread.AddThreadMemberAsync(player);
            }

            _ongoingRounds.TourneyRounds.Add(round);

            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round sequence commenced"));
        }

        [Command("end_round")]
        [Description("Terminate launched round")]
        public async Task EndRound(CommandContext context, [Description("Participant")] DiscordUser Participant)
        {
            await context.DeleteResponseAsync();

            

            var round = _ongoingRounds.TourneyRounds.Where(t => t.Pings.Contains(Participant.Mention)).FirstOrDefault();
            if (round is not null)
            {
                if (round.MsgToDel.Count > 0)
                    foreach (var msg in round.MsgToDel)
                        await msg.DeleteAsync();

                foreach (var team in round.Teams)
                {
                    if (team.Thread is not null)
                        await team.Thread.DeleteAsync();
                }

                _ongoingRounds.TourneyRounds.Remove(round);
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round has been manually concluded"));
            }
            else
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round was not found or something went wrong"));
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
