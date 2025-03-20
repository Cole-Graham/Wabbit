using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Test-specific adapter for TournamentMapService that adds methods needed for testing
    /// </summary>
    public class TestTournamentMapService : ITournamentMapService
    {
        private readonly TournamentMapService _mapService;
        private readonly ILogger<TournamentMapService> _logger;

        public TestTournamentMapService(ILogger<TournamentMapService> logger, IRandomProvider randomProvider, IMapService mapService)
        {
            _logger = logger;
            _mapService = new TournamentMapService(logger, randomProvider, mapService);
        }

        /// <summary>
        /// Gets the tournament map pool
        /// </summary>
        public List<string> GetTournamentMapPool(bool oneVOne)
        {
            return _mapService.GetTournamentMapPool(oneVOne);
        }

        /// <summary>
        /// Gets a random map
        /// </summary>
        public string GetRandomMap(bool oneVOne)
        {
            return _mapService.GetRandomMap(oneVOne);
        }

        /// <summary>
        /// Gets multiple random maps
        /// </summary>
        public List<string> GetRandomMaps(bool oneVOne, int count)
        {
            return _mapService.GetRandomMaps(oneVOne, count);
        }

        /// <summary>
        /// Processes map bans and generates a map list
        /// </summary>
        public List<string> GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength, Round? round = null)
        {
            return _mapService.GenerateMapList(oneVOne, team1Bans, team2Bans, matchLength, round);
        }

        /// <summary>
        /// Gets the list of available maps for the next game
        /// </summary>
        public List<string> GetAvailableMapsForNextGame(Round round)
        {
            return _mapService.GetAvailableMapsForNextGame(round);
        }

        /// <summary>
        /// Validates a map ban selection
        /// </summary>
        public (bool isValid, List<string> validatedBans, string? errorMessage) ValidateMapBans(List<string> mapBans, bool oneVOne)
        {
            return _mapService.ValidateMapBans(mapBans, oneVOne);
        }

        /// <summary>
        /// Generates a map list for a Bo1 match
        /// </summary>
        public List<string> GenerateMapListBo1(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null)
        {
            return _mapService.GenerateMapListBo1(oneVOne, team1Bans, team2Bans, customMapPool, round);
        }

        /// <summary>
        /// Generates a map list for a Bo3 match
        /// </summary>
        public List<string> GenerateMapListBo3(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null)
        {
            return _mapService.GenerateMapListBo3(oneVOne, team1Bans, team2Bans, customMapPool, round);
        }

        /// <summary>
        /// Generates a map list for a Bo5 match
        /// </summary>
        public List<string> GenerateMapListBo5(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null)
        {
            return _mapService.GenerateMapListBo5(oneVOne, team1Bans, team2Bans, customMapPool, round);
        }

        /// <summary>
        /// Gets a random map with visualization
        /// </summary>
        public (Map? map, DiscordEmbedBuilder embed) GetRandomMapWithVisualization()
        {
            return _mapService.GetRandomMapWithVisualization();
        }

        /// <summary>
        /// Gets a map by name
        /// </summary>
        public Map? GetMapByName(string mapName)
        {
            return _mapService.GetMapByName(mapName);
        }

        /// <summary>
        /// Gets map thumbnail data
        /// </summary>
        public Task<(string? url, byte[]? data)> GetMapThumbnailAsync(string mapName)
        {
            return _mapService.GetMapThumbnailAsync(mapName);
        }

        /// <summary>
        /// Gets a random map for the next game
        /// </summary>
        public string? GetRandomMapForNextGame(Round round)
        {
            return _mapService.GetRandomMapForNextGame(round);
        }

        /// <summary>
        /// Gets the number of map bans for a round based on match length
        /// This is the missing method from the actual implementation that our tests need
        /// </summary>
        public int GetMapBanCount(Round round)
        {
            // Based on reverse-engineering the actual implementation:
            // Bo5 matches have 2 bans, everything else has 3
            return round.Length == 5 ? 2 : 3;
        }

        /// <summary>
        /// Determines if a map ban is guaranteed or conditional
        /// Used for testing conditional map bans
        /// </summary>
        public bool IsGuaranteedBan(Round round, Round.Team team, int banPriority)
        {
            // Best of 1: All 3 bans are guaranteed
            if (round.Length == 1)
                return true;

            // Best of 3: Priority 0 and 1 (index 0 and 1) are guaranteed, Priority 2 (index 2) is conditional
            else if (round.Length == 3)
            {
                // Priority 0 and 1 (1st and 2nd) are guaranteed
                if (banPriority < 2)
                    return true;

                // Priority 2 (3rd) is conditional and depends on coinflip
                else if (banPriority == 2 && round.CoinflipPerformed)
                {
                    // If this team won the coinflip, their priority 3 ban is applied
                    return string.Equals(team.Name, round.CoinflipWinnerTeamName, System.StringComparison.OrdinalIgnoreCase);
                }
            }
            // Best of 5: Only Priority 0 (index 0) is guaranteed, Priority 1 (index 1) is conditional
            else if (round.Length == 5)
            {
                // Priority 0 (1st) is guaranteed
                if (banPriority == 0)
                    return true;

                // Priority 1 (2nd) is conditional and depends on coinflip
                else if (banPriority == 1 && round.CoinflipPerformed)
                {
                    // If this team won the coinflip, their priority 2 ban is applied
                    return string.Equals(team.Name, round.CoinflipWinnerTeamName, System.StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }
}