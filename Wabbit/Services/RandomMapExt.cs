using DSharpPlus.Entities;
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
                embed.ImageUrl = map.Thumbnail;

            return embed;
        }
    }
}
