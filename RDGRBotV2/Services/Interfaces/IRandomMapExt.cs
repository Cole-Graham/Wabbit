using DSharpPlus.Entities;

namespace RDGRBotV2.Services.Interfaces
{
    public interface IRandomMapExt
    {
        DiscordEmbedBuilder GenerateRandomMap();
    }
}