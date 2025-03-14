using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Wabbit.BotClient.Events.Handlers;

namespace Wabbit.BotClient.Events
{
    public class Event_Button(Tournament_Btn_Handlers tourneyHandlers, Game_Btn_Handlers gameHandlers) : IEventHandler<ComponentInteractionCreatedEventArgs>
    {
        private readonly Tournament_Btn_Handlers _tournamentHandlers = tourneyHandlers;
        private readonly Game_Btn_Handlers _gameHandlers = gameHandlers;

        public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            string customId = e.Id;

            switch (customId)
            {
                #region Tournament_Management
                case var _ when customId.StartsWith("signup_"):
                    await _tournamentHandlers.HandleSignupButton(sender, e);
                    break;
                case var _ when customId.StartsWith("withdraw_"):
                    await _tournamentHandlers.HandleWithdrawButton(sender, e);
                    break;
                case var _ when customId.StartsWith("cancel_signup_"):
                    await _tournamentHandlers.HandleCancelSignupButton(sender, e);
                    break;
                case var _ when customId.StartsWith("keep_signup_"):
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
                            new DiscordInteractionResponseBuilder()
                                .WithContent("Your signup has been kept."));
                    break;
                #endregion

                #region Tournament_Game
                case "btn_deck_0":
                case "btn_deck_1":
                    await _gameHandlers.HandleTournamentDeckButtonClick(e);
                    break;
                case "winner_dropdown":
                    await _gameHandlers.HandleTournamentWinnerDropdown(sender, e);
                    break;
                case "map_ban_dropdown":
                    await _gameHandlers.HandleMapBanClick(sender, e);
                    break;
                #endregion

                #region Regular 1v1
                case "btn_deck":
                    await _gameHandlers.HandleRegularDeckButtonClick(e);
                    break;
                case "1v1_winner_dropdown":
                    await _gameHandlers.HandleRegular1v1WinnerDropdown(e);
                    break;
                #endregion
            }
        }
    }
}
