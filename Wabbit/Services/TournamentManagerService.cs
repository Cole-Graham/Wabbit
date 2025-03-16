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

        // Dictionary to track pending matches by tournament, supporting any game type
        private Dictionary<string, List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)>> _pendingMatches = new();

        // Set of active player IDs to prevent double-booking
        private HashSet<ulong> _activePlayers = new();

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
        public async Task UpdateMatchResult(Tournament tournament, Tournament.Match match, DiscordMember winner, int winnerScore, int loserScore, DiscordClient client)
        {
            // Call match service to update the result
            await _matchService.UpdateMatchResultAsync(tournament, match, winner, winnerScore, loserScore);

            // Check for group completion
            if (match.Type == TournamentMatchType.GroupStage && match.Participants[0].SourceGroup != null)
            {
                _groupService.CheckGroupCompletion(match.Participants[0].SourceGroup!);

                // If all groups are completed, set up playoffs
                if (tournament.Groups?.All(g => g.IsComplete) == true && tournament.CurrentStage == TournamentStage.Groups)
                {
                    await _playoffService.SetupPlayoffsAsync(tournament, client);
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

        /// <summary>
        /// Schedules next available matches for a tournament
        /// </summary>
        public async Task ScheduleNextMatchBatchAsync(Tournament tournament, DiscordClient client)
        {
            if (tournament == null || string.IsNullOrEmpty(tournament.Name))
            {
                _logger.LogError("Cannot schedule matches: tournament is null or has no name");
                return;
            }

            // Initialize pending matches if needed
            if (!_pendingMatches.ContainsKey(tournament.Name))
            {
                _pendingMatches[tournament.Name] = GeneratePendingMatches(tournament);
                _logger.LogInformation($"Generated {_pendingMatches[tournament.Name].Count} pending matches for tournament {tournament.Name}");
            }

            // Get pending matches for this tournament
            var tournamentPendingMatches = _pendingMatches[tournament.Name];
            if (tournamentPendingMatches.Count == 0)
            {
                _logger.LogInformation($"No pending matches for tournament {tournament.Name}");
                return;
            }

            // Find matches that can be scheduled now (no team members already active)
            var matchesToSchedule = new List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)>();

            foreach (var match in tournamentPendingMatches.ToList())
            {
                // Check if any players in either team are already active
                bool anyPlayerActive = false;

                // Check team A
                foreach (var player in match.TeamA)
                {
                    if (_activePlayers.Contains(player.Id))
                    {
                        anyPlayerActive = true;
                        break;
                    }
                }

                // Check team B if needed
                if (!anyPlayerActive)
                {
                    foreach (var player in match.TeamB)
                    {
                        if (_activePlayers.Contains(player.Id))
                        {
                            anyPlayerActive = true;
                            break;
                        }
                    }
                }

                // Skip if any player is active
                if (anyPlayerActive)
                {
                    continue;
                }

                // Teams can be scheduled - add all players to active list
                matchesToSchedule.Add(match);
                foreach (var player in match.TeamA.Concat(match.TeamB))
                {
                    _activePlayers.Add(player.Id);
                }

                // Remove from pending matches
                tournamentPendingMatches.Remove(match);
            }

            // Schedule the matches
            foreach (var match in matchesToSchedule)
            {
                // Determine best-of format based on match type
                int bestOf = match.Group != null ? 1 : 3; // Bo1 for group stage, Bo3 for playoffs

                // Check what game type we're dealing with
                if (tournament.GameType == GameType.OneVsOne && match.TeamA.Count == 1 && match.TeamB.Count == 1)
                {
                    // 1v1 match
                    await _matchService.CreateAndStart1v1Match(
                        tournament,
                        match.Group,
                        match.TeamA[0],
                        match.TeamB[0],
                        client,
                        bestOf);

                    _logger.LogInformation($"Started 1v1 match {match.TeamA[0].DisplayName} vs {match.TeamB[0].DisplayName} in tournament {tournament.Name}");
                }
                else
                {
                    // Team match (2v2, etc.)
                    await _matchService.CreateAndStartMatch(
                        tournament,
                        match.Group,
                        match.TeamA,
                        match.TeamB,
                        client,
                        bestOf);

                    string teamANames = string.Join(", ", match.TeamA.Select(p => p.DisplayName));
                    string teamBNames = string.Join(", ", match.TeamB.Select(p => p.DisplayName));
                    _logger.LogInformation($"Started team match {teamANames} vs {teamBNames} in tournament {tournament.Name}");
                }
            }

            // Save the tournament state
            await _stateService.SaveTournamentStateAsync(client);
        }

        /// <summary>
        /// Generates all possible match pairs for a tournament, supporting any game type
        /// </summary>
        private List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)> GeneratePendingMatches(Tournament tournament)
        {
            var pendingMatches = new List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)>();

            if (tournament.Groups == null)
            {
                _logger.LogError("Cannot generate matches: tournament has no groups");
                return pendingMatches;
            }

            _logger.LogInformation($"Generating matches for tournament {tournament.Name} with game type {tournament.GameType}");

            // Handle different game types
            switch (tournament.GameType)
            {
                case GameType.OneVsOne:
                    _logger.LogInformation("Using 1v1 match generation");
                    return Generate1v1Matches(tournament);

                case GameType.TwoVsTwo:
                    _logger.LogInformation("Using 2v2 match generation");
                    return Generate2v2Matches(tournament);

                default:
                    _logger.LogWarning($"Unrecognized game type: {tournament.GameType}, falling back to 1v1 matches");
                    return Generate1v1Matches(tournament);
            }
        }

        /// <summary>
        /// Generates 1v1 matches for a tournament
        /// </summary>
        private List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)> Generate1v1Matches(Tournament tournament)
        {
            var pendingMatches = new List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)>();

            if (tournament.Groups == null)
            {
                _logger.LogError("Tournament has no groups, cannot generate 1v1 matches");
                return pendingMatches;
            }

            // Log the total number of groups
            _logger.LogInformation($"Generating matches for {tournament.Groups.Count} groups");

            // Process each group
            foreach (var group in tournament.Groups)
            {
                if (group.Participants == null)
                {
                    _logger.LogWarning($"Group {group.Name} has no participants, skipping");
                    continue;
                }

                if (group.Matches == null)
                {
                    // Initialize the matches collection if it's null
                    group.Matches = new List<Tournament.Match>();
                }

                _logger.LogInformation($"Group {group.Name} has {group.Participants.Count} participants");

                // Debug participant types
                foreach (var participant in group.Participants)
                {
                    var playerType = participant.Player?.GetType().Name ?? "null";
                    var isDiscordMember = participant.Player is DiscordMember;
                    _logger.LogInformation($"Participant Player: Type={playerType}, IsDiscordMember={isDiscordMember}, DisplayName={participant.Player?.ToString() ?? "null"}");
                }

                // Create matches for each player pair in this group
                for (int i = 0; i < group.Participants.Count; i++)
                {
                    for (int j = i + 1; j < group.Participants.Count; j++)
                    {
                        var player1 = group.Participants[i].Player as DiscordMember;
                        var player2 = group.Participants[j].Player as DiscordMember;

                        _logger.LogInformation($"Checking pair: Player1={player1?.DisplayName ?? "null"}, Player2={player2?.DisplayName ?? "null"}");

                        if (player1 is not null && player2 is not null)
                        {
                            // Check if a match already exists between these players
                            bool matchExists = group.Matches.Any(m =>
                                m.Participants?.Count == 2 &&
                                ((m.Participants[0].Player is DiscordMember p1 && p1.Id == player1.Id &&
                                  m.Participants[1].Player is DiscordMember p2 && p2.Id == player2.Id) ||
                                 (m.Participants[0].Player is DiscordMember p3 && p3.Id == player2.Id &&
                                  m.Participants[1].Player is DiscordMember p4 && p4.Id == player1.Id)));

                            _logger.LogInformation($"Match already exists for {player1.DisplayName} vs {player2.DisplayName}: {matchExists}");

                            if (!matchExists)
                            {
                                _logger.LogInformation($"Adding pending match: {player1.DisplayName} vs {player2.DisplayName}");
                                pendingMatches.Add((group, new List<DiscordMember> { player1 }, new List<DiscordMember> { player2 }));
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid player pair in group {group.Name}: Player1={player1?.DisplayName ?? "null"}, Player2={player2?.DisplayName ?? "null"}");
                        }
                    }
                }
            }

            _logger.LogInformation($"Generated {pendingMatches.Count} pending 1v1 matches");
            return pendingMatches;
        }

        /// <summary>
        /// Generates 2v2 matches for a tournament
        /// </summary>
        private List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)> Generate2v2Matches(Tournament tournament)
        {
            var pendingMatches = new List<(Tournament.Group Group, List<DiscordMember> TeamA, List<DiscordMember> TeamB)>();
            if (tournament.Groups == null)
            {
                _logger.LogError("Tournament has no groups, cannot generate 2v2 matches");
                return pendingMatches;
            }

            // Process each group - this assumes the participants are already organized into teams
            foreach (var group in tournament.Groups)
            {
                if (group.Participants == null || group.Matches == null)
                {
                    continue;
                }

                // Get all teams (assuming teams are stored as pairs in sequential participants)
                var teams = new List<List<DiscordMember>>();

                // This is a simplistic approach - in a real implementation, you'd need to have
                // proper team data structures or a way to identify which players are on which team
                for (int i = 0; i < group.Participants.Count; i += 2)
                {
                    if (i + 1 < group.Participants.Count)
                    {
                        var player1 = group.Participants[i].Player as DiscordMember;
                        var player2 = group.Participants[i + 1].Player as DiscordMember;

                        if (player1 is not null && player2 is not null)
                        {
                            teams.Add(new List<DiscordMember> { player1, player2 });
                        }
                    }
                }

                // Create matches between each pair of teams
                for (int i = 0; i < teams.Count; i++)
                {
                    for (int j = i + 1; j < teams.Count; j++)
                    {
                        var teamA = teams[i];
                        var teamB = teams[j];

                        // Check if a match already exists between these teams
                        // This is a simplified check - you'd need more robust team identification
                        bool matchExists = false;

                        if (!matchExists)
                        {
                            pendingMatches.Add((group, teamA, teamB));
                        }
                    }
                }
            }

            return pendingMatches;
        }

        /// <summary>
        /// Handles a match completion event and schedules next matches if available
        /// </summary>
        public async Task HandleMatchCompletionEvent(Tournament tournament, Tournament.Match match, DiscordClient client)
        {
            if (tournament == null || match == null)
            {
                return;
            }

            // Free up the players from this match
            foreach (var participant in match.Participants ?? Enumerable.Empty<Tournament.MatchParticipant>())
            {
                if (participant?.Player is DiscordMember member)
                {
                    _activePlayers.Remove(member.Id);
                }
            }

            // Schedule next matches
            await ScheduleNextMatchBatchAsync(tournament, client);

            // Check if tournament is complete
            await HandleTournamentProgressionAsync(tournament, client);
        }

        /// <summary>
        /// Starts the group stage for a tournament
        /// </summary>
        public async Task StartGroupStage(Tournament tournament, DiscordClient client)
        {
            if (tournament == null)
            {
                _logger.LogError("Cannot start group stage: tournament is null");
                return;
            }

            _logger.LogInformation($"Starting group stage for tournament {tournament.Name}");

            // Ensure tournament is set to group stage
            tournament.CurrentStage = TournamentStage.Groups;

            // Ensure all participants are proper DiscordMember objects
            _groupService.EnsureParticipantsAreDiscordMembers(tournament, client);

            // Generate all pending matches based on game type
            _pendingMatches[tournament.Name] = GeneratePendingMatches(tournament);
            _logger.LogInformation($"Generated {_pendingMatches[tournament.Name].Count} pending matches for tournament {tournament.Name}");

            // Schedule the first batch of matches
            await ScheduleNextMatchBatchAsync(tournament, client);

            // Save tournament state
            await _stateService.SaveTournamentStateAsync(client);
        }

        /// <summary>
        /// Checks if tournament can progress to next stage and handles progression if needed
        /// </summary>
        private async Task HandleTournamentProgressionAsync(Tournament tournament, DiscordClient client)
        {
            if (tournament == null)
                return;

            try
            {
                // Check if all group matches are complete for group stage
                if (tournament.CurrentStage == TournamentStage.Groups &&
                    tournament.Groups?.All(g => g.IsComplete || g.Matches?.All(m => m.IsComplete) == true) == true)
                {
                    // Group stage is complete, set up playoffs
                    await _playoffService.SetupPlayoffsAsync(tournament, client);
                    _logger.LogInformation($"Tournament {tournament.Name} has progressed to playoffs stage");

                    // When moving to playoffs, we need to start scheduling playoff matches
                    // Clear active players to allow scheduling playoff matches
                    _activePlayers.Clear();

                    // Start playoff matches
                    await _playoffService.StartPlayoffMatchesAsync(tournament, client);
                }
                // Check if all playoff matches are complete
                else if (tournament.CurrentStage == TournamentStage.Playoffs &&
                         tournament.PlayoffMatches?.All(m => m.IsComplete) == true)
                {
                    // Tournament is complete
                    tournament.CurrentStage = TournamentStage.Complete;
                    tournament.IsComplete = true;
                    _logger.LogInformation($"Tournament {tournament.Name} is now complete");

                    // Archive threads
                    foreach (var match in tournament.PlayoffMatches ?? Enumerable.Empty<Tournament.Match>())
                    {
                        await _matchService.ArchiveMatchThreadsAsync(match, client);
                    }
                }

                // Save tournament state
                await _stateService.SaveTournamentStateAsync(client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling tournament progression for {tournament.Name}");
            }
        }
    }
}