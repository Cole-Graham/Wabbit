using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using Wabbit.Misc;
using Wabbit.Data;
using Wabbit.Models;
using System.ComponentModel;

namespace Wabbit.BotClient.Commands
{
    [Command("Tournament")]
    public class TournamentGroup(OngoingRounds ongoingRounds, TournamentManager tournamentManager)
    {
        private readonly OngoingRounds _ongoingRounds = ongoingRounds;
        private readonly TournamentManager _tournamentManager = tournamentManager;

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

            if (context.Guild is null)
            {
                await context.EditResponseAsync("Guild context is null");
                return;
            }

            // Length kvp

            string pings = $"{Player1.Mention} {Player2.Mention} {Player3.Mention} {Player4.Mention}";

            Round round = new()
            {
                Name = "Round", // Placeholder
                Length = length,
                OneVOne = false,
                Teams = new List<Round.Team>(),
                Pings = pings
            };
            Round.Team team1 = new();
            Round.Team team2 = new();

            var member1 = await context.Guild.GetMemberAsync(Player1.Id);
            var member2 = await context.Guild.GetMemberAsync(Player2.Id);
            var member3 = await context.Guild.GetMemberAsync(Player3.Id);
            var member4 = await context.Guild.GetMemberAsync(Player4.Id);

            if (member1 is null || member2 is null || member3 is null || member4 is null)
            {
                await context.EditResponseAsync("One or more players could not be found in the server");
                return;
            }

            List<DiscordMember> players = [member1, member2, member3, member4];
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

            // Save tournament state
            await _tournamentManager.SaveTournamentState(context.Client);

            string?[] maps2v2 = Maps.MapCollection?.Where(m => m.Size == "2v2").Select(m => m.Name).ToArray() ?? Array.Empty<string?>();

            var options = new List<DiscordSelectComponentOption>();
            foreach (var map in maps2v2)
            {
                if (map is not null)
                {
                    var option = new DiscordSelectComponentOption(map, map);
                    options.Add(option);
                }
            }

            DiscordSelectComponent dropdown;
            string message;

            switch (round.Length)
            {
                case 3:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 2 maps from each team will be banned, leaving 4 remaining maps. One of the 3rd priority maps " +
                        "selected will be randomly banned in case both teams ban the same map. " +
                        "You will not know which maps were banned by your opponent, and the remaining maps will be revealed " +
                        "randomly before each game after deck codes have been locked in.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                        "One of the 2nd priority maps selected by each team will be randomly banned. " +
                        "You will not know which maps were banned by your opponent, " +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                default:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!";
                    break;
            }

            var dropdownBuilder = new DiscordMessageBuilder()
                .WithContent(message)
                .AddComponents(dropdown);

            foreach (var team in round.Teams)
            {
                var thread = await channel.CreateThreadAsync(team.Name ?? "Team Thread", DiscordAutoArchiveDuration.Day, DiscordChannelType.PrivateThread);
                team.Thread = thread;

                await thread.SendMessageAsync(dropdownBuilder);
                foreach (var participant in team.Participants)
                    if (participant.Player is not null)
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

            if (context.Guild is null)
            {
                await context.EditResponseAsync("Guild context is null");
                return;
            }

            // Length kvp

            string pings = $"{Player1.Mention} {Player2.Mention}";

            Round round = new()
            {
                Name = "Round", // Placeholder
                Length = length,
                OneVOne = true,
                Teams = new List<Round.Team>(),
                Pings = pings
            };
            Round.Team team1 = new();
            Round.Team team2 = new();

            var member1 = await context.Guild.GetMemberAsync(Player1.Id);
            var member2 = await context.Guild.GetMemberAsync(Player2.Id);

            if (member1 is null || member2 is null)
            {
                await context.EditResponseAsync("One or more players could not be found in the server");
                return;
            }

            List<DiscordMember> players = [member1, member2];

            string?[] maps1v1 = Maps.MapCollection?.Where(m => m.Size == "1v1").Select(m => m.Name).ToArray() ?? Array.Empty<string?>();

            var options = new List<DiscordSelectComponentOption>();
            foreach (var map in maps1v1)
            {
                if (map is not null)
                {
                    var option = new DiscordSelectComponentOption(map, map);
                    options.Add(option);
                }
            }

            DiscordSelectComponent dropdown;
            string message;

            switch (round.Length)
            {
                case 3:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 2 maps from each team will be banned, leaving 4 remaining maps. One of the 3rd priority maps " +
                        "selected will be randomly banned in case both teams ban the same map. " +
                        "You will not know which maps were banned by your opponent, and the remaining maps will be revealed " +
                        "randomly before each game after deck codes have been locked in.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                        "One of the 2nd priority maps selected by each team will be randomly banned. " +
                        "You will not know which maps were banned by your opponent, " +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                    break;
                default:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!";
                    break;
            }

            var dropdownBuilder = new DiscordMessageBuilder()
                .WithContent(message)
                .AddComponents(dropdown);

            foreach (var player in players)
            {
                string displayName = player?.DisplayName ?? "Player";
                Round.Team team = new() { Name = displayName };
                Round.Participant participant = new() { Player = player };
                team.Participants.Add(participant);

                round.Teams.Add(team);

                var thread = await channel.CreateThreadAsync(displayName, DiscordAutoArchiveDuration.Day, DiscordChannelType.PrivateThread);
                team.Thread = thread;

                await thread.SendMessageAsync(dropdownBuilder);
                if (player is not null)
                    await thread.AddThreadMemberAsync(player);
            }

            _ongoingRounds.TourneyRounds.Add(round);

            // Save tournament state
            await _tournamentManager.SaveTournamentState(context.Client);

            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round sequence commenced"));
        }

        [Command("end_round")]
        [Description("Terminate launched round")]
        public async Task EndRound(CommandContext context, [Description("Participant")] DiscordUser Participant)
        {
            await context.DeleteResponseAsync();

            var round = _ongoingRounds.TourneyRounds?.Where(t => t.Pings != null && t.Pings.Contains(Participant.Mention)).FirstOrDefault();
            if (round is not null)
            {
                if (round.MsgToDel?.Count > 0)
                    foreach (var msg in round.MsgToDel)
                        await msg.DeleteAsync();

                if (round.Teams is not null)
                {
                    foreach (var team in round.Teams)
                    {
                        if (team.Thread is not null)
                            await team.Thread.DeleteAsync();
                    }
                }

                _ongoingRounds.TourneyRounds?.Remove(round);
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round has been manually concluded"));
            }
            else
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Round was not found or something went wrong"));

            // Save tournament state
            await _tournamentManager.SaveTournamentState(context.Client);
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
