using DSharpPlus.Commands;
using DSharpPlus.Entities;
using RDGRBotV2.Misc;
using RDGRBotV2.Models;
using RDGRBotV2.Services.Interfaces;
using System.ComponentModel;

namespace RDGRBotV2.BotClient.Commands
{
    [Command("General")]
    public class BasicGroup(IRandomMapExt randomMap, OngoingRounds roundsHolder)
    {
        private readonly IRandomMapExt _randomMap = randomMap;
        private readonly OngoingRounds _roundsHolder = roundsHolder;

        [Command("random_map")]
        [Description("Gives a random map 1v1 map")]
        public async Task Random1v1(CommandContext context)
        {
            await context.DeferResponseAsync();
            var embed = _randomMap.GenerateRandomMap();

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
        }

        [Command("regular_1v1")]
        [Description("Start a 1v1 round")]
        public async Task OneVsOneStart(CommandContext context, [Description("Select player 1")] DiscordUser Player1, [Description("Select player 2")] DiscordUser Player2)
        {
            await context.DeferResponseAsync();

            Regular1v1 round = new() { Player1 = Player1, Player2 = Player2 };

            var btn = new DiscordButtonComponent(DiscordButtonStyle.Primary, "btn_deck", "Deck");
            var message = await context.EditResponseAsync(new DiscordWebhookBuilder().AddComponents(btn).WithContent($"{Player1.Mention} {Player2.Mention} \nPress the button to submit your deck"));
            round.Messages.Add(message);
            _roundsHolder.RegularRounds.Add(round);
        }


    }
}
