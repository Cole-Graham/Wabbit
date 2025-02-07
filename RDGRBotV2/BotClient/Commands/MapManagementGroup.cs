using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using RDGRBotV2.Data;
using RDGRBotV2.Models;
using System.ComponentModel;

namespace RDGRBotV2.BotClient.Commands
{
    [Command("Map_management")]
    [RequirePermissions(DiscordPermission.Administrator)]
    public class MapManagementGroup
    {
        [Command("list_maps")]
        [Description("Print a list of registered maps")]
        public async Task ListMaps(CommandContext context)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0) // null ref safeguard
            {
                await context.EditResponseAsync("Map collection is empty. Check the file content");
                return;
            }
            
            if (Maps.MapCollection.Count > 8)
            {
                int embedCount = (int)Math.Ceiling(Maps.MapCollection.Count / 8.0);
                List<DiscordEmbed> embeds = [];
                int pos = 0;

                for (int i = 0; i < embedCount; i++)
                {
                    var embed = new DiscordEmbedBuilder();
                    if (i == 0)
                        embed.Title = "Maps";
                    
                    for (int j = 0; j < 8; j++)
                    {
                        if (pos < Maps.MapCollection.Count)
                        {
                            embed.AddField("Name", Maps.MapCollection[pos].Name, true);
                            embed.AddField("Size", Maps.MapCollection[pos].Size, true);

                            string boolConvert = Maps.MapCollection[pos].IsInRandomPool ? "Yes" : "No";
                            embed.AddField("In random pool", boolConvert, true);
                        }
                        else
                            break;
                    }
                    embeds.Add(embed);
                }

                foreach (var embed in embeds)
                {
                    if (embeds.IndexOf(embed) == 0)
                        await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                    else
                        await context.FollowupAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
            }
            else
            {
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "Maps"
                };

                foreach (var map in Maps.MapCollection)
                {
                    embed.AddField("Name", map.Name, true);
                    embed.AddField("Size", map.Size, true);

                    string boolConvert = map.IsInRandomPool ? "Yes" : "No";
                    embed.AddField("In random pool", boolConvert, true);
                }
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
        }

        [Command("add_map")]
        [Description("Add map to map list")]
        public async Task AddMap(CommandContext context, [Description("Map name")] string mapName, [Description("Map ID")] string id, [Description("Size")] [SlashChoiceProvider<MapSizeChoiceProvider>] string size, [Description("Include into random map pool?")] bool inRandom, [Description("Map image URL")] string? thumbnail = null)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection.Where(m => m.Id == id).Any())
            {
                await context.EditResponseAsync($"Map with ID {id} already added");
                return;
            }

            Map map = new()
            {
                Name = mapName,
                Id = id,
                Size = size,
                IsInRandomPool = inRandom
            };
            if (thumbnail is not null)
                map.Thumbnail = thumbnail;

            Maps.MapCollection.Add(map);

            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Map has been added");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection.Remove(map); // Revert
            }
        }

        [Command("edit_map")]
        [Description("Edit selected map")]
        public async Task RemoveMap(CommandContext context, [SlashAutoCompleteProvider<MapNameAutoCompleteProvider>] string? mapName, [Description("New name")] string? newName = null, [Description("Map ID")] string? id = null, [Description("Size")][SlashChoiceProvider<MapSizeChoiceProvider>] string? size = null, [Description("Include into random map pool?")] bool? inRandom = null)
        {
            await context.DeferResponseAsync();

            if (newName is null && id is null && size is null && inRandom is null)
            {
                await context.EditResponseAsync("No values to edit provided");
                return;
            }

            Map? map = Maps.MapCollection.Where(m => m.Name == mapName).FirstOrDefault();
            if (map == null)
            {
                await context.EditResponseAsync("Map was not found. Potential choice provider problem or invalid input");
                return;
            }

            if (newName is not null)
                map.Name = newName;
            if (id is not null)
                map.Id = id;
            if (size is not null)
                map.Size = size;
            if (inRandom is not null)
                map.IsInRandomPool = (bool)inRandom;

            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Changes saved");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection.Add(map); // Revert
            }
        }

        [Command("remove_map")]
        [Description("Remove map from map list")]
        public async Task RemoveMap(CommandContext context, [SlashAutoCompleteProvider<MapNameAutoCompleteProvider>] string? mapName)
        {
            await context.DeferResponseAsync();

            Map? map = Maps.MapCollection.Where(m => m.Name == mapName).FirstOrDefault();
            if (map == null)
            {
                await context.EditResponseAsync("Map was not found. Potential choice provider problem or invalid input");
                return;
            }

            Maps.MapCollection.Remove(map);
            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Map has been removed");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection.Add(map); // Revert
            }
        }

        [Command("remove_all")]
        [Description("Clear map list")]
        public async Task RemoveAllMaps(CommandContext context)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection is not null && Maps.MapCollection.Count > 0)
            {
                List<Map> backup = new List<Map>(Maps.MapCollection); // PH

                Maps.MapCollection.Clear();
                (bool saved, string? error) = await Maps.SaveMaps();

                if (saved == true)
                    await context.EditResponseAsync("Map list has been cleared. Make sure to repopulate the list before launching any other commands");
                else
                {
                    await context.EditResponseAsync($"Operation failed: {error}");
                    Maps.MapCollection.AddRange(backup);
                }
            }
            else
            {
                await context.EditResponseAsync("Map list doesn't contain any values");
            }
        }



        #region Service

        private class MapSizeChoiceProvider : IChoiceProvider
        {
            private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> sizes =
                [
                    new DiscordApplicationCommandOptionChoice("1v1", "1v1"),
                    new DiscordApplicationCommandOptionChoice("2v2", "2v2")
                ];

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
                ValueTask.FromResult(sizes);
        }

        private class MapNameChoiceProvider : IChoiceProvider
        {
            private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> names = Maps.MapCollection.Select(m => new DiscordApplicationCommandOptionChoice(m.Name, m.Name));

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
                ValueTask.FromResult(names);
        }

        private class MapNameAutoCompleteProvider : IAutoCompleteProvider
        {
            private static readonly IEnumerable<DiscordAutoCompleteChoice> names = Maps.MapCollection.Select(m => new DiscordAutoCompleteChoice(m.Name, m.Name));

            public ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context) =>
                ValueTask.FromResult(names);
        }



        #endregion
    }
}
