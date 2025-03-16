using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using System.Linq;
using System.Collections.Generic;

namespace Wabbit.BotClient.Events.Components.Tournament
{
    /// <summary>
    /// Handler for admin-only third place match creation button
    /// </summary>
    public class AdminThirdPlaceMatchHandler : ComponentHandlerBase
    {
        private readonly ITournamentService _tournamentService;
        private readonly ITournamentPlayoffService _playoffService;

        /// <summary>
        /// Constructor for AdminThirdPlaceMatchHandler
        /// </summary>
        public AdminThirdPlaceMatchHandler(
            ILogger<AdminThirdPlaceMatchHandler> logger,
            ITournamentStateService stateService,
            ITournamentService tournamentService,
            ITournamentPlayoffService playoffService)
            : base(logger, stateService)
        {
            _tournamentService = tournamentService;
            _playoffService = playoffService;
        }

        /// <summary>
        /// Determines if this handler can handle the component interaction
        /// </summary>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("admin_create_third_place_");
        }

        /// <summary>
        /// Handles the component interaction
        /// </summary>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs args, bool hasBeenDeferred)
        {
            try
            {
                // Extract tournament ID from the component ID
                string tournamentId = args.Id.Replace("admin_create_third_place_", "");

                // Verify the user is an admin or has manage server permissions
                var member = args.Interaction.User as DiscordMember;
                if (member is null)
                {
                    await SendErrorResponseAsync(args, "Error: Unable to verify your permissions.", hasBeenDeferred);
                    return;
                }

                // Check if user has admin permissions
                bool isAdmin = member.Permissions.HasPermission(DiscordPermission.ManageGuild);
                if (!isAdmin)
                {
                    await SendErrorResponseAsync(args,
                        "You do not have permission to create a third place match. This action requires the Manage Server permission.",
                        hasBeenDeferred);
                    return;
                }

                // Get the tournament
                var tournament = _tournamentService.GetTournament(tournamentId);
                if (tournament == null)
                {
                    await SendErrorResponseAsync(args, "Error: Tournament not found.", hasBeenDeferred);
                    return;
                }

                // Check if we can create a third place match
                if (!_playoffService.CanCreateThirdPlaceMatch(tournament))
                {
                    await SendErrorResponseAsync(args,
                        "Cannot create third place match. Either the semifinals are not complete or a third place match already exists.",
                        hasBeenDeferred);
                    return;
                }

                // Defer the interaction if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(args.Interaction);
                    hasBeenDeferred = true;
                }

                try
                {
                    // Create the third place match
                    bool success = await _playoffService.CreateThirdPlaceMatchAsync(tournament, member.Id);

                    if (success)
                    {
                        // Find the newly created third place match and get match details
                        var thirdPlaceMatch = tournament.PlayoffMatches?.FirstOrDefault(m => m.Type == TournamentMatchType.PlayoffThirdPlace);
                        var finalMatch = tournament.PlayoffMatches?.FirstOrDefault(m => m.Type == TournamentMatchType.Final);
                        var semifinals = finalMatch?.NextMatch != null
                            ? tournament.PlayoffMatches?.Where(m => m.NextMatch == finalMatch).ToList()
                            : new List<Models.Tournament.Match>();

                        // Get semifinal names with null safety
                        var participant1 = semifinals?.Count >= 1 ? semifinals[0]?.Name : "Semifinal 1";
                        var participant2 = semifinals?.Count >= 2 ? semifinals[1]?.Name : "Semifinal 2";
                        var bestOf = thirdPlaceMatch?.BestOf ?? 3;

                        // Create visual confirmation embed
                        var embed = new DiscordEmbedBuilder()
                            .WithTitle("🏅 Third Place Match Created")
                            .WithDescription("A third place match has been added to the tournament bracket.")
                            .AddField("Format", $"Best of {bestOf}", true)
                            .AddField("Participants", "Semifinal losers", true)
                            .AddField("Potential Matchup", $"Loser of {participant1} vs Loser of {participant2}")
                            .WithColor(DiscordColor.Gold)
                            .WithFooter($"Created by {member.Username} • {DateTime.Now:yyyy-MM-dd HH:mm}");

                        // Save the tournament state immediately
                        await _stateService.SaveTournamentStateAsync(client);

                        // Update the tournament display
                        await _tournamentService.UpdateTournamentDisplayAsync(tournament);

                        // Inform the admin with detailed info
                        if (hasBeenDeferred)
                        {
                            await args.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().AddEmbed(embed));
                        }
                        else
                        {
                            await args.Interaction.CreateResponseAsync(
                                DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().AddEmbed(embed));
                        }

                        // Announce in the channel with more detailed notification
                        await args.Channel.SendMessageAsync(
                            new DiscordMessageBuilder()
                                .WithContent($"🏅 **Tournament Update**: A third place match has been added to the tournament by {member.Mention}. This match will use a Best-of-{bestOf} format between the semifinal losers.")
                                .WithAllowedMentions([new UserMention(member.Id)]));
                    }
                    else
                    {
                        await SendErrorResponseAsync(args,
                            "Failed to create third place match. Please check logs for details.",
                            hasBeenDeferred);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Transaction error creating third place match");
                    await SendErrorResponseAsync(args,
                        "An error occurred during the third place match creation process. The operation may not have completed successfully.",
                        hasBeenDeferred);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling admin third place match creation");
                await SendErrorResponseAsync(args,
                    "An error occurred while processing your request.",
                    hasBeenDeferred);
            }
        }
    }
}