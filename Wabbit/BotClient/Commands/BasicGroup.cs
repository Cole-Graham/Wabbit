using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.ComponentModel;
using System.IO;
using Wabbit.Data;
using System.Runtime.InteropServices;

namespace Wabbit.BotClient.Commands
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

            // Check if the embed contains a local image path in the footer
            if (embed.Footer != null && embed.Footer.Text != null && embed.Footer.Text.StartsWith("LOCAL_THUMBNAIL:"))
            {
                // Extract the file path from the footer
                string relativePath = embed.Footer.Text.Replace("LOCAL_THUMBNAIL:", "").Trim();

                // Get the base directory of the application
                string baseDirectory = Directory.GetCurrentDirectory();

                // Combine the base directory with the relative path
                string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

                // Log the path for debugging
                Console.WriteLine($"Attempting to access image at: {fullPath}");

                // Check if the file exists
                if (!File.Exists(fullPath))
                {
                    await context.EditResponseAsync($"Error: Image file not found at path: {fullPath}. Please check that the file exists and the path is correct in Maps.json.");
                    return;
                }

                // Clear the footer so it doesn't show in the message
                embed.Footer = null;

                // Create a webhook builder with the embed
                var webhookBuilder = new DiscordWebhookBuilder().AddEmbed(embed);

                // Add the file as an attachment
                using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                webhookBuilder.AddFile(Path.GetFileName(fullPath), fileStream);

                // Send the response with the file attachment
                await context.EditResponseAsync(webhookBuilder);
            }
            else
            {
                // Regular URL-based image, just send the embed
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
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

        [Command("list_maps")]
        [Description("Print a list of registered maps")]
        public async Task ListMaps(
            CommandContext context,
            [Description("Filter by size (e.g., 1v1 to 4v4, or 'all' for all sizes")][SlashChoiceProvider<MapSizeChoiceProvider>] string? size = "all",
            [Description("Show only maps in random pool")] bool? inRandomPool = null)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0) // null ref safeguard
            {
                await context.EditResponseAsync("Map collection is empty. Check the file content");
                return;
            }

            // Filter maps by size if specified
            var filteredMaps = Maps.MapCollection;

            if (!string.IsNullOrEmpty(size) && size != "all")
            {
                filteredMaps = filteredMaps.Where(m =>
                    string.Equals(m.Size, size, StringComparison.OrdinalIgnoreCase)).ToList();

                if (filteredMaps.Count == 0)
                {
                    await context.EditResponseAsync($"No maps found with size '{size}'. Available sizes: {string.Join(", ", Maps.MapCollection.Select(m => m.Size).Distinct())}");
                    return;
                }
            }

            // Filter by random pool status if specified
            if (inRandomPool.HasValue)
            {
                filteredMaps = filteredMaps.Where(m => m.IsInRandomPool == inRandomPool.Value).ToList();

                if (filteredMaps.Count == 0)
                {
                    string status = inRandomPool.Value ? "in" : "not in";
                    await context.EditResponseAsync($"No maps found that are {status} the random pool.");
                    return;
                }
            }

            if (filteredMaps.Count > 8)
            {
                int embedCount = (int)Math.Ceiling(filteredMaps.Count / 8.0);
                List<DiscordEmbed> embeds = [];
                int pos = 0;

                for (int i = 0; i < embedCount; i++)
                {
                    var embed = new DiscordEmbedBuilder();
                    if (i == 0)
                    {
                        string title = "Maps";
                        if (!string.IsNullOrEmpty(size))
                            title += $" ({size})";
                        if (inRandomPool.HasValue)
                            title += inRandomPool.Value ? " (In Random Pool)" : " (Not In Random Pool)";
                        embed.Title = title;
                    }

                    for (int j = 0; j < 8; j++)
                    {
                        if (pos < filteredMaps.Count)
                        {
                            embed.AddField("Name", filteredMaps[pos].Name, true);
                            embed.AddField("Size", filteredMaps[pos].Size ?? "Unknown", true);

                            string boolConvert = filteredMaps[pos].IsInRandomPool ? "Yes" : "No";
                            embed.AddField("In random pool", boolConvert, true);

                            // Add thumbnail info
                            if (filteredMaps[pos].Thumbnail != null)
                            {
                                string thumbnailInfo = filteredMaps[pos].Thumbnail?.StartsWith("http") == true
                                    ? "URL"
                                    : "Local file";
                                embed.AddField("Thumbnail", thumbnailInfo, true);
                            }
                            else
                            {
                                embed.AddField("Thumbnail", "None", true);
                            }

                            pos++;
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
                var embed = new DiscordEmbedBuilder();
                string title = "Maps";
                if (!string.IsNullOrEmpty(size))
                    title += $" ({size})";
                if (inRandomPool.HasValue)
                    title += inRandomPool.Value ? " (In Random Pool)" : " (Not In Random Pool)";
                embed.Title = title;

                foreach (var map in filteredMaps)
                {
                    embed.AddField("Name", map.Name, true);
                    embed.AddField("Size", map.Size ?? "Unknown", true);

                    string boolConvert = map.IsInRandomPool ? "Yes" : "No";
                    embed.AddField("In random pool", boolConvert, true);
                }
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
        }

        [Command("show_map")]
        [Description("Shows details for a specific map")]
        public async Task ShowMap(CommandContext context, [Description("Map name")][SlashAutoCompleteProvider<MapNameAutoCompleteProvider>] string mapName)
        {
            await context.DeferResponseAsync();

            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0)
            {
                await context.EditResponseAsync("Map collection is empty. Check the file content");
                return;
            }

            // Find the map by name (case-insensitive)
            var map = Maps.MapCollection.FirstOrDefault(m =>
                string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));

            if (map == null)
            {
                await context.EditResponseAsync($"Map '{mapName}' not found. Use the list_maps command to see available maps.");
                return;
            }

            // Create an embed with the map details
            var embed = new DiscordEmbedBuilder
            {
                Title = map.Name,
                Description = $"ID: {map.Id ?? "Not specified"}"
            };

            embed.AddField("Size", map.Size ?? "Unknown", true);

            string inRandomPool = map.IsInRandomPool ? "Yes" : "No";
            embed.AddField("In random pool", inRandomPool, true);

            // Handle the thumbnail
            if (map.Thumbnail != null)
            {
                if (map.Thumbnail.StartsWith("http"))
                {
                    // It's a URL, use it directly
                    embed.ImageUrl = map.Thumbnail;
                    await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    // It's a local file, we need to attach it
                    string relativePath = map.Thumbnail;

                    // Normalize the path
                    relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);

                    // Get the base directory and full path
                    string baseDirectory = Directory.GetCurrentDirectory();
                    string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

                    Console.WriteLine($"Attempting to access image at: {fullPath}");

                    if (!File.Exists(fullPath))
                    {
                        // Image not found, but we can still show the map details
                        embed.AddField("Thumbnail", "Image file not found", true);
                        await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
                        return;
                    }

                    // Create a webhook builder with the embed
                    var webhookBuilder = new DiscordWebhookBuilder().AddEmbed(embed);

                    // Add the file as an attachment
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    webhookBuilder.AddFile(Path.GetFileName(fullPath), fileStream);

                    // Send the response with the file attachment
                    await context.EditResponseAsync(webhookBuilder);
                }
            }
            else
            {
                // No thumbnail
                embed.AddField("Thumbnail", "None", true);
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
        }
    }

    // Add these classes at the end of the file, outside the BasicGroup class but inside the namespace
    internal class MapNameAutoCompleteProvider : IAutoCompleteProvider
    {
        public ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
        {
            var choices = Maps.MapCollection?
                .Select(m => new DiscordAutoCompleteChoice(m.Name, m.Name))
                .ToList() ?? new List<DiscordAutoCompleteChoice>();

            return new ValueTask<IEnumerable<DiscordAutoCompleteChoice>>(choices);
        }
    }

    internal class MapSizeChoiceProvider : IChoiceProvider
    {
        private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> sizes =
            Maps.MapCollection?
                .Select(m => m.Size)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .Select(s => new DiscordApplicationCommandOptionChoice(s!, s!))
                .ToList() ??
            new List<DiscordApplicationCommandOptionChoice>
            {
                new DiscordApplicationCommandOptionChoice("1v1", "1v1"),
                new DiscordApplicationCommandOptionChoice("2v2", "2v2"),
                new DiscordApplicationCommandOptionChoice("3v3", "3v3"),
                new DiscordApplicationCommandOptionChoice("4v4", "4v4")
            };

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter) =>
            ValueTask.FromResult(sizes);
    }
}
