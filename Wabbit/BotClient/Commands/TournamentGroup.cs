using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus;
using Wabbit.Misc;
using Wabbit.Data;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbit.Services;

namespace Wabbit.BotClient.Commands
{
    [Command("Tournament")]
    public class TournamentGroup
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentGroup> _logger;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentService _tournamentService;
        private readonly ITournamentMatchService _tournamentMatchService;

        public TournamentGroup(
            OngoingRounds ongoingRounds,
            ILogger<TournamentGroup> logger,
            ITournamentStateService stateService,
            ITournamentService tournamentService,
            ITournamentMatchService tournamentMatchService)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;
            _stateService = stateService;
            _tournamentService = tournamentService;
            _tournamentMatchService = tournamentMatchService;
        }

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
            await _stateService.SaveTournamentStateAsync(context.Client);

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
                        "randomly before each game after deck codes have been locked in.\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                        "One of the 2nd priority maps selected by each team will be randomly banned. " +
                        "You will not know which maps were banned by your opponent, " +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                    break;
                default:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                    break;
            }

            var dropdownBuilder = new DiscordMessageBuilder()
                .WithContent(message)
                .AddComponents(dropdown);

            foreach (var team in round.Teams)
            {
                var thread = await DiscordUtilities.CreateThreadAsync(
                    channel,
                    team.Name ?? "Team Thread",
                    _logger,
                    DiscordChannelType.PrivateThread,
                    DiscordAutoArchiveDuration.Day);

                if (thread is not null)
                {
                    team.Thread = thread;
                    await thread.SendMessageAsync(dropdownBuilder);

                    foreach (var participant in team.Participants)
                        if (participant.Player is not null)
                            await thread.AddThreadMemberAsync(participant.Player);
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to create thread for team {team.Name}"));
                    return;
                }
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
                        "randomly before each game after deck codes have been locked in.\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                    break;
                case 5:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 2, 2);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                        "One of the 2nd priority maps selected by each team will be randomly banned. " +
                        "You will not know which maps were banned by your opponent, " +
                        "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                    break;
                default:
                    dropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", options, false, 3, 3);
                    message = "**Scroll to see all map options!**\n\n" +
                        "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                        "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
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

                var thread = await DiscordUtilities.CreateThreadAsync(
                    channel,
                    displayName,
                    _logger,
                    DiscordChannelType.PrivateThread,
                    DiscordAutoArchiveDuration.Day);

                if (thread is not null)
                {
                    team.Thread = thread;
                    await thread.SendMessageAsync(dropdownBuilder);

                    if (player is not null)
                        await thread.AddThreadMemberAsync(player);
                }
                else
                {
                    await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to create thread for player {displayName}"));
                    return;
                }
            }

            _ongoingRounds.TourneyRounds.Add(round);

            // Save tournament state
            await _stateService.SaveTournamentStateAsync(context.Client);

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
            await _stateService.SaveTournamentStateAsync(context.Client);
        }

        [Command("submit_deck")]
        [Description("Submit a deck code for your current tournament match")]
        public async Task SubmitDeck(
            CommandContext context,
            [Description("Your deck code")] string deckCode)
        {
            await context.DeferResponseAsync();

            try
            {
                _logger.LogInformation($"User {context.User.Username} (ID: {context.User.Id}) submitting deck code in channel {context.Channel.Name} (ID: {context.Channel.Id})");

                // Check if used in a private thread (tournament matches use private threads)
                if (context.Channel.Type != DiscordChannelType.PrivateThread)
                {
                    _logger.LogWarning($"Deck submission attempted in non-thread channel type: {context.Channel.Type}");
                    await context.EditResponseAsync("This command can only be used in tournament match threads.");
                    return;
                }

                // Find the tournament round for this thread
                var round = _ongoingRounds.TourneyRounds?.FirstOrDefault(r =>
                    r.Teams is not null &&
                    r.Teams.Any(t => t.Thread is not null && t.Thread.Id == context.Channel.Id));

                if (round is null)
                {
                    _logger.LogWarning($"No tournament round found for thread: {context.Channel.Id}");
                    await context.EditResponseAsync("No active tournament round found for this channel.");
                    return;
                }

                // Find the team associated with this thread
                var team = round.Teams?.FirstOrDefault(t => t.Thread is not null && t.Thread.Id == context.Channel.Id);
                if (team is null)
                {
                    _logger.LogWarning($"No team found for thread: {context.Channel.Id} in round: {round.Name}");
                    await context.EditResponseAsync("Could not find the team associated with this thread.");
                    return;
                }

                // Check if the user is a participant in this team
                var participant = team.Participants?.FirstOrDefault(p =>
                    p is not null && p.Player is not null && p.Player.Id == context.User.Id);

                if (participant is null)
                {
                    _logger.LogWarning($"User {context.User.Username} (ID: {context.User.Id}) is not a participant in team: {team.Name}");
                    await context.EditResponseAsync("You are not a participant in this tournament match.");
                    return;
                }

                // Check if a game is in progress
                if (round.InGame == true)
                {
                    _logger.LogWarning($"Deck submission attempted while game is in progress for round: {round.Name}");
                    await context.EditResponseAsync("Game is in progress. Deck submission is disabled.");
                    return;
                }

                // Store as temporary deck code
                participant.TempDeckCode = deckCode;
                _logger.LogInformation($"Temporary deck code stored for user {context.User.Username} (ID: {context.User.Id})");

                // Create confirmation message with buttons
                var confirmEmbed = new DiscordEmbedBuilder()
                    .WithTitle("Confirm Deck Code")
                    .WithDescription("Please review your deck code and confirm if it's correct:")
                    .AddField("Deck Code", deckCode)
                    .WithColor(DiscordColor.Orange);

                var confirmBtn = new DiscordButtonComponent(
                    DiscordButtonStyle.Success,
                    $"confirm_deck_{context.User.Id}",
                    "Confirm");

                var reviseBtn = new DiscordButtonComponent(
                    DiscordButtonStyle.Secondary,
                    $"revise_deck_{context.User.Id}",
                    "Revise");

                // Send the confirmation message
                await context.EditResponseAsync(
                    new DiscordWebhookBuilder()
                        .WithContent($"{context.User.Mention} Please review your deck code submission:")
                        .AddEmbed(confirmEmbed)
                        .AddComponents(confirmBtn, reviseBtn));

                // Save the tournament state
                await _stateService.SaveTournamentStateAsync(context.Client);
                _logger.LogInformation($"Tournament state saved after deck submission for user {context.User.Username} (ID: {context.User.Id})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error submitting deck code for user {context.User.Username} (ID: {context.User.Id})");
                await context.EditResponseAsync($"Error submitting deck: {ex.Message}");
            }
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
