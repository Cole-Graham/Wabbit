using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.BotClient.Config;
using Wabbit.Data;
using System.ComponentModel;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Net;
using System.Reflection;
using System.Dynamic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Commands
{
    [Command("tournament_manager")]
    [Description("Commands for managing tournaments")]
    public partial class TournamentManagementGroup
    {
        private const int autoDeleteSeconds = 30;

        // New services
        private readonly ITournamentManagerService _tournamentService;
        private readonly OngoingRounds _ongoingRounds;
        private readonly ILogger<TournamentManagementGroup> _logger;
        private readonly ITournamentRepositoryService _repositoryService;
        private readonly ITournamentSignupService _signupService;
        private readonly ITournamentStateService _stateService;
        private readonly ITournamentGroupService _groupService;
        private readonly ITournamentPlayoffService _playoffService;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ITournamentMatchService _tournamentMatchService;
        private readonly ITournamentStateValidator _stateValidator;

        public TournamentManagementGroup(
            OngoingRounds ongoingRounds,
            ILogger<TournamentManagementGroup> logger,
            ITournamentManagerService tournamentService,
            ITournamentRepositoryService repositoryService,
            ITournamentSignupService signupService,
            ITournamentStateService stateService,
            ITournamentGroupService groupService,
            ITournamentPlayoffService playoffService,
            ITournamentGameService tournamentGameService,
            ITournamentMatchService tournamentMatchService,
            ITournamentStateValidator stateValidator)
        {
            _ongoingRounds = ongoingRounds;
            _logger = logger;

            // Initialize new services
            _tournamentService = tournamentService;
            _repositoryService = repositoryService;
            _signupService = signupService;
            _stateService = stateService;
            _groupService = groupService;
            _playoffService = playoffService;
            _tournamentGameService = tournamentGameService;
            _tournamentMatchService = tournamentMatchService;
            _stateValidator = stateValidator;
        }

        // Helper method to get player display name
        private string GetPlayerDisplayName(object? player)
        {
            if (player is DiscordMember member)
                return member.DisplayName;
            return player?.ToString() ?? "Unknown Player";
        }

        private ulong? GetSignupChannelId(CommandContext context)
        {
            if (ConfigManager.Config?.Servers == null) return null;

            var server = ConfigManager.Config.Servers.FirstOrDefault(s => s.ServerId == context.Guild?.Id);
            return server?.SignupChannelId;
        }

        private async Task SafeResponse(CommandContext context, string message, Action? action = null, bool ephemeral = false, int autoDeleteSeconds = 0)
        {
            try
            {
                // For ephemeral messages, we need to use a different approach
                if (ephemeral)
                {
                    // First respond with a regular message
                    var response = await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
                    action?.Invoke();

                    // Then delete it after a short delay
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds > 0 ? autoDeleteSeconds : 10));
                        try
                        {
                            await response.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to auto-delete message: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Regular response
                    var response = await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(message));
                    action?.Invoke();

                    // Auto-delete if requested
                    if (autoDeleteSeconds > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds));
                            try
                            {
                                await response.DeleteAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to auto-delete message: {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in SafeResponse: {ex.Message}");
                try
                {
                    var msg = await context.Channel.SendMessageAsync(message);
                    action?.Invoke();

                    // Auto-delete if requested
                    if ((ephemeral || autoDeleteSeconds > 0) && msg is not null)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(autoDeleteSeconds > 0 ? autoDeleteSeconds : 10));
                            try
                            {
                                await msg.DeleteAsync();
                            }
                            catch (Exception delEx)
                            {
                                _logger.LogError(delEx, $"Failed to auto-delete fallback message: {delEx.Message}");
                            }
                        });
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, $"Failed to send fallback message: {innerEx.Message}");
                }
            }
        }

        private async Task SafeExecute(CommandContext context, Func<Task> action, string errorPrefix = "Command failed")
        {
            try
            {
                // Give the API a much longer delay to be ready - this helps with "Unknown interaction" errors
                await Task.Delay(500);  // Increased from 200ms

                // Try to defer the interaction, but ignore if it fails
                try
                {
                    // Call DeferResponseAsync early to avoid timeouts
                    await context.DeferResponseAsync();
                }
                catch (Exception deferEx)
                {
                    // If deferring fails, log it but continue - the interaction might already be deferred
                    _logger.LogWarning($"Failed to defer response: {deferEx.Message}. Continuing execution...");
                }

                // Execute the action
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{errorPrefix}: {ex.Message}");

                // Try to respond with the error message
                try
                {
                    // Use SafeResponse for error messages to make them ephemeral and auto-delete
                    await SafeResponse(context, $"{errorPrefix}: {ex.Message}", null, true, 10);
                }
                catch (Exception responseEx)
                {
                    _logger.LogError(responseEx, "Failed to send error response via interaction");

                    // Fallback to channel message if interaction response fails
                    try
                    {
                        var msg = await context.Channel.SendMessageAsync($"{errorPrefix}: {ex.Message}");

                        // Set up auto-deletion for channel messages too
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(10000); // 10 seconds
                            try
                            {
                                await msg.DeleteAsync();
                            }
                            catch (Exception delEx)
                            {
                                _logger.LogError(delEx, "Failed to auto-delete fallback error message");
                            }
                        });
                    }
                    catch (Exception channelEx)
                    {
                        _logger.LogError(channelEx, "Failed to send any error messages");
                    }
                }
            }
        }

        // Helper method to find a tournament winner
        private object? FindTournamentWinner(Tournament tournament)
        {
            if (tournament?.PlayoffMatches == null || tournament.PlayoffMatches.Count == 0)
                return null;

            var finalMatch = tournament.PlayoffMatches.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
            if (finalMatch?.Result != null)
            {
                return finalMatch.Result.Winner;
            }

            return null;
        }

        private async Task SaveTournamentStateAsync(BaseDiscordClient client)
        {
            await _stateService.SaveTournamentStateAsync(client as DiscordClient);
        }
    }

    public class TournamentFormatChoiceProvider : IChoiceProvider
    {
        private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> formats = new DiscordApplicationCommandOptionChoice[]
        {
            new("Group Stage + Playoffs", "GroupStageWithPlayoffs"),
            new("Single Elimination", "SingleElimination"),
            new("Double Elimination", "DoubleElimination"),
            new("Round Robin", "RoundRobin"),
        };

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(formats);
        }
    }

    public class GameTypeChoiceProvider : IChoiceProvider
    {
        private static readonly IEnumerable<DiscordApplicationCommandOptionChoice> gameTypes = new DiscordApplicationCommandOptionChoice[]
        {
            new("1v1", "OneVOne"),
            new("2v2", "TwoVTwo"),
            new("3v3", "ThreeVThree"),
            new("4v4", "FourVFour"),
        };

        public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        {
            return new ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>>(gameTypes);
        }
    }
}