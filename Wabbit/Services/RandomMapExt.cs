﻿using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Services.Interfaces;

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
                    // For local files, we need to handle this differently
                    // We'll store the path and handle the file attachment when sending the message
                    // This will be done in the command handler
                    embed.Description = $"Local image: {map.Thumbnail}";
                }
            }

            return embed;
        }
    }
}
