using DSharpPlus.Entities;
using Wabbit.Models;
using System.Collections.Generic;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for casual/non-tournament random map operations
    /// This interface provides methods for selecting maps from the casual map pool
    /// </summary>
    public interface IRandomMapExt
    {
        /// <summary>
        /// Generates a random map embed for casual play
        /// </summary>
        /// <returns>A Discord embed builder with the random map details</returns>
        DiscordEmbedBuilder GenerateRandomMap();

        /// <summary>
        /// Gets a random map from the casual map pool
        /// Used for casual play and non-tournament matches
        /// </summary>
        /// <returns>A randomly selected Map object, or null if no maps are available</returns>
        Map? GetRandomMap();

        /// <summary>
        /// Gets multiple random maps for casual sequential games
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <param name="count">Number of maps to select</param>
        /// <returns>A list of random map names</returns>
        List<string> GetRandomMaps(bool oneVOne, int count);
    }
}