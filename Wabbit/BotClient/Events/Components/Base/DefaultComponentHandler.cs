using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Wabbit.Services.Interfaces;

namespace Wabbit.BotClient.Events.Components.Base
{
    /// <summary>
    /// Default handler for component interactions that don't match any specialized handler
    /// </summary>
    public class DefaultComponentHandler : ComponentHandlerBase
    {
        /// <summary>
        /// Constructor with required dependencies
        /// </summary>
        /// <param name="logger">Logger for logging events</param>
        /// <param name="stateService">Service for accessing tournament state</param>
        public DefaultComponentHandler(ILogger<DefaultComponentHandler> logger, ITournamentStateService stateService)
            : base(logger, stateService)
        {
        }

        /// <summary>
        /// This handler can handle any component (as a fallback)
        /// </summary>
        /// <param name="customId">The custom ID of the component</param>
        /// <returns>Always returns true</returns>
        public override bool CanHandle(string customId)
        {
            return true; // Default handler can handle anything as a fallback
        }

        /// <summary>
        /// Handles any component interaction not matched by specialized handlers
        /// </summary>
        /// <param name="client">The Discord client</param>
        /// <param name="e">The component interaction event args</param>
        /// <param name="hasBeenDeferred">Whether the interaction has already been deferred</param>
        public override async Task HandleAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            _logger.LogWarning("No specialized handler found for component with ID: {CustomId}", e.Id);

            // Log the interaction for debugging purposes
            _logger.LogInformation(
                "Unhandled component interaction: User={User}, Component={Component}, Channel={Channel}",
                e.User.Username,
                e.Id,
                e.Channel.Name);

            // Send a generic response to the user
            await SendErrorResponseAsync(
                e,
                "Sorry, I couldn't process that interaction. Please try again or contact an administrator.",
                hasBeenDeferred);
        }
    }
}