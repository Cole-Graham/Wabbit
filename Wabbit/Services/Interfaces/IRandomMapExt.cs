using DSharpPlus.Entities;

namespace Wabbit.Services.Interfaces
{
    public interface IRandomMapExt
    {
        DiscordEmbedBuilder GenerateRandomMap();
    }
}