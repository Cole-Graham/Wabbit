using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.BotClient.Events.Modals.Base;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Modals.Tournament
{
    /// <summary>
    /// Handles tournament creation modal submissions
    /// </summary>
    public class TournamentCreationModalHandler : ModalHandlerBase
    {
        private readonly ITournamentManagerService _tournamentManager;
        private readonly ITournamentService _tournamentService;

        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="tournamentManager">Service for managing tournaments</param>
        /// <param name="tournamentService">Service for accessing tournament data</param>
        public TournamentCreationModalHandler(
            ILogger<TournamentCreationModalHandler> logger,
            ITournamentManagerService tournamentManager,
            ITournamentService tournamentService)
            : base(logger)
        {
            _tournamentManager = tournamentManager;
            _tournamentService = tournamentService;
        }

        /// <summary>
        /// Determines if this handler can handle the given modal
        /// </summary>
        /// <param name="customId">The custom ID of the modal</param>
        /// <returns>True if this handler can handle the modal, false otherwise</returns>
        public override bool CanHandle(string customId)
        {
            return customId.StartsWith("tournament_create_modal_");
        }

        /// <summary>
        /// Handles tournament creation modal submissions
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The modal submission event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ModalSubmittedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                _logger.LogInformation("Handling tournament creation modal from {User}", e.Interaction.User.Username);

                // Extract the tournament name from the custom ID
                string tournamentName = e.Interaction.Data.CustomId.Replace("tournament_create_modal_", "");

                // Extract values from the modal
                string formatValue = e.Values["tournament_format"];
                string description = e.Values.TryGetValue("tournament_description", out var descValue) ? descValue : "";

                // Parse format
                if (!Enum.TryParse<TournamentFormat>(formatValue, true, out var format))
                {
                    await SendErrorResponseAsync(e, "Invalid tournament format specified.", hasBeenDeferred);
                    return;
                }

                // Validate tournament name
                if (string.IsNullOrWhiteSpace(tournamentName))
                {
                    await SendErrorResponseAsync(e, "Tournament name cannot be empty.", hasBeenDeferred);
                    return;
                }

                // Check if tournament already exists
                var existingTournament = _tournamentService.GetTournament(tournamentName);
                if (existingTournament != null)
                {
                    await SendErrorResponseAsync(e, $"A tournament with the name '{tournamentName}' already exists.", hasBeenDeferred);
                    return;
                }

                if (e.Interaction.User is not DiscordMember member)
                {
                    await SendErrorResponseAsync(e, "This command can only be used in a server.", hasBeenDeferred);
                    return;
                }

                // Create the tournament
                var tournament = await _tournamentManager.CreateTournamentAsync(
                    tournamentName,
                    new List<DiscordMember> { member },
                    format,
                    e.Interaction.Channel,
                    GameType.OneVsOne);

                if (tournament is null)
                {
                    await SendErrorResponseAsync(e, "Failed to create tournament.", hasBeenDeferred);
                    return;
                }

                // Success message
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Tournament Created")
                    .WithDescription($"Tournament **{tournamentName}** has been created with format **{format}**.")
                    .WithColor(DiscordColor.Green);

                if (!string.IsNullOrEmpty(description))
                {
                    embed.AddField("Description", description);
                }

                if (hasBeenDeferred)
                {
                    await e.Interaction.EditOriginalResponseAsync(
                        new DiscordWebhookBuilder().AddEmbed(embed));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().AddEmbed(embed));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentCreationModalHandler");
                await SendErrorResponseAsync(e, $"An error occurred: {ex.Message}", hasBeenDeferred);
            }
        }
    }
}