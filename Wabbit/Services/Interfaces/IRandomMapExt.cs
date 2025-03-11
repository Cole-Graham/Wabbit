using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    public interface IRandomMapExt
    {
        DiscordEmbedBuilder GenerateRandomMap();
        Map? GetRandomMap();
        List<string> GetRandomMaps(bool oneVOne, int count);
    }
}