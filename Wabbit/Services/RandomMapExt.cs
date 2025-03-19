using DSharpPlus.Entities;
using Wabbit.Data;
using Wabbit.Services.Interfaces;
using System.IO;
using Wabbit.Models;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of IRandomMapExt for casual map operations
    /// </summary>
    public class RandomMapExt : IRandomMapExt
    {
        private readonly IRandomProvider _randomProvider;
        private readonly IMapService _mapService;
        private readonly ILogger<RandomMapExt> _logger;

        /// <summary>
        /// Constructor with dependency injection
        /// </summary>
        public RandomMapExt(
            IRandomProvider randomProvider,
            IMapService mapService,
            ILogger<RandomMapExt> logger)
        {
            _randomProvider = randomProvider;
            _mapService = mapService;
            _logger = logger;
        }

        /// <inheritdoc />
        public Map? GetRandomMap()
        {
            try
            {
                var maps = Maps.MapCollection?.Where(m => m.IsInRandomPool).ToList();
                if (maps is null || maps.Count == 0)
                {
                    _logger.LogWarning("No maps found in the random pool");
                    return null;
                }

                int mIndex = _randomProvider.Instance.Next(maps.Count);
                return maps.ElementAt(mIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random map for casual play");
                return null;
            }
        }

        /// <inheritdoc />
        public DiscordEmbedBuilder GenerateRandomMap()
        {
            try
            {
                var map = GetRandomMap();
                if (map == null)
                {
                    _logger.LogWarning("Failed to get a random map from the pool");
                    return new DiscordEmbedBuilder().WithTitle("No maps found in the random pool");
                }

                // Use the common MapService to create the embed
                return _mapService.CreateMapEmbed(map, "Random map from the casual pool");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating random map embed");
                return new DiscordEmbedBuilder().WithTitle("Error").WithDescription("An error occurred while generating a random map");
            }
        }

        /// <inheritdoc />
        public List<string> GetRandomMaps(bool oneVOne, int count)
        {
            try
            {
                string mapSize = oneVOne ? "1v1" : "2v2";

                var maps = Maps.MapCollection?
                    .Where(m => m.Size == mapSize && m.IsInRandomPool)
                    .Select(m => m.Name)
                    .ToList();

                if (maps == null || maps.Count == 0)
                {
                    _logger.LogWarning($"No {mapSize} maps found in the random pool");
                    return new List<string>();
                }

                // Ensure count is valid
                if (count <= 0)
                {
                    _logger.LogWarning($"Invalid map count requested: {count}. Using 1 instead.");
                    count = 1;
                }

                // Return random maps up to the requested count
                return maps.OrderBy(_ => _randomProvider.Instance.Next())
                          .Take(Math.Min(count, maps.Count))
                          .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting random maps for casual play");
                return new List<string>();
            }
        }
    }
}
