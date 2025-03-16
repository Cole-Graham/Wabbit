using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.Linq;

namespace Wabbit.Services
{
    /// <summary>
    /// Manages concurrent access to tournament state and provides transaction-like behavior for updates
    /// </summary>
    public class TournamentStateManager : ITournamentStateManager
    {
        private readonly ILogger<TournamentStateManager> _logger;
        private readonly ITournamentStateService _stateService;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tournamentLocks;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _matchLocks;
        private readonly ConcurrentDictionary<string, Tournament> _stateBackups;
        private readonly ITournamentStateValidator _stateValidator;

        public TournamentStateManager(
            ILogger<TournamentStateManager> logger,
            ITournamentStateService stateService,
            ITournamentStateValidator stateValidator)
        {
            _logger = logger;
            _stateService = stateService;
            _stateValidator = stateValidator;
            _tournamentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _matchLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _stateBackups = new ConcurrentDictionary<string, Tournament>();
        }

        /// <summary>
        /// Executes a tournament update operation with proper locking and rollback capability
        /// </summary>
        public async Task<bool> ExecuteTournamentUpdateAsync(
            Tournament tournament,
            Func<Tournament, Task<bool>> updateAction,
            string operationName)
        {
            var tournamentLock = _tournamentLocks.GetOrAdd(
                tournament.Name,
                _ => new SemaphoreSlim(1, 1));

            try
            {
                await tournamentLock.WaitAsync();
                _logger.LogInformation($"Acquired lock for tournament {tournament.Name} - {operationName}");

                // Create state backup
                _stateBackups[tournament.Name] = tournament.DeepClone();

                // Perform the update
                bool success = await updateAction(tournament);

                if (success)
                {
                    // Save state if successful
                    await _stateService.SafeSaveTournamentStateAsync();
                    _logger.LogInformation($"Successfully completed {operationName} for tournament {tournament.Name}");
                }
                else
                {
                    // Rollback on failure
                    await RollbackTournamentStateAsync(tournament.Name);
                    _logger.LogWarning($"Rolling back {operationName} for tournament {tournament.Name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during {operationName} for tournament {tournament.Name}");
                await RollbackTournamentStateAsync(tournament.Name);
                return false;
            }
            finally
            {
                tournamentLock.Release();
                _logger.LogInformation($"Released lock for tournament {tournament.Name} - {operationName}");
            }
        }

        /// <summary>
        /// Executes a match update operation with proper locking and validation
        /// </summary>
        public async Task<bool> ExecuteMatchUpdateAsync(
            Tournament tournament,
            Tournament.Match match,
            Func<Tournament.Match, Task<bool>> updateAction,
            string operationName)
        {
            var matchLock = _matchLocks.GetOrAdd(
                match.Id,
                _ => new SemaphoreSlim(1, 1));

            try
            {
                await matchLock.WaitAsync();
                _logger.LogInformation($"Acquired lock for match {match.Id} - {operationName}");

                // Validate match state before proceeding
                if (!ValidateMatchState(match))
                {
                    _logger.LogWarning($"Invalid match state detected for {match.Id}");
                    return false;
                }

                // Create state backup at tournament level since match updates can affect tournament state
                _stateBackups[tournament.Name] = tournament.DeepClone();

                // Perform the update
                bool success = await updateAction(match);

                if (success)
                {
                    // Save state if successful
                    await _stateService.SafeSaveTournamentStateAsync();
                    _logger.LogInformation($"Successfully completed {operationName} for match {match.Id}");
                }
                else
                {
                    // Rollback on failure
                    await RollbackTournamentStateAsync(tournament.Name);
                    _logger.LogWarning($"Rolling back {operationName} for match {match.Id}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during {operationName} for match {match.Id}");
                await RollbackTournamentStateAsync(tournament.Name);
                return false;
            }
            finally
            {
                matchLock.Release();
                _logger.LogInformation($"Released lock for match {match.Id} - {operationName}");
            }
        }

        /// <summary>
        /// Validates the current state of a match
        /// </summary>
        private bool ValidateMatchState(Tournament.Match match)
        {
            try
            {
                if (match is null) return false;

                // Find the tournament this match belongs to
                var tournament = _stateBackups.Values.FirstOrDefault(t =>
                    t.PlayoffMatches?.Any(m => m.Id == match.Id) == true ||
                    t.Groups?.Any(g => g.Matches?.Any(m => m.Id == match.Id) == true) == true);

                if (tournament == null)
                {
                    _logger.LogError($"Could not find tournament for match {match.Id}");
                    return false;
                }

                var errors = _stateValidator.ValidateMatch(tournament, match);
                if (errors.Any())
                {
                    foreach (var error in errors)
                    {
                        _logger.LogWarning($"Match validation error: {error}");
                    }
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating match state for {match.Id}");
                return false;
            }
        }

        /// <summary>
        /// Rolls back tournament state to the last backup
        /// </summary>
        private async Task RollbackTournamentStateAsync(string tournamentName)
        {
            try
            {
                if (_stateBackups.TryRemove(tournamentName, out var backup))
                {
                    // Load the current tournament state
                    var currentState = await _stateService.LoadTournamentState();
                    if (currentState is not null)
                    {
                        // Replace with backup
                        currentState = backup;
                        await _stateService.SafeSaveTournamentStateAsync();
                        _logger.LogInformation($"Successfully rolled back tournament {tournamentName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rolling back tournament state for {tournamentName}");
            }
        }

        /// <summary>
        /// Releases all locks and cleans up resources
        /// </summary>
        public void Dispose()
        {
            foreach (var lock_ in _tournamentLocks.Values)
            {
                lock_.Dispose();
            }
            foreach (var lock_ in _matchLocks.Values)
            {
                lock_.Dispose();
            }
            _tournamentLocks.Clear();
            _matchLocks.Clear();
            _stateBackups.Clear();
        }
    }
}