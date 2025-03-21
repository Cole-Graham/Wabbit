using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wabbit.Models;

namespace Wabbit.Services
{
    public partial class MatchStatusService
    {
        /// <summary>
        /// Updates the status message for deck submission stage
        /// </summary>
        public async Task<DiscordMessage> UpdateToDeckSubmissionStageAsync(DiscordChannel channel, Round round, DiscordClient client)
        {
            // Update the round's current stage
            round.CurrentStage = MatchStage.DeckSubmission;

            // Update the status message
            var message = await UpdateMatchStatusAsync(channel, round, client);

            // Also update in all team threads if this is not a team thread
            if (round.Teams?.All(t => t is not null && t.Thread?.Id != channel.Id) ?? false)
            {
                await UpdateMatchStatusInAllThreadsAsync(round, client);
            }

            return message;
        }

        /// <summary>
        /// Records a deck submission and updates the match status
        /// </summary>
        public async Task RecordDeckSubmissionAsync(DiscordChannel channel, Round round, ulong playerId, string deckCode, int gameNumber, DiscordClient client)
        {
            try
            {
                _logger.LogInformation($"Recording deck submission for user {playerId} in channel {channel.Id}, game number: {gameNumber}");

                // Find the participant and team
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == channel.Id);
                var participant = team?.Participants?.FirstOrDefault(p => p.Player?.Id == playerId);

                if (participant == null)
                {
                    _logger.LogWarning($"Failed to find participant with ID {playerId} in round {round.Id}");
                    return;
                }

                // Store the deck code as temporary until confirmed
                participant.TempDeckCode = deckCode;

                // Mark this team as having a pending deck submission
                if (round.CustomProperties == null)
                {
                    round.CustomProperties = new Dictionary<string, object>();
                }
                if (team != null)
                {
                    round.CustomProperties[$"PendingDeckSubmission_{team.Name}"] = true;
                }

                // Simply update the match status - the existing logic in UpdateMatchStatusAsync 
                // will handle displaying the deck information and adding appropriate buttons
                await UpdateMatchStatusAsync(channel, round, client);

                // Save tournament state to preserve the temp deck code
                if (round.CustomProperties is not null)
                {
                    // Let callers handle saving the state
                    round.CustomProperties["DeckSubmissionPending"] = true;
                }

                _logger.LogInformation($"Deck submission recorded for user {playerId} in channel {channel.Id}, waiting for confirmation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording deck submission: {ex.Message}");
            }
        }

