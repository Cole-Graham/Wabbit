using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Data;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of ITournamentMapService for map operations
    /// </summary>
    public class TournamentMapService : ITournamentMapService
    {
        private readonly ILogger<TournamentMapService> _logger;
        private readonly Random _random;

        /// <summary>
        /// Constructor
        /// </summary>
        public TournamentMapService(
            ILogger<TournamentMapService> logger)
        {
            _logger = logger;
            _random = new Random();
        }

        /// <summary>
        /// Gets the tournament map pool
        /// </summary>
        public List<string> GetTournamentMapPool(bool oneVOne)
        {
            string mapSize = oneVOne ? "1v1" : "2v2";
            var mapPool = Maps.MapCollection?
                .Where(m => m.Size == mapSize && m.IsInTournamentPool)
                .Select(m => m.Name)
                .ToList() ?? new List<string>();

            _logger.LogInformation($"Retrieved tournament map pool for {mapSize}: {mapPool.Count} maps");
            return mapPool;
        }

        /// <summary>
        /// Gets a random map
        /// </summary>
        public string GetRandomMap(bool oneVOne)
        {
            var mapPool = GetTournamentMapPool(oneVOne);
            if (mapPool.Count == 0)
            {
                _logger.LogWarning("Map pool is empty");
                return "Default Map";
            }

            int randomIndex = _random.Next(mapPool.Count);
            return mapPool[randomIndex];
        }

        /// <summary>
        /// Gets multiple random maps
        /// </summary>
        public List<string> GetRandomMaps(bool oneVOne, int count)
        {
            var mapPool = GetTournamentMapPool(oneVOne);
            if (mapPool.Count == 0)
            {
                _logger.LogWarning("Map pool is empty");
                return new List<string> { "Default Map" };
            }

            // If count is greater than the map pool, use the entire pool
            if (count >= mapPool.Count)
            {
                return mapPool.ToList();
            }

            // Shuffle the map pool and take the first 'count' maps
            var shuffledMaps = mapPool.OrderBy(_ => _random.Next()).Take(count).ToList();
            return shuffledMaps;
        }

        /// <summary>
        /// Processes map bans and generates a map list
        /// </summary>
        public List<string> GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength)
        {
            var mapPool = GetTournamentMapPool(oneVOne);
            if (mapPool.Count == 0)
            {
                _logger.LogWarning("Map pool is empty");
                return Enumerable.Repeat("Default Map", matchLength).ToList();
            }

            // Process bans - remove banned maps from the pool
            var availableMaps = mapPool.ToList();

            // Process team 1 bans
            foreach (var ban in team1Bans)
            {
                availableMaps.Remove(ban);
            }

            // Process team 2 bans
            foreach (var ban in team2Bans)
            {
                availableMaps.Remove(ban);
            }

            // If no maps left after bans, use the original map pool with a warning
            if (availableMaps.Count == 0)
            {
                _logger.LogWarning("No maps left after bans, using original map pool");
                availableMaps = mapPool.ToList();
            }

            // If available maps less than match length, duplicate some maps
            while (availableMaps.Count < matchLength)
            {
                availableMaps.AddRange(availableMaps.Take(matchLength - availableMaps.Count));
            }

            // Shuffle and take required number of maps
            return availableMaps.OrderBy(_ => _random.Next()).Take(matchLength).ToList();
        }
    }
}