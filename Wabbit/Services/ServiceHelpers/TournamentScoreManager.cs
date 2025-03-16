using System;
using System.Collections.Generic;
using System.Linq;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services.ServiceHelpers
{
    /// <summary>
    /// Manages tournament score tracking and calculations
    /// </summary>
    public class TournamentScoreManager : ITournamentScoreManager
    {
        private readonly ILogger<TournamentScoreManager> _logger;

        public TournamentScoreManager(ILogger<TournamentScoreManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Updates scores for a group after a match is completed
        /// </summary>
        public void UpdateGroupScores(Tournament.Group group, Tournament.Match match)
        {
            try
            {
                if (match.Result == null || match.Result.Winner == null)
                {
                    _logger.LogWarning($"Match {match.Id} has no result or winner");
                    return;
                }

                var winner = group.Participants.FirstOrDefault(p =>
                    (p.Player as DiscordUser)?.Id == (match.Result.Winner as DiscordUser)?.Id);
                var loser = group.Participants.FirstOrDefault(p =>
                    (p.Player as DiscordUser)?.Id != (match.Result.Winner as DiscordUser)?.Id &&
                    match.Participants.Any(mp => (mp.Player as DiscordUser)?.Id == (p.Player as DiscordUser)?.Id));

                if (winner == null || loser == null)
                {
                    _logger.LogError($"Could not find participants in group {group.Name} for match {match.Id}");
                    throw new ArgumentException("Match participants not found in group");
                }

                winner.Wins++;
                loser.Losses++;

                _logger.LogInformation($"Updated scores for group {group.Name}: {(winner.Player as DiscordUser)?.Username} W:{winner.Wins} L:{winner.Losses}, {(loser.Player as DiscordUser)?.Username} W:{loser.Wins} L:{loser.Losses}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating group scores for group {group.Name}");
                throw;
            }
        }

        /// <summary>
        /// Gets sorted standings for a group based on points, wins, and game differentials
        /// </summary>
        public List<Tournament.GroupParticipant> GetGroupStandings(Tournament.Group group)
        {
            try
            {
                if (group.Participants == null)
                {
                    _logger.LogError($"Group {group.Name} has no participants");
                    throw new ArgumentException($"Group {group.Name} has no participants");
                }

                return group.Participants
                    .OrderByDescending(p => p.Wins)
                    .ThenBy(p => p.Losses)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting group standings for group {group.Name}");
                throw;
            }
        }

        /// <summary>
        /// Checks for ties among participants at a specified position
        /// </summary>
        public List<Tournament.GroupParticipant> CheckForTie(Tournament.Group group, int position)
        {
            try
            {
                var standings = GetGroupStandings(group);
                if (position >= standings.Count)
                {
                    return new List<Tournament.GroupParticipant>();
                }

                var targetParticipant = standings[position];
                return standings
                    .Where(p => p.Wins == targetParticipant.Wins && p.Losses == targetParticipant.Losses)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking for ties in group {group.Name} at position {position}");
                throw;
            }
        }

        /// <summary>
        /// Gets the best third place finishers across multiple groups
        /// </summary>
        public List<Tournament.GroupParticipant> GetBestThirdPlace(Tournament tournament, int count)
        {
            try
            {
                if (tournament.Groups == null) return new List<Tournament.GroupParticipant>();

                var thirdPlaceFinishers = tournament.Groups
                    .Select(g => GetGroupStandings(g))
                    .Where(standings => standings.Count >= 3)
                    .Select(standings => standings[2])
                    .OrderByDescending(p => p.Wins)
                    .ThenBy(p => p.Losses)
                    .Take(count)
                    .ToList();

                _logger.LogInformation($"Found {thirdPlaceFinishers.Count} best third place finishers");
                return thirdPlaceFinishers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting best third place finishers");
                throw;
            }
        }

        /// <summary>
        /// Determines if all matches in a group are complete and checks for ties
        /// </summary>
        public bool IsGroupComplete(Tournament.Group group)
        {
            try
            {
                // Check if all matches are complete
                var groupMatches = group.Matches?.Where(m => m.Type == TournamentMatchType.GroupStage);
                if (groupMatches == null || !groupMatches.All(m => m.Result != null && m.Result.Winner != null))
                {
                    return false;
                }

                // Check for ties in qualification positions
                var qualifyingSpots = 2; // Default to 2 if not specified
                for (int i = 0; i < qualifyingSpots; i++)
                {
                    if (CheckForTie(group, i).Count > 1)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if group {group.Name} is complete");
                throw;
            }
        }

        /// <summary>
        /// Gets the head-to-head record between two participants
        /// </summary>
        public (int wins, int losses) GetHeadToHeadRecord(Tournament.Group group, Tournament.GroupParticipant participant1, Tournament.GroupParticipant participant2)
        {
            try
            {
                var matches = group.Matches?
                    .Where(m => m.Result != null && m.Result.Winner != null)
                    .Where(m => m.Participants.Any(p => (p.Player as DiscordUser)?.Id == (participant1.Player as DiscordUser)?.Id) &&
                               m.Participants.Any(p => (p.Player as DiscordUser)?.Id == (participant2.Player as DiscordUser)?.Id));

                if (matches == null)
                {
                    return (0, 0);
                }

                var matches_list = matches.ToList();
                if (!matches_list.Any())
                {
                    return (0, 0);
                }

                int wins = matches_list.Count(m =>
                    (m.Result?.Winner as DiscordUser)?.Id == (participant1.Player as DiscordUser)?.Id);
                int losses = matches_list.Count(m =>
                    (m.Result?.Winner as DiscordUser)?.Id == (participant2.Player as DiscordUser)?.Id);

                return (wins, losses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting head-to-head record in group {group.Name}");
                throw;
            }
        }
    }
}