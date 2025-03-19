using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for tournament-specific map operations
    /// This interface provides methods for managing maps in tournament contexts,
    /// including map pools, map bans, and tournament-specific map selection rules.
    /// </summary>
    public interface ITournamentMapService
    {
        /// <summary>
        /// Gets the tournament map pool - maps specifically designated for tournament play
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <returns>A list of tournament-approved map names</returns>
        List<string> GetTournamentMapPool(bool oneVOne);

        /// <summary>
        /// Gets a random map for a tournament match from the tournament map pool
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <returns>A random tournament-approved map name</returns>
        string GetRandomMap(bool oneVOne);

        /// <summary>
        /// Gets multiple random maps for a tournament match from the tournament map pool
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <param name="count">Number of maps to get</param>
        /// <returns>A list of random tournament-approved map names</returns>
        List<string> GetRandomMaps(bool oneVOne, int count);

        /// <summary>
        /// Processes map bans from teams and generates a tournament map list based on match length
        /// Applies tournament-specific rules for conditional bans based on coinflip results
        /// </summary>
        /// <param name="oneVOne">True for 1v1 maps, false for 2v2+ maps</param>
        /// <param name="team1Bans">Maps banned by team 1</param>
        /// <param name="team2Bans">Maps banned by team 2</param>
        /// <param name="matchLength">Length of the match (number of maps needed)</param>
        /// <param name="round">Optional round information for conditional bans</param>
        /// <returns>A list of maps for the tournament match</returns>
        List<string> GenerateMapList(bool oneVOne, List<string> team1Bans, List<string> team2Bans, int matchLength, Round? round = null);

        /// <summary>
        /// Gets the list of available maps for the next game in a tournament match
        /// Takes into account team bans, round state, and previous map picks
        /// </summary>
        /// <param name="round">The current tournament round</param>
        /// <returns>A list of available tournament map names</returns>
        List<string> GetAvailableMapsForNextGame(Round round);

        /// <summary>
        /// Validates a map ban selection to ensure it contains valid tournament maps
        /// </summary>
        /// <param name="mapBans">The list of map bans to validate</param>
        /// <param name="oneVOne">Whether this is for a 1v1 match</param>
        /// <returns>A tuple containing (isValid, validatedBans, errorMessage)</returns>
        (bool isValid, List<string> validatedBans, string? errorMessage) ValidateMapBans(List<string> mapBans, bool oneVOne);

        /// <summary>
        /// Generates a map list for a Bo1 (Best of 1) tournament match
        /// </summary>
        List<string> GenerateMapListBo1(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null);

        /// <summary>
        /// Generates a map list for a Bo3 (Best of 3) tournament match
        /// </summary>
        List<string> GenerateMapListBo3(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null);

        /// <summary>
        /// Generates a map list for a Bo5 (Best of 5) tournament match
        /// </summary>
        List<string> GenerateMapListBo5(bool oneVOne, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null, Round? round = null);

        /// <summary>
        /// Gets a random map with visualization data for tournament display
        /// </summary>
        (Map? map, DiscordEmbedBuilder embed) GetRandomMapWithVisualization();

        /// <summary>
        /// Gets a map by name (delegates to common MapService)
        /// </summary>
        Map? GetMapByName(string mapName);

        /// <summary>
        /// Gets map thumbnail data (delegates to common MapService)
        /// </summary>
        Task<(string? url, byte[]? data)> GetMapThumbnailAsync(string mapName);

        /// <summary>
        /// Gets a random map for the next game in a tournament match considering banned and played maps
        /// Used to select the next map during an ongoing tournament match
        /// </summary>
        /// <param name="round">The current tournament round</param>
        /// <returns>A random map name, or null if no maps are available</returns>
        string? GetRandomMapForNextGame(Round round);
    }
}