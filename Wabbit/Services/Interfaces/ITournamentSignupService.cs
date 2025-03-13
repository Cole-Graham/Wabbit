using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Wabbit.Models;

namespace Wabbit.Services.Interfaces
{
    /// <summary>
    /// Interface for tournament signup management
    /// </summary>
    public interface ITournamentSignupService
    {
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
        /// Loads participant information for a signup
        /// </summary>
        Task LoadParticipantsAsync(TournamentSignup signup, DiscordClient client, bool verbose = true);

        /// <summary>
        /// Loads participant information for all signups
        /// </summary>
        Task LoadAllParticipantsAsync(DiscordClient client);

        /// <summary>
        /// Gets a signup with fully loaded participants
        /// </summary>
        Task<TournamentSignup?> GetSignupWithParticipantsAsync(string name, DiscordClient client);

        /// <summary>
        /// Save signups to file
        /// </summary>
        Task SaveSignupsAsync();
    }
}