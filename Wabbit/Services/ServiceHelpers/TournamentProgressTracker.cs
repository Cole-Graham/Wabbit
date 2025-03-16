using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Tracks and manages tournament stage progression
    /// </summary>
    public class TournamentProgressTracker : ITournamentProgressTracker
    {
        private readonly ILogger<TournamentProgressTracker> _logger;
        private readonly TournamentScoreManager _scoreManager;
        private readonly TournamentStateValidator _stateValidator;

        public TournamentProgressTracker(
            ILogger<TournamentProgressTracker> logger,
            TournamentScoreManager scoreManager,
            TournamentStateValidator stateValidator)
        {
            _logger = logger;
            _scoreManager = scoreManager;
            _stateValidator = stateValidator;
        }

        /// <summary>
        /// Checks if a tournament can move to the next stage
        /// </summary>
        public bool CanProgressToNextStage(Tournament tournament)
        {
            if (tournament == null)
            {
                _logger.LogError("Cannot check progression for null tournament");
                return false;
            }

            return tournament.CurrentStage switch
            {
                TournamentStage.SignupOpen => CanStartGroupStage(tournament),
                TournamentStage.Groups => CanStartPlayoffs(tournament),
                TournamentStage.Playoffs => CanComplete(tournament),
                _ => false
            };
        }

        /// <summary>
        /// Gets progress metrics for the current tournament stage
        /// </summary>
        public Dictionary<string, double> GetStageProgress(Tournament tournament)
        {
            var progress = new Dictionary<string, double>();

            if (tournament == null)
            {
                return progress;
            }

            switch (tournament.CurrentStage)
            {
                case TournamentStage.SignupOpen:
                    if (tournament.Settings?.MinPlayers > 0)
                    {
                        int signedUp = tournament.SignedUpPlayers?.Count ?? 0;
                        progress["SignupProgress"] = Math.Min(100.0, (signedUp * 100.0) / tournament.Settings.MinPlayers);
                    }
                    break;

                case TournamentStage.Groups:
                    if (tournament.Groups != null)
                    {
                        int totalMatches = tournament.Groups.Sum(g => g.Matches?.Count ?? 0);
                        int completedMatches = tournament.Groups.Sum(g => g.Matches?.Count(m => m.IsComplete) ?? 0);

                        if (totalMatches > 0)
                        {
                            progress["GroupStageProgress"] = (completedMatches * 100.0) / totalMatches;
                        }

                        progress["GroupsComplete"] = tournament.Groups.Count(g => _scoreManager.IsGroupComplete(g)) * 100.0 / tournament.Groups.Count;
                    }
                    break;

                case TournamentStage.Playoffs:
                    if (tournament.PlayoffMatches != null)
                    {
                        int totalMatches = tournament.PlayoffMatches.Count;
                        int completedMatches = tournament.PlayoffMatches.Count(m => m.IsComplete);

                        if (totalMatches > 0)
                        {
                            progress["PlayoffProgress"] = (completedMatches * 100.0) / totalMatches;
                        }
                    }
                    break;
            }

            return progress;
        }

        /// <summary>
        /// Gets a list of pending actions needed to progress the tournament
        /// </summary>
        public List<string> GetPendingActions(Tournament tournament)
        {
            var actions = new List<string>();

            if (tournament == null)
            {
                actions.Add("Tournament object is null");
                return actions;
            }

            // Add any validation errors
            actions.AddRange(_stateValidator.ValidateTournamentState(tournament));

            // Add stage-specific actions
            switch (tournament.CurrentStage)
            {
                case TournamentStage.SignupOpen:
                    CheckSignupActions(tournament, actions);
                    break;

                case TournamentStage.Groups:
                    CheckGroupStageActions(tournament, actions);
                    break;

                case TournamentStage.Playoffs:
                    CheckPlayoffActions(tournament, actions);
                    break;
            }

            return actions;
        }

        private void CheckSignupActions(Tournament tournament, List<string> actions)
        {
            if (tournament.Settings?.MinPlayers > 0)
            {
                int signedUp = tournament.SignedUpPlayers?.Count ?? 0;
                int needed = tournament.Settings.MinPlayers - signedUp;
                if (needed > 0)
                {
                    actions.Add($"Need {needed} more players to start tournament");
                }
            }
        }

        private void CheckGroupStageActions(Tournament tournament, List<string> actions)
        {
            if (tournament.Groups == null || !tournament.Groups.Any())
            {
                actions.Add("Groups need to be created");
                return;
            }

            foreach (var group in tournament.Groups)
            {
                if (!_scoreManager.IsGroupComplete(group))
                {
                    int pendingMatches = group.Matches?.Count(m => !m.IsComplete) ?? 0;
                    actions.Add($"Group {group.Name} has {pendingMatches} pending matches");
                }
            }
        }

        private void CheckPlayoffActions(Tournament tournament, List<string> actions)
        {
            if (tournament.PlayoffMatches == null || !tournament.PlayoffMatches.Any())
            {
                actions.Add("Playoff bracket needs to be created");
                return;
            }

            int pendingMatches = tournament.PlayoffMatches.Count(m => !m.IsComplete);
            if (pendingMatches > 0)
            {
                actions.Add($"{pendingMatches} playoff matches need to be completed");
            }
        }

        private bool CanStartGroupStage(Tournament tournament)
        {
            if (tournament.Settings?.MinPlayers == null || tournament.SignedUpPlayers == null)
            {
                return false;
            }

            return tournament.SignedUpPlayers.Count >= tournament.Settings.MinPlayers
                && _stateValidator.IsValidStateTransition(tournament, TournamentStage.Groups);
        }

        private bool CanStartPlayoffs(Tournament tournament)
        {
            if (tournament.Groups == null)
            {
                return false;
            }

            bool allGroupsComplete = tournament.Groups.All(g => _scoreManager.IsGroupComplete(g));
            return allGroupsComplete && _stateValidator.IsValidStateTransition(tournament, TournamentStage.Playoffs);
        }

        private bool CanComplete(Tournament tournament)
        {
            if (tournament.PlayoffMatches == null)
            {
                return false;
            }

            bool allMatchesComplete = tournament.PlayoffMatches.All(m => m.IsComplete);
            bool hasFinalWinner = tournament.PlayoffMatches
                .FirstOrDefault(m => m.Type == TournamentMatchType.Final)?.Result?.Winner != null;

            return allMatchesComplete && hasFinalWinner && _stateValidator.IsValidStateTransition(tournament, TournamentStage.Complete);
        }
    }
}