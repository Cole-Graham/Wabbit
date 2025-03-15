using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for tournament map operations
    /// </summary>
    public interface ITournamentMapService
    {
        /// <summary>
        /// Gets the tournament map pool
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <returns>A list of map names in the pool</returns>
        List<string> GetTournamentMapPool(bool oneVOne);

        /// <summary>
        /// Gets a random map for a tournament match
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <returns>A random map name</returns>
        string GetRandomMap(bool oneVOne);

        /// <summary>
        /// Gets multiple random maps for a tournament match
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <param name="count">Number of maps to get</param>
        /// <returns>A list of random map names</returns>
        List<string> GetRandomMaps(bool oneVOne, int count);

        /// <summary>
        /// Processes map bans from teams and generates a map list
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <param name="team1Bans">Maps banned by team 1</param>
        /// <param name="team2Bans">Maps banned by team 2</param>
        /// <param name="matchLength">Length of the match (number of maps needed)</param>
        /// <returns>A list of maps for the match</returns>
        List<string> GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength);

        /// <summary>
        /// Gets the list of available maps for the next game in a match
        /// </summary>
        /// <param name="round">The current round</param>
        /// <returns>A list of available map names</returns>
        List<string> GetAvailableMapsForNextGame(Round round);

        /// <summary>
        /// Validates a map ban selection to ensure it contains valid maps
        /// </summary>
        /// <param name="mapBans">The list of map bans to validate</param>
        /// <param name="oneVOne">Whether this is for a 1v1 match</param>
        /// <returns>A tuple containing (isValid, validatedBans, errorMessage)</returns>
        (bool isValid, List<string> validatedBans, string? errorMessage) ValidateMapBans(List<string> mapBans, bool oneVOne);
    }
}