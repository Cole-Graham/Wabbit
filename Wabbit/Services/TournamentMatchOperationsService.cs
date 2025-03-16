using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System.Linq;
using Wabbit.Models;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for tournament match operations
    /// </summary>
    public class TournamentMatchOperationsService
    {
        private readonly ILogger<TournamentMatchOperationsService> _logger;

        public TournamentMatchOperationsService(ILogger<TournamentMatchOperationsService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Validates that a match can be created between two players in a tournament
        /// </summary>
        public bool ValidateMatchCreation(Tournament tournament, DiscordMember player1, DiscordMember player2)
        {
            _logger.LogInformation($"Validating match creation: {player1?.DisplayName ?? "null"} vs {player2?.DisplayName ?? "null"}");

            // Check if tournament exists
            if (tournament == null)
            {
                _logger.LogError("Cannot create match: tournament is null");
                return false;
            }

            // Check if both players are valid
            if (player1 is null || player2 is null)
            {
                _logger.LogError($"Cannot create match: one or both players are null. Player1={player1?.DisplayName ?? "null"}, Player2={player2?.DisplayName ?? "null"}");
                return false;
            }

            // Check if players are the same
            if (player1.Id == player2.Id)
            {
                _logger.LogError($"Cannot create match: both players are the same ({player1.DisplayName})");
                return false;
            }

            // Check if both players are registered in the tournament
            bool player1Registered = false;
            bool player2Registered = false;

            // Check in all groups
            foreach (var group in tournament.Groups ?? Enumerable.Empty<Tournament.Group>())
            {
                foreach (var participant in group.Participants ?? Enumerable.Empty<Tournament.GroupParticipant>())
                {
                    if (participant.Player is DiscordMember member)
                    {
                        if (member.Id == player1.Id)
                            player1Registered = true;
                        else if (member.Id == player2.Id)
                            player2Registered = true;
                    }
                }
            }

            if (!player1Registered || !player2Registered)
            {
                _logger.LogError($"Cannot create match: players not registered in tournament. Player1={player1.DisplayName} registered={player1Registered}, Player2={player2.DisplayName} registered={player2Registered}");
                return false;
            }

            _logger.LogInformation("Match validation successful");
            return true;
        }
    }
}