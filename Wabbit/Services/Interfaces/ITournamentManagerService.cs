using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Main interface for tournament management operations
    /// </summary>
    public interface ITournamentManagerService
    {
        /// <summary>
        /// Creates a new tournament from a list of players
        /// </summary>
        Task<Tournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            TournamentFormat format,
            DiscordChannel announcementChannel,
            GameType gameType = GameType.OneVsOne,
            Dictionary<DiscordMember, int>? playerSeeds = null);

        /// <summary>
        /// Posts a visualization of the tournament state
        /// </summary>
        Task PostTournamentVisualizationAsync(Tournament tournament, DiscordClient client);

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        Tournament? GetTournament(string name);

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        List<Tournament> GetAllTournaments();

        /// <summary>
        /// Deletes a tournament
        /// </summary>
        Task DeleteTournamentAsync(string name, DiscordClient? client = null);

        /// <summary>
        /// Updates a match result
        /// </summary>
        Task UpdateMatchResult(Tournament tournament, Tournament.Match match, DiscordMember winner, int winnerScore, int loserScore);

        /// <summary>
        /// Starts a match round
        /// </summary>
        Task StartMatchRoundAsync(Tournament tournament, Tournament.Match match, DiscordChannel channel, DiscordClient client);

        /// <summary>
        /// Creates a new tournament signup
        /// </summary>
        TournamentSignup CreateSignup(
            string name,
            TournamentFormat format,
            DiscordUser creator,
            ulong signupChannelId,
            GameType gameType = GameType.OneVsOne,
            DateTime? scheduledStartTime = null);

        /// <summary>
        /// Gets a signup by name
        /// </summary>
        TournamentSignup? GetSignup(string name);

        /// <summary>
        /// Gets all signups
        /// </summary>
        List<TournamentSignup> GetAllSignups();

        /// <summary>
        /// Deletes a signup
        /// </summary>
        Task DeleteSignupAsync(string name, DiscordClient? client = null, bool preserveData = false);

        /// <summary>
        /// Gets the number of participants in a signup
        /// </summary>
        int GetParticipantCount(TournamentSignup signup);

        /// <summary>
        /// Updates a signup
        /// </summary>
        void UpdateSignup(TournamentSignup signup);

        /// <summary>
        /// Saves all tournament and signup data
        /// </summary>
        Task SaveAllDataAsync();

        /// <summary>
        /// Archives tournament data
        /// </summary>
        Task ArchiveTournamentDataAsync(string tournamentName, DiscordClient? client = null);

        /// <summary>
        /// Repairs data files
        /// </summary>
        Task RepairDataFilesAsync(DiscordClient? client = null);

        /// <summary>
        /// Loads participant information for all signups
        /// </summary>
        Task LoadAllParticipantsAsync(DiscordClient client);

        /// <summary>
        /// Gets a signup with fully loaded participants
        /// </summary>
        Task<TournamentSignup?> GetSignupWithParticipantsAsync(string name, DiscordClient client);
    }
}