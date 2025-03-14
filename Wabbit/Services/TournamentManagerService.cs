using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Misc;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Main service class for tournament management operations
    /// </summary>
    public class TournamentManagerService : ITournamentManagerService
    {
        private readonly OngoingRounds _ongoingRounds;
        private readonly ITournamentRepositoryService _repositoryService;
        private readonly ITournamentSignupService _signupService;
        private readonly ITournamentMatchService _matchService;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentStateService _stateService;
        private readonly ILogger<TournamentManagerService> _logger;

        public TournamentManagerService(
            OngoingRounds ongoingRounds,
            ITournamentRepositoryService repositoryService,
            ITournamentSignupService signupService,
            ITournamentMatchService matchService,
            ITournamentGroupService groupService,
            ITournamentPlayoffService playoffService,
            ITournamentStateService stateService,
            ILogger<TournamentManagerService> logger)
        {
            _ongoingRounds = ongoingRounds;
            _repositoryService = repositoryService;
            _signupService = signupService;
            _matchService = matchService;
            _groupService = groupService;
            _playoffService = playoffService;
            _stateService = stateService;
            _logger = logger;

            // Initialize and load data
            _repositoryService.Initialize();
            _stateService.LinkRoundsToTournaments();
        }

        /// <summary>
        /// Creates a new tournament from a list of players
        /// </summary>
        public async Task<Tournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            TournamentFormat format,
            DiscordChannel announcementChannel,
            GameType gameType = GameType.OneVsOne,
            Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            // Create tournament with basic properties
            var tournament = new Tournament
            {
                Name = name,
                Format = format,
                GameType = gameType,
                AnnouncementChannel = announcementChannel
            };

            // Set up groups based on format and player count
            _groupService.CreateGroups(tournament, players, playerSeeds);

            // Add tournament to repository
            _repositoryService.AddTournament(tournament);
            await _repositoryService.SaveTournamentsAsync();

            return tournament;
        }

        /// <summary>
        /// Posts a visualization of the tournament state
        /// </summary>
        public async Task PostTournamentVisualizationAsync(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Posting visualization for tournament {tournament.Name}");

            if (tournament == null || tournament.AnnouncementChannel is null)
            {
                _logger.LogWarning("Cannot post tournament visualization: tournament or announcement channel is null");
                return;
            }

            try
            {
                // Generate the tournament standings image using the TournamentVisualization class
                string imagePath = await Misc.TournamentVisualization.GenerateStandingsImage(tournament, client, _stateService);
                _logger.LogInformation($"Generated tournament visualization image at {imagePath} for tournament {tournament.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error posting tournament visualization for {tournament.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        public Tournament? GetTournament(string name)
        {
            return _repositoryService.GetTournament(name);
        }

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        public List<Tournament> GetAllTournaments()
        {
            return _repositoryService.GetAllTournaments();
        }

        /// <summary>
        /// Deletes a tournament
        /// </summary>
        public async Task DeleteTournamentAsync(string name, DiscordClient? client = null)
        {
            await _repositoryService.DeleteTournamentAsync(name, client);
        }

        /// <summary>
        /// Updates a match result
        /// </summary>
        public async Task UpdateMatchResult(Tournament tournament, Tournament.Match match, DiscordMember winner, int winnerScore, int loserScore)
        {
            // Call match service to update the result
            await _matchService.UpdateMatchResultAsync(tournament, match, winner, winnerScore, loserScore);

            // Check for group completion
            if (match.Type == TournamentMatchType.GroupStage && match.Participants[0].SourceGroup != null)
            {
                _groupService.CheckGroupCompletion(match.Participants[0].SourceGroup!);

                // If all groups are completed, set up playoffs
                if (tournament.Groups.All(g => g.IsComplete) && tournament.CurrentStage == TournamentStage.Groups)
                {
                    _playoffService.SetupPlayoffs(tournament);
                }
            }

            // Save changes
            await _repositoryService.SaveTournamentsAsync();
        }

        /// <summary>
        /// Starts a match round
        /// </summary>
        public async Task StartMatchRoundAsync(Tournament tournament, Tournament.Match match, DiscordChannel channel, DiscordClient client)
        {
            // Get player members
            var player1 = _groupService.ConvertToDiscordMember(match.Participants[0].Player);
            var player2 = _groupService.ConvertToDiscordMember(match.Participants[1].Player);

            // Ensure both players are valid
            if (player1 is null || player2 is null)
            {
                _logger.LogError($"Cannot start match {match.Name}: One or both players could not be converted to DiscordMember");
                return;
            }

            // Call match service to start the round
            await _matchService.CreateAndStart1v1Match(
                tournament,
                match.Participants[0].SourceGroup,
                player1,
                player2,
                client,
                match.BestOf,
                match);
        }

        /// <summary>
        /// Creates a new tournament signup
        /// </summary>
        public TournamentSignup CreateSignup(
            string name,
            TournamentFormat format,
            DiscordUser creator,
            ulong signupChannelId,
            GameType gameType = GameType.OneVsOne,
            DateTime? scheduledStartTime = null)
        {
            return _signupService.CreateSignup(name, format, creator, signupChannelId, gameType, scheduledStartTime);
        }

        /// <summary>
        /// Gets a signup by name
        /// </summary>
        public TournamentSignup? GetSignup(string name)
        {
            return _signupService.GetSignup(name);
        }

        /// <summary>
        /// Gets all signups
        /// </summary>
        public List<TournamentSignup> GetAllSignups()
        {
            return _signupService.GetAllSignups();
        }

        /// <summary>
        /// Deletes a signup
        /// </summary>
        public async Task DeleteSignupAsync(string name, DiscordClient? client = null, bool preserveData = false)
        {
            await _signupService.DeleteSignupAsync(name, client, preserveData);
        }

        /// <summary>
        /// Gets the number of participants in a signup
        /// </summary>
        public int GetParticipantCount(TournamentSignup signup)
        {
            return _signupService.GetParticipantCount(signup);
        }

        /// <summary>
        /// Updates a signup
        /// </summary>
        public void UpdateSignup(TournamentSignup signup)
        {
            _signupService.UpdateSignup(signup);
        }

        /// <summary>
        /// Saves all tournament and signup data
        /// </summary>
        public async Task SaveAllDataAsync()
        {
            await _repositoryService.SaveTournamentsAsync();
            await _signupService.SaveSignupsAsync();
            await _stateService.SaveTournamentStateAsync();
        }

        /// <summary>
        /// Archives tournament data
        /// </summary>
        public async Task ArchiveTournamentDataAsync(string tournamentName, DiscordClient? client = null)
        {
            await _repositoryService.ArchiveTournamentDataAsync(tournamentName, client);
        }

        /// <summary>
        /// Repairs data files
        /// </summary>
        public async Task RepairDataFilesAsync(DiscordClient? client = null)
        {
            await _repositoryService.RepairDataFilesAsync(client);
        }

        /// <summary>
        /// Loads all participants for signups
        /// </summary>
        public async Task LoadAllParticipantsAsync(DiscordClient client)
        {
            await _signupService.LoadAllParticipantsAsync(client);
        }

        /// <summary>
        /// Gets a signup with participants loaded
        /// </summary>
        public async Task<TournamentSignup?> GetSignupWithParticipantsAsync(string name, DiscordClient client)
        {
            return await _signupService.GetSignupWithParticipantsAsync(name, client);
        }
    }
}