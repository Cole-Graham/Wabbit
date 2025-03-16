using DSharpPlus.Commands;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using Wabbit.Services;

namespace Wabbit.BotClient.Commands
{
    public partial class TournamentManagementGroup
    {
        [Command("recover_threads")]
        [Description("Recover threads and match status embeds for a tournament")]
        public async Task RecoverThreads(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' not found", ephemeral: true);
                    return;
                }

                // Send initial response
                await SafeResponse(context, $"Starting recovery process for tournament '{tournamentName}'...", ephemeral: true);

                // Validate and recover threads
                bool threadsRecovered = await _stateService.ValidateAndRecoverThreadsAsync(tournament, context.Client);

                // Rebuild match thread associations
                bool associationsRebuilt = await _stateService.RebuildMatchThreadAssociationsAsync(tournament, context.Client);

                // Report results
                if (threadsRecovered && associationsRebuilt)
                {
                    await SafeResponse(context, $"Successfully recovered all threads and match status embeds for tournament '{tournamentName}'", ephemeral: true);
                }
                else if (threadsRecovered)
                {
                    await SafeResponse(context, $"Recovered threads but had some issues with match associations for tournament '{tournamentName}'", ephemeral: true);
                }
                else if (associationsRebuilt)
                {
                    await SafeResponse(context, $"Recovered match associations but had some issues with threads for tournament '{tournamentName}'", ephemeral: true);
                }
                else
                {
                    await SafeResponse(context, $"Had issues recovering threads and match associations for tournament '{tournamentName}'. Check logs for details.", ephemeral: true);
                }
            }, "Error recovering tournament threads");
        }

        [Command("recover_player_thread")]
        [Description("Recover a specific player's thread in a tournament")]
        public async Task RecoverPlayerThread(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Player whose thread needs recovery")] DiscordMember player)
        {
            await SafeExecute(context, async () =>
            {
                // Find the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' not found", ephemeral: true);
                    return;
                }

                // Find the player's rounds in this tournament
                var rounds = _ongoingRounds.TourneyRounds
                    .Where(r => r.TournamentId == tournament.Name)
                    .Where(r => r.Teams?.Any(t =>
                        t.Participants?.Any(p =>
                            p.Player is DiscordMember member && member.Id == player.Id) == true) == true)
                    .ToList();

                if (rounds.Count == 0)
                {
                    await SafeResponse(context, $"Could not find any rounds for player {player.DisplayName} in tournament '{tournamentName}'", ephemeral: true);
                    return;
                }

                await SafeResponse(context, $"Attempting to recover {player.DisplayName}'s thread in tournament '{tournamentName}'...", ephemeral: true);

                int recoveredThreads = 0;
                foreach (var round in rounds)
                {
                    var team = round.Teams?.FirstOrDefault(t =>
                        t.Participants?.Any(p => p.Player is DiscordMember member && member.Id == player.Id) == true);

                    if (team != null)
                    {
                        bool needsRecovery = team.Thread is null;
                        if (!needsRecovery)
                        {
                            if (team.Thread is not null)
                            {
                                try
                                {
                                    // Check if thread still exists and is accessible
                                    var threadChannel = await context.Client.GetChannelAsync(team.Thread.Id);
                                    if (threadChannel is null)
                                    {
                                        needsRecovery = true;
                                    }
                                }
                                catch
                                {
                                    needsRecovery = true;
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Team {team.Name} has no thread property in round {round.Name} of tournament {tournament.Name}");
                            }
                        }

                        if (needsRecovery)
                        {
                            if (context.Guild is null)
                            {
                                await SafeResponse(context, $"Could not find guild for tournament '{tournamentName}'", ephemeral: true);
                                return;
                            }
                            // Get appropriate channel for the thread (use announcement channel)
                            var parentChannel = tournament.AnnouncementChannel ??
                                await context.Client.GetChannelAsync(context.Guild.Id);

                            if (parentChannel is not null)
                            {
                                // Create new thread
                                string threadName = $"{player.DisplayName}'s Tournament Thread";
                                var threadChannel = await DiscordUtilities.CreateThreadAsync(
                                    parentChannel,
                                    threadName,
                                    _logger);

                                if (threadChannel is not null)
                                {
                                    // Add the player to the thread
                                    await threadChannel.AddThreadMemberAsync(player);

                                    // Update the team's thread reference
                                    team.Thread = threadChannel;
                                    recoveredThreads++;
                                }
                            }
                        }
                    }
                }

                if (recoveredThreads > 0)
                {
                    await _stateService.SaveTournamentStateAsync(context.Client);
                    await SafeResponse(context, $"Successfully recovered thread for {player.DisplayName} in tournament '{tournamentName}'", ephemeral: true);
                }
                else
                {
                    await SafeResponse(context, $"No threads needed recovery for {player.DisplayName} in tournament '{tournamentName}'", ephemeral: true);
                }
            }, "Error recovering player thread");
        }

        [Command("recover_match")]
        [Description("Recover a specific match in a tournament")]
        public async Task RecoverMatch(
            CommandContext context,
            [Description("Tournament name")] string tournamentName,
            [Description("Match name (e.g., 'Final' or 'Group A Match 1')")] string matchName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' not found", ephemeral: true);
                    return;
                }

                // Find the match
                Tournament.Match? match = null;

                // Check group matches
                if (tournament.Groups != null)
                {
                    foreach (var group in tournament.Groups)
                    {
                        if (group.Matches != null)
                        {
                            match = group.Matches.FirstOrDefault(m =>
                                m.Name != null && m.Name.Equals(matchName, StringComparison.OrdinalIgnoreCase));

                            if (match != null) break;
                        }
                    }
                }

                // Check playoff matches if not found in groups
                if (match == null && tournament.PlayoffMatches != null)
                {
                    match = tournament.PlayoffMatches.FirstOrDefault(m =>
                        m.Name != null && m.Name.Equals(matchName, StringComparison.OrdinalIgnoreCase));
                }

                if (match == null)
                {
                    await SafeResponse(context, $"Could not find match '{matchName}' in tournament '{tournamentName}'", ephemeral: true);
                    return;
                }

                await SafeResponse(context, $"Attempting to recover match '{matchName}' in tournament '{tournamentName}'...", ephemeral: true);

                // Check if the match has a linked round
                Round? round = match.LinkedRound;

                // If no linked round, try to find one with the same name
                if (round == null)
                {
                    round = _ongoingRounds.TourneyRounds.FirstOrDefault(r =>
                        r.TournamentId == tournament.Name && r.Name == matchName);

                    // Link the round to the match
                    if (round != null)
                    {
                        match.LinkedRound = round;
                        if (round.CustomProperties == null)
                        {
                            round.CustomProperties = new Dictionary<string, object>();
                        }
                        round.CustomProperties["TournamentMatch"] = match;
                    }
                }

                if (round == null)
                {
                    await SafeResponse(context, $"Could not find a round for match '{matchName}' in tournament '{tournamentName}'", ephemeral: true);
                    return;
                }

                // Recover match status embeds
                bool recovered = await _stateService.RecoverMatchStatusEmbedsAsync(round, match, context.Client);

                if (recovered)
                {
                    await _stateService.SaveTournamentStateAsync(context.Client);
                    await SafeResponse(context, $"Successfully recovered match '{matchName}' in tournament '{tournamentName}'", ephemeral: true);
                }
                else
                {
                    await SafeResponse(context, $"Failed to recover match '{matchName}' in tournament '{tournamentName}'. Check logs for details.", ephemeral: true);
                }
            }, "Error recovering match");
        }

        [Command("rebuild_tournament")]
        [Description("Rebuild a tournament's internal data integrity")]
        public async Task RebuildTournament(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' not found", ephemeral: true);
                    return;
                }

                await SafeResponse(context, $"Starting tournament rebuild process for '{tournamentName}'...", ephemeral: true);

                // Update tournament from rounds
                _stateService.UpdateTournamentFromRound(tournament);

                // Save state
                await _stateService.SaveTournamentStateAsync(context.Client);

                await SafeResponse(context, $"Successfully rebuilt tournament '{tournamentName}' data", ephemeral: true);
            }, "Error rebuilding tournament");
        }

        [Command("validate_tournament")]
        [Description("Validate a tournament's data structure")]
        public async Task ValidateTournament(
            CommandContext context,
            [Description("Tournament name")] string tournamentName)
        {
            await SafeExecute(context, async () =>
            {
                // Find the tournament
                var tournament = _tournamentService.GetTournament(tournamentName);
                if (tournament == null)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' not found", ephemeral: true);
                    return;
                }

                await SafeResponse(context, $"Validating tournament '{tournamentName}'...", ephemeral: true);

                var errors = _stateValidator.ValidateTournamentState(tournament);

                if (errors.Count == 0)
                {
                    await SafeResponse(context, $"Tournament '{tournamentName}' validation successful. No errors found.", ephemeral: true);
                }
                else
                {
                    var messageParts = new List<string>
                    {
                        $"Tournament '{tournamentName}' validation failed with {errors.Count} errors:"
                    };

                    // Add all errors, but limit to prevent hitting Discord's message size limit
                    int maxErrors = Math.Min(errors.Count, 15);
                    for (int i = 0; i < maxErrors; i++)
                    {
                        messageParts.Add($"- {errors[i]}");
                    }

                    if (errors.Count > maxErrors)
                    {
                        messageParts.Add($"...and {errors.Count - maxErrors} more errors.");
                    }

                    await SafeResponse(context, string.Join("\n", messageParts), ephemeral: true);
                }
            }, "Error validating tournament");
        }
    }
}
