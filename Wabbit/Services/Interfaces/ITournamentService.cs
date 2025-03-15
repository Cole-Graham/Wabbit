using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Service for general tournament operations
    /// </summary>
    public interface ITournamentService
    {
        /// <summary>
        /// Creates a new tournament
        /// </summary>
        /// <param name="name">The tournament name</param>
        /// <param name="players">List of participants</param>
        /// <param name="format">Tournament format</param>
        /// <param name="announcementChannel">Discord channel for announcements</param>
        /// <param name="gameType">Game type</param>
        /// <param name="playerSeeds">Optional player seeding</param>
        /// <returns>The created tournament</returns>
        Task<Tournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            TournamentFormat format,
            DiscordChannel announcementChannel,
            GameType gameType = GameType.OneVsOne,
            Dictionary<DiscordMember, int>? playerSeeds = null);

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        /// <param name="name">The tournament name</param>
        /// <returns>The tournament if found, otherwise null</returns>
        Tournament? GetTournament(string name);

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        /// <returns>List of all tournaments</returns>
        List<Tournament> GetAllTournaments();

        /// <summary>
        /// Deletes a tournament
        /// </summary>
        /// <param name="name">Tournament name</param>
        /// <param name="client">Discord client</param>
        Task DeleteTournamentAsync(string name, DiscordClient? client = null);

        /// <summary>
        /// Posts tournament visualization to the announcement channel
        /// </summary>
        /// <param name="tournament">The tournament</param>
        /// <param name="client">Discord client</param>
        Task PostTournamentVisualizationAsync(Tournament tournament, DiscordClient client);

        /// <summary>
        /// Starts a tournament
        /// </summary>
        /// <param name="tournament">The tournament to start</param>
        /// <param name="client">Discord client</param>
        Task StartTournamentAsync(Tournament tournament, DiscordClient client);

        /// <summary>
        /// Archives threads for a completed match
        /// </summary>
        /// <param name="match">The completed match</param>
        /// <param name="client">Discord client</param>
        /// <param name="archiveDuration">Duration before thread is auto-archived (default: 24 hours)</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task ArchiveThreadsAsync(Tournament.Match match, DiscordClient client, TimeSpan? archiveDuration = null);

        /// <summary>
        /// Archives all threads for a completed tournament
        /// </summary>
        /// <param name="tournament">The completed tournament</param>
        /// <param name="client">Discord client</param>
        /// <param name="archiveDuration">Duration before threads are auto-archived (default: 24 hours)</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task ArchiveAllTournamentThreadsAsync(Tournament tournament, DiscordClient client, TimeSpan? archiveDuration = null);

        /// <summary>
        /// Updates the tournament display in Discord
        /// </summary>
        /// <param name="tournament">The tournament to update</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task UpdateTournamentDisplayAsync(Tournament tournament);
    }
}