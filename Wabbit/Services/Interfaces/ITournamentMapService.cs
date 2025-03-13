using System.Collections.Generic;
using System.Threading.Tasks;

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
    }
}