        /// <summary>
        /// Confirms a player's deck submission
        /// </summary>
        public async Task ConfirmDeckAsync(DiscordChannel channel, Round round, ulong playerId, DiscordClient client)
        {
            try
            {
                _logger.LogInformation($"Confirming deck submission for user {playerId} in channel {channel.Id}");

                // Find the team and participant for proper tracking
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == channel.Id);
                var participant = team?.Participants?.FirstOrDefault(p => p.Player?.Id == playerId);

                if (participant == null || string.IsNullOrEmpty(participant.TempDeckCode))
                {
                    _logger.LogWarning($"No pending deck submission found for user {playerId}");
                    return;
                }

                // Move temp deck code to permanent deck field
                participant.Deck = participant.TempDeckCode;
                participant.TempDeckCode = null;

                // Clear the pending deck submission flag
                if (round.CustomProperties != null && team != null)
                {
                    if (round.CustomProperties.ContainsKey($"PendingDeckSubmission_{team.Name}"))
                    {
                        round.CustomProperties.Remove($"PendingDeckSubmission_{team.Name}");
                    }
                }

                // Check if all decks are submitted, which could trigger a stage transition
                bool allDecksSubmitted = round.Teams?.All(t =>
                    t?.Participants?.Where(p => p?.Player is not null)
                     .All(p => !string.IsNullOrEmpty(p?.Deck)) ?? false) ?? false;

                // If all decks are submitted and we're in deck submission stage, transition
                if (round.CustomProperties != null && allDecksSubmitted && round.CurrentStage == MatchStage.DeckSubmission)
                {
                    // The actual transition to GameResults would happen elsewhere, 
                    // but we need to track that a critical milestone was reached
                    round.CustomProperties["AllDecksSubmitted"] = true;

                    try
                    {
                        // Instead of selecting a map here, we should rely on TournamentMatchService.HandleDeckSubmissionAsync
                        // which should be called by the command handler after this method returns

                        // For safety, ensure we have at least a fallback map in case HandleDeckSubmissionAsync isn't called
                        if (round.Maps is null || !round.Maps.Any())
                        {
                            // Only add fallback map if we don't have any maps yet
                            string? fallbackMap = _mapService.GetRandomMapForNextGame(round);
                            if (fallbackMap == null)
                            {
                                _logger.LogError("Failed to get fallback map - using default");
                                fallbackMap = "Default Map";
                            }

                            // Initialize maps collection if needed
                            if (round.Maps is null)
                            {
                                round.Maps = new List<string>();
                            }

                            if (!round.Maps.Contains(fallbackMap))
                            {
                                round.Maps.Add(fallbackMap);
                            }

                            // Set custom instructions
                            if (round.CustomProperties is null)
                            {
                                round.CustomProperties = new Dictionary<string, object>();
                            }
                            round.CustomProperties["Instructions"] = $"Next map is **{fallbackMap}**. Please prepare your decks accordingly.";
                        }
                    }
                    catch (Exception mapEx)
                    {
                        _logger.LogError(mapEx, "Error ensuring fallback map is available");
                    }

                    // Then transition to game results stage
                    _logger.LogInformation("All decks submitted. Automatically transitioning to game results stage.");
                    round.CurrentStage = MatchStage.GameResults;

                    // First update this channel to ensure we don't lose the message
                    try
                    {
                        await UpdateToGameResultsStageAsync(channel, round, client);
                        _logger.LogInformation($"Updated game results stage in current channel {channel.Id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating game results stage in current channel {channel.Id}");
                    }

                    // Then update other team threads asynchronously to avoid getting stuck if one fails
                    if (round.Teams != null)
                    {
                        foreach (var teamObj in round.Teams)
                        {
                            if (teamObj?.Thread is not null && teamObj.Thread.Id != channel.Id)
                            {
                                // Fire and forget - don't await this call to avoid cascading failures
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await UpdateToGameResultsStageAsync(teamObj.Thread, round, client);
                                        _logger.LogInformation($"Updated game results stage in thread {teamObj.Thread.Id} for team {teamObj.Name}");
                                    }
                                    catch (Exception threadEx)
                                    {
                                        _logger.LogError(threadEx, $"Error updating game results stage in thread {teamObj.Thread.Id} for team {teamObj.Name}");
                                    }
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Just update the match status for this channel
                    await UpdateMatchStatusAsync(channel, round, client);
                }

                _logger.LogInformation($"Deck confirmed for user {playerId} in channel {channel.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming deck: {ex.Message}");

                // Try to recover by updating the channel anyway
                try
                {
                    await UpdateMatchStatusAsync(channel, round, client);
                }
                catch { }
            }
        }

        /// <summary>
        /// Revises a player's deck submission by clearing the temporary deck code
        /// </summary>
        public async Task ReviseDeckAsync(DiscordChannel channel, Round round, ulong playerId, DiscordClient client)
        {
            try
            {
                _logger.LogInformation($"Revising deck submission for user {playerId} in channel {channel.Id}");

                // Find the team and participant
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == channel.Id);
                var participant = team?.Participants?.FirstOrDefault(p => p.Player?.Id == playerId);

                if (participant == null)
                {
                    _logger.LogWarning($"No participant found for user {playerId}");
                    return;
                }

                // Clear the temp deck code
                participant.TempDeckCode = null;

                // Clear the pending deck submission flag
                if (round.CustomProperties != null && team != null)
                {
                    if (round.CustomProperties.ContainsKey($"PendingDeckSubmission_{team.Name}"))
                    {
                        round.CustomProperties.Remove($"PendingDeckSubmission_{team.Name}");
                    }
                }

                // Update the match status
                await UpdateMatchStatusAsync(channel, round, client);

                _logger.LogInformation($"Deck submission revised for user {playerId} in channel {channel.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revising deck: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds deck submissions area to the embed with a divider
        /// </summary>
        private void AddDeckSubmissionsAreaWithDivider(DiscordEmbedBuilder builder, Round round, ulong? channelId = null)
        {
            if (round.Teams == null || round.Teams.Count == 0) return;

            var deckBuilder = new StringBuilder();

            // Determine which team's thread we're in based on the channel ID
            var userTeam = channelId.HasValue
                ? round.Teams.FirstOrDefault(t => t?.Thread?.Id == channelId.Value)
                : null;

            foreach (var team in round.Teams ?? Enumerable.Empty<Round.Team>())
            {
                foreach (var participant in team.Participants ?? Enumerable.Empty<Round.Participant>())
                {
                    if (participant?.Player is null) continue;

                    // Check if there's a confirmed deck or a pending deck
                    bool hasConfirmedDeck = !string.IsNullOrEmpty(participant.Deck);
                    bool hasPendingDeck = !string.IsNullOrEmpty(participant.TempDeckCode);

                    if (hasConfirmedDeck)
                    {
                        deckBuilder.AppendLine($"\n{team.Name}: ✅ Deck submitted and confirmed");

                        // Only show deck code to the user's team
                        if (userTeam != null && team == userTeam)
                        {
                            // Add deck code in a separate line with monospace formatting
                            deckBuilder.AppendLine($"Your deck code: `{participant.Deck}`");
                        }
                    }
                    else if (hasPendingDeck)
                    {
                        deckBuilder.AppendLine($"\n{team.Name}: ⏳ Deck submitted (pending confirmation)");

                        // Only show deck code to the user's team
                        if (userTeam != null && team == userTeam)
                        {
                            // Add deck code in a separate line with monospace formatting
                            deckBuilder.AppendLine($"Your pending deck code: `{participant.TempDeckCode}`");
                        }
                    }
                    else
                    {
                        deckBuilder.AppendLine($"\n{team.Name}: ⏳ Waiting for deck submission");
                    }
                }
            }

            // Add divider at the end
            deckBuilder.AppendLine("_______________________________________________");

            builder.AddField("🃏 Deck Submissions", deckBuilder.ToString().Trim(), false);
        }
    }
}
