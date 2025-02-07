using DSharpPlus.Entities;
using RDGRBotV2.Data;
using RDGRBotV2.Services.Interfaces;

namespace RDGRBotV2.Services
{
    public class RandomMapExt(IRandomProvider random) : IRandomMapExt
    {
        private readonly Random _random = random.Instance;

        public DiscordEmbedBuilder GenerateRandomMap()
        {
            var maps = Maps.MapCollection.Where(m => m.IsInRandomPool == true).ToList();
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
