using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Implementation of core match operations
    /// </summary>
    public class TournamentMatchOperationsService : ITournamentMatchOperationsService
    {
        private readonly ILogger<TournamentMatchOperationsService> _logger;
        private readonly ITournamentScoreManager _scoreManager;

        public TournamentMatchOperationsService(
            ILogger<TournamentMatchOperationsService> logger,
            ITournamentScoreManager scoreManager)
        {
            _logger = logger;
            _scoreManager = scoreManager;
        }

        /// <inheritdoc/>
        public Tournament.Match CreateMatch(
            string matchName,
            TournamentMatchType matchType,
            int bestOf,
            DiscordMember player1,
            DiscordMember player2,
            Tournament.Group? sourceGroup = null)
        {
            var match = new Tournament.Match
            {
                Name = matchName,
                Type = matchType,
                BestOf = bestOf,
                Participants = new List<Tournament.MatchParticipant>
                {
                    new Tournament.MatchParticipant
                    {
                        Player = player1,
                        SourceGroup = sourceGroup
                    },
                    new Tournament.MatchParticipant
                    {
                        Player = player2,
                        SourceGroup = sourceGroup
                    }
                }
            };

            // Add to group if provided
            sourceGroup?.Matches?.Add(match);

            return match;
        }

        /// <inheritdoc/>
        public async Task UpdateMatchResultAsync(
            Tournament tournament,
            Tournament.Match match,
            DiscordMember winner,
            int winnerScore,
            int loserScore)
        {
            if (match == null)
                throw new ArgumentNullException(nameof(match));

            // Find winner and loser participants
            var winnerParticipant = match.Participants.FirstOrDefault(p =>
                (p.Player as DiscordMember)?.Id == winner.Id);
            var loserParticipant = match.Participants.FirstOrDefault(p =>
                (p.Player as DiscordMember)?.Id != winner.Id);

            if (winnerParticipant == null || loserParticipant == null)
            {
                _logger.LogError("Could not find match participants");
                throw new InvalidOperationException("Match participants not found");
            }

            // Update match result
            match.Result = new Tournament.MatchResult
            {
                Winner = winner,
                WinnerScore = winnerScore,
                LoserScore = loserScore,
                CompletedAt = DateTime.Now,
                Status = MatchStatus.Completed
            };

            // Update group stats if this is a group stage match
            if (match.Type == TournamentMatchType.GroupStage &&
                winnerParticipant.SourceGroup != null)
            {
                _scoreManager.UpdateGroupScores(winnerParticipant.SourceGroup, match);
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public bool ValidateMatchCreation(
            Tournament tournament,
            DiscordMember player1,
            DiscordMember player2)
        {
            if (tournament == null)
                throw new ArgumentNullException(nameof(tournament));

            // Check if players are different
            if (player1.Id == player2.Id)
            {
                _logger.LogError("Cannot create match between the same player");
                return false;
            }

            // Check if players are in the tournament
            if (tournament.Groups == null) return false;

            var allParticipants = tournament.Groups
                .SelectMany(g => g.Participants ?? Enumerable.Empty<Tournament.GroupParticipant>())
                .Select(p => p.Player as DiscordMember)
                .Where(p => p is not null)
                .Select(p => p!.Id)
                .ToList();

            if (!allParticipants.Contains(player1.Id) || !allParticipants.Contains(player2.Id))
            {
                _logger.LogError("One or both players are not in the tournament");
                return false;
            }

            return true;
        }
    }
}