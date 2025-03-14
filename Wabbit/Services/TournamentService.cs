using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;

namespace Wabbit.Services
{
    /// <summary>
    /// Implementation of ITournamentService for general tournament operations
    /// </summary>
    public class TournamentService : ITournamentService
    {
        private readonly ILogger<TournamentService> _logger;
        private readonly ITournamentManagerService _tournamentManagerService;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentStateService _stateService;

        /// <summary>
        /// Constructor
        /// </summary>
        public TournamentService(
            ILogger<TournamentService> logger,
            ITournamentManagerService tournamentManagerService,
            ITournamentGroupService groupService,
            ITournamentStateService stateService)
        {
            _logger = logger;
            _tournamentManagerService = tournamentManagerService;
            _groupService = groupService;
            _stateService = stateService;
        }

        /// <summary>
        /// Creates a new tournament
        /// </summary>
        public async Task<Tournament> CreateTournamentAsync(
            string name,
            List<DiscordMember> players,
            TournamentFormat format,
            DiscordChannel announcementChannel,
            GameType gameType = GameType.OneVsOne,
            Dictionary<DiscordMember, int>? playerSeeds = null)
        {
            _logger.LogInformation($"Creating tournament {name} with {players.Count} players");

            return await _tournamentManagerService.CreateTournamentAsync(
                name,
                players,
                format,
                announcementChannel,
                gameType,
                playerSeeds);
        }

        /// <summary>
        /// Gets a tournament by name
        /// </summary>
        public Tournament? GetTournament(string name)
        {
            return _tournamentManagerService.GetTournament(name);
        }

        /// <summary>
        /// Gets all tournaments
        /// </summary>
        public List<Tournament> GetAllTournaments()
        {
            return _tournamentManagerService.GetAllTournaments();
        }

        /// <summary>
        /// Deletes a tournament
        /// </summary>
        public async Task DeleteTournamentAsync(string name, DiscordClient? client = null)
        {
            _logger.LogInformation($"Deleting tournament {name}");
            await _tournamentManagerService.DeleteTournamentAsync(name, client);
        }

        /// <summary>
        /// Posts tournament visualization to the announcement channel
        /// </summary>
        public async Task PostTournamentVisualizationAsync(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Posting visualization for tournament {tournament.Name}");
            await _tournamentManagerService.PostTournamentVisualizationAsync(tournament, client);
        }

        /// <summary>
        /// Starts a tournament
        /// </summary>
        public async Task StartTournamentAsync(Tournament tournament, DiscordClient client)
        {
            _logger.LogInformation($"Starting tournament {tournament.Name}");

            // Create groups using the group service
            if (tournament.Format == TournamentFormat.GroupStageWithPlayoffs ||
                tournament.Format == TournamentFormat.RoundRobin)
            {
                // Get players from all groups
                List<DiscordMember> players = new List<DiscordMember>();

                // Extract players from all group participants
                foreach (var group in tournament.Groups)
                {
                    foreach (var participant in group.Participants)
                    {
                        if (participant.Player is DiscordMember member)
                        {
                            players.Add(member);
                        }
                    }
                }

                // Determine group count
                int groupCount = _groupService.DetermineGroupCount(players.Count, tournament.Format);

                // Create seeding dictionary from participant seed values
                Dictionary<DiscordMember, int>? playerSeeds = null;
                if (players.Count > 0)
                {
                    playerSeeds = new Dictionary<DiscordMember, int>();
                    foreach (var group in tournament.Groups)
                    {
                        foreach (var participant in group.Participants)
                        {
                            if (participant.Player is DiscordMember member && participant.Seed > 0)
                            {
                                playerSeeds[member] = participant.Seed;
                            }
                        }
                    }
                }

                // Create groups - now using correct parameter count
                _groupService.CreateGroups(tournament, players, playerSeeds);
            }

            // Current stage to playoffs
            tournament.CurrentStage = TournamentStage.Playoffs;

            // Save tournament state
            await _stateService.SaveTournamentStateAsync(client);

            // Generate tournament visualization
            await PostTournamentVisualizationAsync(tournament, client);

            // Additional tournament startup steps are now handled by individual services
            // Future enhancements: Add tournament visualization and match scheduling implementation
        }
    }
}