using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Services.Interfaces;
using System.IO;

namespace Wabbit.Services
{
    public class RandomMapExt(IRandomProvider random) : IRandomMapExt
    {
        private readonly Random _random = random.Instance;

        public DiscordEmbedBuilder GenerateRandomMap()
        {
            var maps = Maps.MapCollection?.Where(m => m.IsInRandomPool == true).ToList();
            if (maps is null || maps.Count == 0)
            {
                Console.WriteLine("No maps found in the random pool");
                return new DiscordEmbedBuilder().WithTitle("No maps found in the random pool");
            }
            int mIndex = _random.Next(maps.Count);
            var map = maps.ElementAt(mIndex);

            var embed = new DiscordEmbedBuilder
            {
                Title = map.Name,
            };
            if (map.Thumbnail is not null)
            {
                // Check if the thumbnail is a URL or a local file path
                if (map.Thumbnail.StartsWith("http"))
                {
                    // It's a URL, use it directly
                    embed.ImageUrl = map.Thumbnail;
                }
                else
                {
                    // For local files, ensure we're using a relative path
                    // We'll store the path and handle the file attachment when sending the message
                    string relativePath = map.Thumbnail;

                    // Normalize the path to ensure it uses the correct directory separators
                    relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);

                    // Store the relative path in a footer to be used when sending the message
                    embed.Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"LOCAL_THUMBNAIL:{relativePath}"
                    };

                    // Log for debugging
                    Console.WriteLine($"Using relative path for image: {relativePath}");
                }
            }

            return embed;
        }
    }
}
