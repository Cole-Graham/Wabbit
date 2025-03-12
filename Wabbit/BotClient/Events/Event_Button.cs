using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Wabbit.BotClient.Events
{
    public class Event_Button : IEventHandler<ComponentInteractionCreatedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly TournamentManager _tournamentManager;
        private readonly ILogger<Event_Button> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public Event_Button(OngoingRounds roundsHolder, TournamentManager tournamentManager, ILogger<Event_Button> logger, IServiceScopeFactory scopeFactory)
        {
            _roundsHolder = roundsHolder;
            _tournamentManager = tournamentManager;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    try
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                    }
                    catch (Exception respEx)
                    {
                        Console.WriteLine($"Failed to defer response: {respEx.Message}. Continuing execution...");
                    }

                    string customId = e.Id;

                    if (customId.StartsWith("signup_"))
                    {
                        await HandleSignupButton(sender, e);
                    }
                    else if (customId.StartsWith("withdraw_"))
                    {
                        await HandleWithdrawButton(sender, e);
                    }
                    else if (customId.StartsWith("cancel_signup_"))
                    {
                        await HandleCancelSignupButton(sender, e);
                    }
                    else if (customId.StartsWith("keep_signup_"))
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
                            new DiscordInteractionResponseBuilder()
                                .WithContent("Your signup has been kept."));
                    }
                    // Other button types would be handled here
                    else
                    {
                        Console.WriteLine($"Unhandled button interaction with ID: {customId}");
                    }

                    if (_roundsHolder is null || _roundsHolder.TourneyRounds is null)
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().WithContent("Tournament rounds are not initialized"));
                        return;
                    }

                    List<Round> tourneyRounds = _roundsHolder.TourneyRounds;
                    Round? round = tourneyRounds.Where(t => t is not null && t.Teams is not null && t.Teams.Any(team => team is not null && team.Participants is not null &&
                        team.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id))).FirstOrDefault();

                    if (ConfigManager.Config is null || ConfigManager.Config.Servers is null || !ConfigManager.Config.Servers.Any())
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().WithContent("Server configuration is missing"));
                        return;
                    }

                    var server = ConfigManager.Config.Servers.Where(s => s?.ServerId != null && s.ServerId == e.Guild?.Id).FirstOrDefault();
                    if (server == null)
                    {
                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().WithContent("Server configuration not found"));
                        return;
                    }

                    var deckChannelId = server.DeckChannelId;

                    if (round is not null && round.Teams != null)
                    {
                        List<Round.Team> teams = round.Teams;
                        Round.Team? team1 = round.Teams.Where(t => t is not null && t.Participants is not null &&
                            t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id)).FirstOrDefault();

                        if (team1 == null)
                        {
                            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().WithContent("Your team was not found"));
                            return;
                        }

                        Round.Team? team2 = round.Teams.Where(t => t is not null && t.Name is not null && t.Name != team1.Name).FirstOrDefault();
                        if (team2 == null)
                        {
                            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().WithContent("Opponent team was not found"));
                            return;
                        }

                        Round.Participant? participant = team1.Participants?.Where(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id).FirstOrDefault();
                        if (participant == null)
                        {
                            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource,
                                new DiscordInteractionResponseBuilder().WithContent("Your participant data was not found"));
                            return;
                        }

                        switch (e.Id)
                        {
                            case string s when s.StartsWith("btn_deck_"):
                                if (round.InGame == true)
                                {
                                    var response = new DiscordInteractionResponseBuilder().WithContent("Game is in progress. Deck submitting is disabled");
                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.ChannelMessageWithSource, response);
                                }
                                else
                                {
                                    var modal = new DiscordInteractionResponseBuilder()
                                    {
                                        Title = "Enter a deck code",
                                        CustomId = "deck_modal"
                                    };
                                    modal.AddComponents(new DiscordTextInputComponent("Deck code", "deck_code", required: true));

                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
                                }
                                break;

                            case "winner_dropdown":
                                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                                if (e.Values == null || !e.Values.Any())
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("No winner selected"));
                                    return;
                                }

                                var winner = teams.Where(t => t is not null && t.Name is not null && t.Name == e.Values[0]).FirstOrDefault();
                                if (winner == null)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Winner team not found"));
                                    return;
                                }

                                winner.Wins += 1;
                                var loser = teams.Where(t => t is not null && t.Name is not null && t.Name != e.Values[0]).FirstOrDefault();
                                if (loser == null)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Loser team not found"));
                                    return;
                                }

                                round.InGame = false;

                                if (round.Maps == null || round.Cycle >= round.Maps.Count)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Map data is missing or invalid"));
                                    return;
                                }

                                var map = round.Maps[round.Cycle];
                                round.Cycle += 1;

                                var embedResult = new DiscordEmbedBuilder()
                                {
                                    Title = $"**{round.Name ?? "Round"}** - Game {round.Cycle} Results"
                                };
                                embedResult.AddField("Map", map ?? "Unknown");

                                foreach (var teamItem in teams)
                                    embedResult.AddField(teamItem?.Name ?? "Unknown Team", teamItem?.Wins.ToString() ?? "0");

                                await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddEmbed(embedResult));

                                var embedDeck = new DiscordEmbedBuilder()
                                {
                                    Title = $"**{round.Name ?? "Round"}**, Game {round.Cycle}"
                                };

                                foreach (var teamItem in teams)
                                {
                                    if (teamItem is not null && teamItem.Participants is not null)
                                    {
                                        foreach (var p in teamItem.Participants)
                                        {
                                            if (p is not null && p.Player is not null)
                                                embedDeck.AddField(p.Player.DisplayName ?? "Unknown Player", p.Deck ?? "No deck");
                                        }
                                    }
                                }

                                if (e.Guild is not null && deckChannelId.HasValue)
                                {
                                    var deckChannel = await e.Guild.GetChannelAsync((ulong)deckChannelId);
                                    if (deckChannel is not null)
                                        await sender.SendMessageAsync(deckChannel, embedDeck);
                                }

                                int scoreLimit;

                                switch (round.Length)
                                {
                                    default:
                                        scoreLimit = 1;
                                        break;
                                    case 3:
                                        scoreLimit = 2;
                                        break;
                                    case 5:
                                        scoreLimit = 3;
                                        break;
                                }

                                if (winner.Wins == scoreLimit)
                                {
                                    var followup = new DiscordFollowupMessageBuilder().WithContent("Round is concluded");
                                    await e.Interaction.CreateFollowupMessageAsync(followup);

                                    if (team1.Thread is not null)
                                        await team1.Thread.DeleteAsync();
                                    if (team2.Thread is not null)
                                        await team2.Thread.DeleteAsync();

                                    _roundsHolder.TourneyRounds?.Remove(round);
                                }
                                else
                                {
                                    if (team1.Thread is not null && team2.Thread is not null)
                                    {
                                        List<DiscordThreadChannel> threads = [team1.Thread, team2.Thread];

                                        if (team1.Participants != null)
                                        {
                                            foreach (var p in team1.Participants)
                                            {
                                                if (p is not null)
                                                {
                                                    p.Deck = null;
                                                    if (p.Player is not null)
                                                        await sender.SendMessageAsync(team1.Thread, $"{p.Player.Mention} Please submit your deck for Game {round.Cycle + 1}");
                                                }
                                            }
                                        }

                                        if (team2.Participants != null)
                                        {
                                            foreach (var p in team2.Participants)
                                            {
                                                if (p is not null)
                                                {
                                                    p.Deck = null;
                                                    if (p.Player is not null)
                                                        await sender.SendMessageAsync(team2.Thread, $"{p.Player.Mention} Please submit your deck for Game {round.Cycle + 1}");
                                                }
                                            }
                                        }
                                    }
                                }

                                // Track the played map
                                string currentMapName = round.Maps[round.Cycle - 1];

                                // Save tournament state to record the played map
                                await _tournamentManager.SaveTournamentState(sender);
                                break;

                            case "map_ban_dropdown":
                                var teamForMapBan = teams.Where(t => t is not null && t.Participants is not null &&
                                    t.Participants.Any(p => p is not null && p.Player is not null && p.Player.Id == e.User.Id)).FirstOrDefault();

                                if (teamForMapBan == null)
                                {
                                    await sender.SendMessageAsync(e.Channel, $"{e.User.Mention} you're not allowed to interact with this component");
                                }
                                else
                                {
                                    // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                    try
                                    {
                                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Interaction might already be acknowledged
                                        Console.WriteLine($"Could not defer interaction: {ex.Message}");
                                    }

                                    if (e.Channel is not null)
                                    {
                                        var channel = await sender.GetChannelAsync(e.Channel.Id);
                                        if (channel is not null && e.Message is not null)
                                        {
                                            var dropdown = await channel.GetMessageAsync(e.Message.Id);
                                            if (dropdown is not null && e.Values is not null)
                                            {
                                                List<string> bannedMaps = e.Values.ToList();

                                                // Store map bans temporarily
                                                teamForMapBan.MapBans = bannedMaps;

                                                // Create confirmation embed with clear priority order
                                                var confirmEmbed = new DiscordEmbedBuilder()
                                                {
                                                    Title = "Review Map Ban Selections",
                                                    Description = "Please review your map ban selections below. The order represents your ban priority.\n\n**You can change your selections by clicking the Revise button.**",
                                                    Color = DiscordColor.Orange
                                                };

                                                for (int i = 0; i < bannedMaps.Count; i++)
                                                {
                                                    confirmEmbed.AddField($"Priority #{i + 1}", bannedMaps[i] ?? "Unknown", true);
                                                }

                                                // Add confirmation and revision buttons
                                                var confirmBtn = new DiscordButtonComponent(DiscordButtonStyle.Success, $"confirm_map_bans_{teams.IndexOf(teamForMapBan)}", "Confirm Selections");
                                                var reviseBtn = new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"revise_map_bans_{teams.IndexOf(teamForMapBan)}", "Revise Selections");

                                                try
                                                {
                                                    // Modify the existing message instead of sending a new one
                                                    await dropdown.ModifyAsync(new DiscordMessageBuilder()
                                                        .WithContent("Please confirm your map ban selections:")
                                                        .AddEmbed(confirmEmbed)
                                                        .AddComponents(confirmBtn, reviseBtn));

                                                    // Try to send a notification, but don't fail if it doesn't work
                                                    try
                                                    {
                                                        await channel.SendMessageAsync($"{e.User.Mention} Your map ban selections have been recorded. Please confirm or revise them.");
                                                    }
                                                    catch (Exception notifyEx)
                                                    {
                                                        Console.WriteLine($"Could not send notification: {notifyEx.Message}");
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine($"Error updating map ban message: {ex.Message}");
                                                    try
                                                    {
                                                        await channel.SendMessageAsync($"{e.User.Mention} Error updating map bans: {ex.Message}");
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            case string s when s.StartsWith("confirm_map_bans_"):
                                int teamIndex = int.Parse(s.Replace("confirm_map_bans_", ""));
                                var team = teams[teamIndex];

                                // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                try
                                {
                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                }
                                catch (Exception ex)
                                {
                                    // Interaction might already be acknowledged
                                    Console.WriteLine($"Could not defer confirmation interaction: {ex.Message}");
                                }

                                try
                                {
                                    // Create final embed showing confirmed bans
                                    var finalEmbed = new DiscordEmbedBuilder()
                                    {
                                        Title = "Map Bans Confirmed",
                                        Description = "Your map ban selections have been confirmed with the following priority order:",
                                        Color = DiscordColor.Green
                                    };

                                    if (team.MapBans != null)
                                    {
                                        for (int i = 0; i < team.MapBans.Count; i++)
                                        {
                                            finalEmbed.AddField($"Priority #{i + 1}", team.MapBans[i] ?? "Unknown", true);
                                        }
                                    }

                                    // Add deck submission button
                                    var deckBtn = new DiscordButtonComponent(DiscordButtonStyle.Primary, $"btn_deck_{teamIndex}", "Submit Deck");

                                    // Update message
                                    if (e.Message is not null)
                                    {
                                        await e.Message.ModifyAsync(new DiscordMessageBuilder()
                                            .WithContent("Map bans confirmed! Please submit your deck next.")
                                            .AddEmbed(finalEmbed)
                                            .AddComponents(deckBtn));
                                    }

                                    // Try to send a notification, but don't fail if it doesn't work
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} Your map ban selections have been confirmed! Please submit your deck next.");
                                        }
                                        catch (Exception notifyEx)
                                        {
                                            Console.WriteLine($"Could not send confirmation notification: {notifyEx.Message}");
                                        }
                                    }

                                    // Send notification to bot channel
                                    if (e.Guild is not null && server.BotChannelId.HasValue)
                                    {
                                        var botChannel = await e.Guild.GetChannelAsync((ulong)server.BotChannelId);
                                        if (botChannel is not null)
                                        {
                                            var msg = await sender.SendMessageAsync(botChannel, $"{team.Name ?? "A team"} has confirmed their map ban selections");
                                            if (msg is not null && round.MsgToDel is not null)
                                                round.MsgToDel.Add(msg);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error confirming map bans: {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} Error confirming map bans: {ex.Message}");
                                        }
                                        catch { }
                                    }
                                }

                                // Save tournament state
                                await _tournamentManager.SaveTournamentState(sender);
                                break;

                            case string s when s.StartsWith("revise_map_bans_"):
                                int reviseTeamIndex = int.Parse(s.Replace("revise_map_bans_", ""));
                                var reviseTeam = teams[reviseTeamIndex];

                                // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                try
                                {
                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                }
                                catch (Exception ex)
                                {
                                    // Interaction might already be acknowledged
                                    Console.WriteLine($"Could not defer revision interaction: {ex.Message}");
                                }

                                try
                                {
                                    // Get the map pool from the tournament manager
                                    var mapPool = _tournamentManager.GetTournamentMapPool(round.OneVOne);

                                    var mapSelectOptions = new List<DiscordSelectComponentOption>();
                                    foreach (var mapName in mapPool)
                                    {
                                        if (mapName is not null)
                                        {
                                            var option = new DiscordSelectComponentOption(mapName, mapName);
                                            mapSelectOptions.Add(option);
                                        }
                                    }

                                    // Recreate the dropdown based on the round length
                                    DiscordSelectComponent newDropdown;
                                    int selectionCount = round.Length == 5 ? 2 : 3; // Bo5 = 2 bans, others = 3 bans

                                    newDropdown = new DiscordSelectComponent("map_ban_dropdown", "Select maps to ban", mapSelectOptions, false, selectionCount, selectionCount);

                                    // Create a descriptive message based on the length
                                    string instructionMsg;
                                    if (round.Length == 3)
                                    {
                                        instructionMsg = "**Scroll to see all map options!**\n\n" +
                                            "Choose 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                            "Only 2 maps from each team will be banned, leaving 4 remaining maps. One of the 3rd priority maps " +
                                            "selected will be randomly banned in case both teams ban the same map. " +
                                            "You will not know which maps were banned by your opponent, and the remaining maps will be revealed " +
                                            "randomly before each game after deck codes have been locked in.";
                                    }
                                    else if (round.Length == 5)
                                    {
                                        instructionMsg = "**Scroll to see all map options!**\n\n" +
                                            "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                            "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                                            "One of the 2nd priority maps selected by each team will be randomly banned. " +
                                            "You will not know which maps were banned by your opponent, " +
                                            "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.";
                                    }
                                    else
                                    {
                                        instructionMsg = "**Scroll to see all map options!**\n\n" +
                                            "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!";
                                    }

                                    // Reset and show dropdown again
                                    if (e.Message is not null)
                                    {
                                        await e.Message.ModifyAsync(new DiscordMessageBuilder()
                                            .WithContent(instructionMsg)
                                            .AddComponents(newDropdown));
                                    }

                                    // Try to send a notification, but don't fail if it doesn't work
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} You're now revising your map ban selections. Please select maps again from the dropdown.");
                                        }
                                        catch (Exception notifyEx)
                                        {
                                            Console.WriteLine($"Could not send revision notification: {notifyEx.Message}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error revising map bans: {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} Error revising map bans: {ex.Message}");
                                        }
                                        catch { }
                                    }
                                }

                                break;
                        }
                    }
                    else
                    {
                        switch (e.Id)
                        {
                            case string s when s.StartsWith("btn_deck"):
                                var modal = new DiscordInteractionResponseBuilder();
                                modal.WithTitle("Enter a deck code")
                                    .WithCustomId("deck_modal")
                                    .AddComponents(new DiscordTextInputComponent("Deck code", "deck_code", required: true));

                                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
                                break;

                            case "1v1_winner_dropdown":
                                await e.Interaction.DeferAsync();

                                if (_roundsHolder.RegularRounds == null)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("Regular rounds are not initialized"));
                                    return;
                                }

                                var regularRound = _roundsHolder.RegularRounds.Where(p => p is not null && p.Player1 is not null && p.Player2 is not null &&
                                    (p.Player1.Id == e.User.Id || p.Player2.Id == e.User.Id)).FirstOrDefault();

                                if (regularRound == null)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent($"{e.User.Mention} you're not a participant of this game"));
                                }
                                else
                                {
                                    if (e.Values == null || !e.Values.Any())
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder().WithContent("No winner selected"));
                                        return;
                                    }

                                    var winner = e.Values[0];
                                    var embed = new DiscordEmbedBuilder()
                                    {
                                        Title = "Deck codes"
                                    };

                                    if (regularRound.Player1 is not null)
                                        embed.AddField(regularRound.Player1.Username ?? "Player 1", regularRound.Deck1 ?? "No deck");

                                    if (regularRound.Player2 is not null)
                                        embed.AddField(regularRound.Player2.Username ?? "Player 2", regularRound.Deck2 ?? "No deck");

                                    await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent($"The winner of this round is {winner}").AddEmbed(embed));

                                    if (regularRound.Messages is not null && regularRound.Messages.Any() && e.Channel is not null)
                                    {
                                        await e.Channel.DeleteMessageAsync(regularRound.Messages.First());
                                        _roundsHolder.RegularRounds.Remove(regularRound);
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling component interaction: {ex.Message}\n{ex.StackTrace}");
                }
            });

            return Task.CompletedTask;
        }

        private async Task HandleSignupButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                // Immediately acknowledge the interaction to prevent timeouts
                // Give Discord some time to prepare for the interaction
                await Task.Delay(500);

                try
                {
                    // Then try to create the response
                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                    // Give Discord some more time after the response
                    await Task.Delay(1000);
                }
                catch (Exception deferEx)
                {
                    // Failed to defer signup button response, will try to continue
                    _logger.LogWarning($"Failed to defer signup button response: {deferEx.Message}");
                }

                // Extract the tournament name from the button ID (format: signup_TournamentName)
                string tournamentName = e.Id.Substring("signup_".Length);

                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

                if (signup == null)
                {
                    try
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                            .WithContent($"Signup '{tournamentName}' not found. It may have been removed.")
                            .AsEphemeral(true));
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send followup message: {followupEx.Message}");
                        // Try direct channel message as fallback
                        await e.Channel.SendMessageAsync($"Signup '{tournamentName}' not found. It may have been removed.");
                    }
                    return;
                }

                if (!signup.IsOpen)
                {
                    try
                    {
                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                            .WithContent($"Signup for '{tournamentName}' is closed.")
                            .AsEphemeral(true));
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send followup message: {followupEx.Message}");
                        await e.Channel.SendMessageAsync($"Signup for '{tournamentName}' is closed.");
                    }
                    return;
                }

                // Check if the user is already signed up
                var member = (DiscordMember)e.User;
                var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == member.Id);

                if (existingParticipant is not null)
                {
                    // User is already signed up - handle cancellation
                    // Create a confirmation message with buttons
                    try
                    {
                        var confirmationMessage = new DiscordFollowupMessageBuilder()
                            .WithContent($"You are already signed up for tournament '{tournamentName}'. Would you like to cancel your signup?")
                            .AddComponents(
                                new DiscordButtonComponent(DiscordButtonStyle.Danger, $"cancel_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Cancel Signup"),
                                new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"keep_signup_{tournamentName.Replace(" ", "_")}_{member.Id}", "Keep Signup")
                            )
                            .AsEphemeral(true);

                        await e.Interaction.CreateFollowupMessageAsync(confirmationMessage);
                    }
                    catch (Exception followupEx)
                    {
                        _logger.LogWarning($"Failed to send confirmation message: {followupEx.Message}");
                        await e.Channel.SendMessageAsync($"You are already signed up for tournament '{tournamentName}'.");
                    }
                    return;
                }

                // Add the user to the signup
                var newParticipantsList = new List<DiscordMember>(signup.Participants);

                // Add the new player
                newParticipantsList.Add(member);

                // Replace the participants list in the signup
                signup.Participants = newParticipantsList;

                // Log signup using logger instead of console
                _logger.LogInformation($"Added participant {member.Username} to signup '{signup.Name}' (now has {signup.Participants.Count} participants)");

                // Save the updated signup
                _tournamentManager.UpdateSignup(signup);

                // Update the signup message
                await UpdateSignupMessage(sender, signup);

                try
                {
                    // Create the followup message with ephemeral setting and store the response
                    var response = await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You have been added to the tournament '{tournamentName}'.")
                        .AsEphemeral(true));

                    // Schedule deletion after 10 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10000); // 10 seconds
                        try
                        {
                            await response.DeleteAsync();
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogWarning($"Failed to delete confirmation message: {deleteEx.Message}");
                        }
                    });
                }
                catch (Exception followupEx)
                {
                    _logger.LogWarning($"Failed to send confirmation message: {followupEx.Message}");
                    await e.Channel.SendMessageAsync($"You have been added to the tournament '{tournamentName}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling signup button: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"An error occurred: {ex.Message}")
                        .AsEphemeral(true));
                }
                catch (Exception msgEx)
                {
                    _logger.LogWarning($"Failed to send error message: {msgEx.Message}");
                    try
                    {
                        // Direct message as a final fallback
                        await e.Channel.SendMessageAsync($"An error occurred processing your signup: {ex.Message}");
                    }
                    catch
                    {
                        _logger.LogError("Failed to send any error messages");
                    }
                }
            }
        }

        // Add a method to handle the cancel/keep signup buttons
        private async Task HandleCancelSignupButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                // Immediately acknowledge the interaction
                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

                // Parse the button ID to get tournament name and user ID
                // Format: cancel_signup_TournamentName_UserId
                string[] parts = e.Id.Split('_');
                if (parts.Length < 4)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Invalid button ID format.")
                        .AsEphemeral(true));
                    return;
                }

                // Get tournament name (may have underscores in it)
                string tournamentName = string.Join(" ", parts.Skip(2).Take(parts.Length - 3));

                // Get user ID from the last part
                if (!ulong.TryParse(parts[parts.Length - 1], out ulong userId))
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Invalid user ID in button.")
                        .AsEphemeral(true));
                    return;
                }

                // Make sure the user who clicked is the same as the user in the button
                if (e.User.Id != userId)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("You can only cancel your own signup.")
                        .AsEphemeral(true));
                    return;
                }

                // Find the signup using the TournamentManager and ensure participants are loaded
                var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

                if (signup == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"Signup '{tournamentName}' not found. It may have been removed.")
                        .AsEphemeral(true));
                    return;
                }

                // Remove the user from the signup
                var participant = signup.Participants.FirstOrDefault(p => p.Id == userId);
                if (participant is not null)
                {
                    var newParticipantsList = new List<DiscordMember>();

                    // Add all participants except the one to be removed
                    foreach (var p in signup.Participants)
                    {
                        if (p.Id != userId)
                        {
                            newParticipantsList.Add(p);
                        }
                    }

                    // Replace the participants list in the signup
                    signup.Participants = newParticipantsList;

                    // Save the updated signup
                    _tournamentManager.UpdateSignup(signup);

                    // Update the signup message
                    await UpdateSignupMessage(sender, signup);

                    // Send confirmation message and schedule deletion
                    var response = await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You have been removed from the tournament '{tournamentName}'.")
                        .AsEphemeral(true));

                    // Schedule deletion after 10 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10000); // 10 seconds
                        try
                        {
                            await response.DeleteAsync();
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogWarning($"Failed to delete confirmation message: {deleteEx.Message}");
                        }
                    });
                }
                else
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"You were not found in the participants list for '{tournamentName}'.")
                        .AsEphemeral(true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling cancel signup button: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent($"An error occurred: {ex.Message}")
                        .AsEphemeral(true));
                }
                catch (Exception msgEx)
                {
                    _logger.LogError($"Failed to send error message: {msgEx.Message}");
                }
            }
        }

        private async Task UpdateSignupMessage(DiscordClient sender, TournamentSignup signup)
        {
            if (signup.SignupChannelId != 0 && signup.MessageId != 0)
            {
                try
                {
                    Console.WriteLine($"Updating signup message for '{signup.Name}' with {signup.Participants.Count} participants");

                    // Get the channel and message
                    var channel = await sender.GetChannelAsync(signup.SignupChannelId);
                    var message = await channel.GetMessageAsync(signup.MessageId);

                    // Update the embed with the new participant list
                    DiscordEmbed updatedEmbed = CreateSignupEmbed(signup);

                    // Create components based on signup status
                    var components = new List<DiscordComponent>();
                    if (signup.IsOpen)
                    {
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Success,
                            $"signup_{signup.Name}",
                            "Sign Up"
                        ));

                        // Add withdraw button so participants can withdraw
                        components.Add(new DiscordButtonComponent(
                            DiscordButtonStyle.Danger,
                            $"withdraw_{signup.Name}",
                            "Withdraw"
                        ));
                    }

                    // Update the message
                    var builder = new DiscordMessageBuilder()
                        .AddEmbed(updatedEmbed)
                        .AddComponents(components);

                    await message.ModifyAsync(builder);
                    Console.WriteLine($"Successfully updated signup message for '{signup.Name}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating signup message: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                Console.WriteLine($"Cannot update signup message: Missing channel ID ({signup.SignupChannelId}) or message ID ({signup.MessageId})");
            }
        }

        private DiscordEmbed CreateSignupEmbed(TournamentSignup signup)
        {
            var builder = new DiscordEmbedBuilder()
                .WithTitle($"🏆 Tournament Signup: {signup.Name}")
                .WithDescription("Sign up for this tournament by clicking the button below.")
                .WithColor(new DiscordColor(75, 181, 67))
                .AddField("Format", signup.Format.ToString(), true)
                .AddField("Status", signup.IsOpen ? "Open" : "Closed", true)
                .AddField("Created By", signup.CreatedBy?.Username ?? signup.CreatorUsername, true)
                .WithTimestamp(signup.CreatedAt);

            if (signup.ScheduledStartTime.HasValue)
            {
                // Convert DateTime to PST correctly
                // First convert to UTC to ensure we're working from a common reference point
                DateTime utcTime = signup.ScheduledStartTime.Value.ToUniversalTime();

                // Pacific Standard Time is UTC-8 (or UTC-7 during daylight saving)
                TimeZoneInfo pstZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                DateTime pstTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, pstZone);

                // Format with proper AM/PM indicator
                string pstFormatted = pstTime.ToString("MMM d, yyyy h:mm tt") + " PST";

                // Create Discord timestamp using the original UTC time to ensure accuracy
                long unixTimestamp = ((DateTimeOffset)utcTime).ToUnixTimeSeconds();
                string discordTimestampFull = $"<t:{unixTimestamp}:F>";
                string discordTimestampFriendly = $"<t:{unixTimestamp}:f>";

                builder.AddField("Scheduled Start (PST)", pstFormatted, false);
                builder.AddField("Scheduled Start (Local Time)", discordTimestampFriendly, false);
            }

            // Log the number of participants for debugging
            Console.WriteLine($"Creating embed for signup '{signup.Name}' with {signup.Participants.Count} participants");

            if (signup.Participants != null && signup.Participants.Count > 0)
            {
                // Create a list of participant usernames
                var participantNames = new List<string>();
                foreach (var participant in signup.Participants)
                {
                    try
                    {
                        participantNames.Add($"{participantNames.Count + 1}. {participant.Username}");
                        Console.WriteLine($"  - Adding participant to embed: {participant.Username} (ID: {participant.Id})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error adding participant to embed: {ex.Message}");
                    }
                }

                // Join the names with newlines
                string participants = string.Join("\n", participantNames);

                // Add the field with all participants
                builder.AddField($"Participants ({signup.Participants.Count})", participants, false);
            }
            else
            {
                builder.AddField("Participants (0)", "No participants yet", false);
            }

            return builder.Build();
        }

        private async Task HandleWithdrawButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            // Extract tournament name from customId
            string tournamentName = e.Id.Substring("withdraw_".Length);

            // Find the tournament and ensure participants are loaded
            var signup = await _tournamentManager.GetSignupWithParticipants(tournamentName, sender);

            if (signup == null)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"Tournament signup '{tournamentName}' not found.")
                        .AsEphemeral(true)
                );
                return;
            }

            if (!signup.IsOpen)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"This tournament signup is closed and no longer accepting changes.")
                        .AsEphemeral(true)
                );
                return;
            }

            var user = e.User as DiscordMember;

            // Check if user is already signed up
            var existingParticipant = signup.Participants.FirstOrDefault(p => p.Id == user?.Id);
            if (existingParticipant is null)
            {
                await e.Interaction.CreateFollowupMessageAsync(
                    new DiscordFollowupMessageBuilder()
                        .WithContent($"You're not signed up for the '{signup.Name}' tournament.")
                        .AsEphemeral(true)
                );
                return;
            }

            // Remove the participant
            var newParticipantsList = new List<DiscordMember>();

            // Add all participants except the one to be removed
            foreach (var p in signup.Participants)
            {
                if (p.Id != user?.Id)
                {
                    newParticipantsList.Add(p);
                }
            }

            // Replace the participants list in the signup
            signup.Participants = newParticipantsList;

            // Save the updated signup
            _tournamentManager.UpdateSignup(signup);

            // Update the signup message
            await UpdateSignupMessage(sender, signup);

            // Send confirmation message and schedule deletion
            var response = await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                .WithContent($"You have been removed from the '{signup.Name}' tournament.")
                .AsEphemeral(true));

            // Schedule deletion after 10 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000); // 10 seconds
                try
                {
                    await response.DeleteAsync();
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning($"Failed to delete confirmation message: {deleteEx.Message}");
                }
            });

            // Log the withdrawal
            _logger.LogInformation($"User {e.Interaction.User.Username} withdrawn from tournament '{signup.Name}'");
        }
    }
}
