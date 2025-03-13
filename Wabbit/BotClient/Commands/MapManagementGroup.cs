using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Models;
using System.ComponentModel;
using System.IO;

namespace Wabbit.BotClient.Commands
{
    [Command("Map_management")]
    [RequirePermissions(DiscordPermission.ManageMessages)]
    public class MapManagementGroup
    {
        [Command("add_map")]
        [Description("Add map to map list")]
        public async Task AddMap(CommandContext context,
            [Description("Map name")] string mapName,
            [Description("Map ID")] string id,
            [Description("Size")][SlashChoiceProvider<MapSizeChoiceProvider>] string size,
            [Description("Include into random map pool?")] bool inRandom)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection?.Where(m => m.Id == id).Any() == true)
            {
                await context.EditResponseAsync($"Map with ID {id} already added");
                return;
            }

            Map map = new()
            {
                Name = mapName,
                Id = id,
                Size = size,
                IsInRandomPool = inRandom,
                Thumbnail = "Data/images/maps/default.jpg"
            };

            if (Maps.MapCollection is null)
            {
                Maps.MapCollection = new List<Map>();
            }
            Maps.MapCollection.Add(map);

            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Map has been added with default thumbnail. Administrators can manually update the thumbnail in the Maps.json configuration file if needed.");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection?.Remove(map); // Revert
            }
        }

        [Command("edit_map")]
        [Description("Edit selected map")]
        public async Task EditMap(CommandContext context,
            [SlashAutoCompleteProvider<MapNameAutoCompleteProvider>] string mapName,
            [Description("New name")] string newName = "",
            [Description("Map ID")] string id = "",
            [Description("Size")][SlashChoiceProvider<MapSizeChoiceProvider>] string size = "",
            [Description("Include into random map pool (true/false)")] int inRandomInt = -1)
        {
            await context.DeferResponseAsync();

            bool? inRandom = inRandomInt switch
            {
                1 => true,
                0 => false,
                _ => null
            };

            if (string.IsNullOrEmpty(newName) && string.IsNullOrEmpty(id) && string.IsNullOrEmpty(size) && inRandom is null)
            {
                await context.EditResponseAsync("No values to edit provided");
                return;
            }

            Map? map = Maps.MapCollection?.Where(m => m.Name == mapName).FirstOrDefault();
            if (map == null)
            {
                await context.EditResponseAsync("Map was not found. Potential choice provider problem or invalid input");
                return;
            }

            if (!string.IsNullOrEmpty(newName))
                map.Name = newName;
            if (!string.IsNullOrEmpty(id))
                map.Id = id;
            if (!string.IsNullOrEmpty(size))
                map.Size = size;
            if (inRandom is not null)
                map.IsInRandomPool = (bool)inRandom;

            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Changes saved");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection?.Add(map); // Revert
            }
        }

        [Command("remove_map")]
        [Description("Remove map from map list")]
        public async Task RemoveMap(CommandContext context,
            [SlashAutoCompleteProvider<MapNameAutoCompleteProvider>] string mapName)
        {
            await context.DeferResponseAsync();

            Map? map = Maps.MapCollection?.Where(m => m.Name == mapName).FirstOrDefault();
            if (map == null)
            {
                await context.EditResponseAsync("Map was not found. Potential choice provider problem or invalid input");
                return;
            }

            Maps.MapCollection?.Remove(map);
            (bool saved, string? error) = await Maps.SaveMaps();

            if (saved == true)
                await context.EditResponseAsync("Map has been removed");
            else
            {
                await context.EditResponseAsync($"Operation failed: {error}");
                Maps.MapCollection?.Add(map); // Revert
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
            private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> names = Maps.MapCollection?.Select(m => new DiscordApplicationCommandOptionChoice(m.Name, m.Name)) ?? Array.Empty<DiscordApplicationCommandOptionChoice>();

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
                ValueTask.FromResult(names);
        }

        private class MapNameAutoCompleteProvider : IAutoCompleteProvider
        {
            private static readonly IEnumerable<DiscordAutoCompleteChoice> names = Maps.MapCollection?.Select(m => new DiscordAutoCompleteChoice(m.Name, m.Name)) ?? Array.Empty<DiscordAutoCompleteChoice>();

            public ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context) =>
                ValueTask.FromResult(names);
        }



        #endregion
    }
}
