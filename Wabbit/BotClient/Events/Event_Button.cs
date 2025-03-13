using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Wabbit.BotClient.Config;
using Wabbit.BotClient.Commands;
using Wabbit.Misc;
using Wabbit.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.IO;
using Wabbit.Services.Interfaces;
using System.Text;
using Wabbit.Services;

namespace Wabbit.BotClient.Events
{
    public class Event_Button : IEventHandler<ComponentInteractionCreatedEventArgs>
    {
        private readonly OngoingRounds _roundsHolder;
        private readonly TournamentManager _tournamentManager;
        private readonly ILogger<Event_Button> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITournamentGameService _tournamentGameService;
        private readonly ITournamentMatchService _tournamentMatchService;

        public Event_Button(
            OngoingRounds roundsHolder,
            TournamentManager tournamentManager,
            ILogger<Event_Button> logger,
            IServiceScopeFactory scopeFactory,
            ITournamentGameService tournamentGameService,
            ITournamentMatchService tournamentMatchService)
        {
            _roundsHolder = roundsHolder;
            _tournamentManager = tournamentManager;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _tournamentGameService = tournamentGameService;
            _tournamentMatchService = tournamentMatchService;
        }

        // Helper method to safely defer interactions
        private async Task SafeDeferAsync(DiscordInteraction interaction)
        {
            try
            {
                await interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
            }
            catch (Exception ex)
            {
                // Just log and continue - the interaction might already have been responded to
                _logger.LogWarning($"Could not defer interaction: {ex.Message}");
            }
        }

        public Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                // Track if the interaction has been deferred
                bool hasBeenDeferred = false;

                try
                {
                    // Try to defer the interaction using our helper method
                    try
                    {
                        await SafeDeferAsync(e.Interaction);
                        hasBeenDeferred = true;
                    }
                    catch (Exception)
                    {
                        // SafeDeferAsync already logs the error
                        // Don't return here - continue with handling the interaction
                    }

                    string customId = e.Id;

                    // Special cases for confirm and revise deck buttons
                    if (customId.StartsWith("confirm_deck_") || customId.StartsWith("revise_deck_"))
                    {
                        // These need special handling because they can be created from Event_MessageCreated
                        if (customId.StartsWith("confirm_deck_"))
                        {
                            await HandleConfirmDeckButton(sender, e, hasBeenDeferred);
                        }
                        else // revise_deck_
                        {
                            await HandleReviseDeckButton(sender, e, hasBeenDeferred);
                        }
                        return;
                    }

                    // Special case for game winner dropdown
                    if (customId == "tournament_game_winner_dropdown")
                    {
                        await HandleGameWinnerDropdown(sender, e, hasBeenDeferred);
                    }

                    // Handle other button types
                    if (customId.StartsWith("join_tournament_"))
                    {
                        await HandleJoinTournamentButton(sender, e);
                    }
                    else if (customId.StartsWith("signup_"))
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
                        if (!hasBeenDeferred)
                        {
                            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage,
                                new DiscordInteractionResponseBuilder()
                                    .WithContent("Your signup has been kept."));
                        }
                        else
                        {
                            await e.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().WithContent("Your signup has been kept."));
                        }
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
                                try
                                {
                                    // We already attempted to defer at the beginning of the method,
                                    // so we don't need to try again here

                                    if (round.InGame == true)
                                    {
                                        if (e.Channel is not null)
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} Game is in progress. Deck submitting is disabled");
                                        }
                                    }
                                    else
                                    {
                                        // Extract team index from button ID
                                        int teamIdx = int.Parse(s.Replace("btn_deck_", ""));

                                        // Find the participant in the team
                                        var participantTeam = teams[teamIdx];
                                        var userParticipant = participantTeam.Participants?.FirstOrDefault(p =>
                                            p is not null && p.Player is not null && p.Player.Id == e.User.Id);

                                        if (userParticipant != null && e.Channel is not null)
                                        {
                                            // Create a message with instructions for deck submission
                                            var promptMessage = new DiscordMessageBuilder()
                                                .WithContent($"{e.User.Mention} Please enter your deck code in a reply to this message.\n\n" +
                                                            "After submitting, you'll be able to review and confirm your deck code.");

                                            var deckPrompt = await e.Channel.SendMessageAsync(promptMessage);

                                            // Log the action
                                            Console.WriteLine($"Sent deck submission prompt to user {e.User.Username} ({e.User.Id})");

                                            // The detailed instruction message is already in the deck prompt above
                                        }
                                        else
                                        {
                                            if (e.Channel is not null)
                                            {
                                                await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your participant data");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error handling deck button: {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error processing your deck submission request. " +
                                                "Please type your deck code directly in this channel.");
                                        }
                                        catch { }
                                    }
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

                                // Get tournament and match information
                                var tournamentMatchResult = FindTournamentMatchForRound(round);
                                if (tournamentMatchResult == null)
                                {
                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                        .WithContent("Could not find the tournament match."));
                                    return;
                                }

                                // Use deconstruction to get tournament and match
                                var (tournamentObj, matchObj) = tournamentMatchResult.Value;

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
                                            if (p is not null && p.Player is not null && p.Player.Id == e.User.Id)
                                            {
                                                string title = "Player";
                                                if (p.Player is DiscordMember dMember)
                                                    title = dMember.DisplayName;

                                                embedDeck.AddField(title, $"Deck code: {p.Deck}");

                                                if (map != null)
                                                {
                                                    // Store this deck code in the participant's history for this map
                                                    if (p.Deck != null)
                                                    {
                                                        p.DeckHistory[map] = p.Deck;
                                                    }

                                                    // Also store in tournament match data for verification
                                                    if (tournamentObj != null && matchObj != null && matchObj.Result != null)
                                                    {
                                                        string playerId = p.Player is DiscordMember member ? member.Id.ToString() : "unknown";

                                                        if (!matchObj.Result.DeckCodes.ContainsKey(playerId))
                                                            matchObj.Result.DeckCodes[playerId] = new Dictionary<string, string>();

                                                        if (p.Deck != null)
                                                        {
                                                            matchObj.Result.DeckCodes[playerId][map] = p.Deck;
                                                        }
                                                    }
                                                }
                                            }
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
                                string currentMap = round.Maps[round.Cycle - 1];

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

                                    // Update message without deck button
                                    if (e.Message is not null)
                                    {
                                        await e.Message.ModifyAsync(new DiscordMessageBuilder()
                                            .WithContent("Map bans confirmed! Please submit your deck next using `/tournament submit_deck`")
                                            .AddEmbed(finalEmbed));
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
                                            "randomly before each game after deck codes have been locked in.\n\n" +
                                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                                    }
                                    else if (round.Length == 5)
                                    {
                                        instructionMsg = "**Scroll to see all map options!**\n\n" +
                                            "Choose 2 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                            "Only 3 maps will be banned in total, leaving 5 remaining maps. " +
                                            "One of the 2nd priority maps selected by each team will be randomly banned. " +
                                            "You will not know which maps were banned by your opponent, " +
                                            "and the remaining maps will be revealed randomly before each game after deck codes have been locked in.\n\n" +
                                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
                                    }
                                    else
                                    {
                                        instructionMsg = "**Scroll to see all map options!**\n\n" +
                                            "Select 3 maps to ban **in order of your ban priority**. The order of your selection matters!\n\n" +
                                            "**Note:** After making your selections, you'll have a chance to review your choices and confirm or revise them.";
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

                            case string s when s.StartsWith("confirm_deck_"):
                                // Use our special handler with proper deferred handling
                                await HandleConfirmDeckButton(sender, e, hasBeenDeferred);
                                break;

                            case string s when s.StartsWith("revise_deck_"):
                                // Use our special handler with proper deferred handling
                                await HandleReviseDeckButton(sender, e, hasBeenDeferred);
                                break;

                            case "submit_deck_button":
                                try
                                {
                                    // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                    try
                                    {
                                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Interaction might already be acknowledged
                                        Console.WriteLine($"Could not defer deck button interaction: {ex.Message}");
                                    }

                                    // Find the tournament round and user's team/participant info
                                    var currentRound = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                                        r.Teams?.Any(t => t.Thread?.Id == e.Channel.Id) == true);

                                    if (currentRound == null)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} No active tournament round found for this channel.");
                                        return;
                                    }

                                    // Get the team information
                                    var currentTeam = currentRound.Teams?.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                                    if (currentTeam == null)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your team in this channel.");
                                        return;
                                    }

                                    // Check if user is a participant
                                    if (!currentTeam.Participants?.Any(p => p.Player?.Id == e.User.Id) == true)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} You are not a participant in this tournament round.");
                                        return;
                                    }

                                    // Clean up map ban messages
                                    await CleanupMapBanMessages(e.Channel);

                                    // Delete the message with the button
                                    await e.Message.DeleteAsync();

                                    // Send the text-based deck submission prompt
                                    await e.Channel.SendMessageAsync($"{e.User.Mention} Please enter your deck code as a reply to this message.");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error handling submit deck button: {ex.Message}");
                                    await e.Channel.SendMessageAsync($"{e.User.Mention} Error starting deck submission: {ex.Message}");
                                }
                                break;

                            case "tournament_match_winner_dropdown":
                                try
                                {
                                    await e.Interaction.DeferAsync();

                                    // Find the tournament round
                                    var tourneyRound = _roundsHolder.TourneyRounds?.FirstOrDefault(r =>
                                        r.Teams?.Any(t => t.Thread?.Id == e.Channel.Id) == true);

                                    if (tourneyRound == null)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("Could not find the tournament round."));
                                        return;
                                    }

                                    // Get winner ID
                                    if (e.Values == null || !e.Values.Any())
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("No winner selected."));
                                        return;
                                    }

                                    // Parse winner ID and score
                                    string[] parts = e.Values[0].Split(':');
                                    if (parts.Length != 3)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("Invalid selection format."));
                                        return;
                                    }

                                    ulong winnerId = ulong.Parse(parts[0]);
                                    int winnerScore = int.Parse(parts[1]);
                                    int loserScore = int.Parse(parts[2]);

                                    // Find the winner
                                    DiscordMember? winnerMember = null;
                                    if (tourneyRound.Teams != null)
                                    {
                                        foreach (var matchTeam in tourneyRound.Teams)
                                        {
                                            foreach (var matchParticipant in matchTeam.Participants)
                                            {
                                                if (matchParticipant.Player is DiscordMember member && member.Id == winnerId)
                                                {
                                                    winnerMember = member;
                                                    break;
                                                }
                                            }
                                            if (winnerMember is not null) break;
                                        }
                                    }

                                    if (winnerMember is null)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("Could not find the winner."));
                                        return;
                                    }

                                    // Find tournament and match
                                    var matchResult = FindTournamentMatchForRound(tourneyRound);
                                    if (matchResult == null)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("Could not find the tournament match."));
                                        return;
                                    }

                                    // Use null-conditional operators to safely access tournament and match
                                    Tournament? tournament = matchResult.Value.Item1;
                                    Tournament.Match? match = matchResult.Value.Item2;

                                    if (tournament == null || match == null)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                            .WithContent("Tournament or match information is missing."));
                                        return;
                                    }

                                    // Update match result
                                    _tournamentManager.UpdateMatchResult(tournament, match, winnerMember, winnerScore, loserScore);

                                    // Update tournament state
                                    await _tournamentManager.SaveTournamentState(sender);

                                    // Create a success message
                                    var embed = new DiscordEmbedBuilder()
                                        .WithTitle("Match Result Recorded")
                                        .WithDescription($"**Winner:** {winnerMember?.DisplayName ?? "Unknown"}\n**Score:** {winnerScore}-{loserScore}")
                                        .WithColor(DiscordColor.Green);

                                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                                        .AddEmbed(embed));

                                    // Handle match completion to create new matches or advance to playoffs
                                    await _tournamentMatchService.HandleMatchCompletion(tournament, match, sender);

                                    // Log success
                                    _logger.LogInformation($"Successfully processed game result with winner {winnerId}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error handling game result");

                                    // Use a followup message if interaction was deferred
                                    if (hasBeenDeferred)
                                    {
                                        await e.Interaction.CreateFollowupMessageAsync(
                                            new DiscordFollowupMessageBuilder()
                                                .WithContent($"⚠️ Error processing game result: {ex.Message}"));
                                    }
                                    else
                                    {
                                        await e.Channel.SendMessageAsync($"⚠️ Error processing game result: {ex.Message}");
                                    }
                                }
                                break;

                            case "tournament_game_winner_dropdown":
                                // Use our special handler with proper deferred handling
                                await HandleGameWinnerDropdown(sender, e, hasBeenDeferred);
                                break;

                            default:
                                return;
                        }
                    }
                    else
                    {
                        switch (e.Id)
                        {
                            case string s when s.StartsWith("btn_deck"):
                                try
                                {
                                    // Use DeferredMessageUpdate to safely acknowledge the interaction
                                    try
                                    {
                                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                    }
                                    catch (Exception) { /* Ignore if already acknowledged */ }

                                    if (e.Channel is not null)
                                    {
                                        // Create a message with instructions for deck submission
                                        var promptMessage = new DiscordMessageBuilder()
                                            .WithContent($"{e.User.Mention} Please enter your deck code in a reply to this message.\n\n" +
                                                        "After submitting, you'll be able to review and confirm your deck code.");

                                        var deckPrompt = await e.Channel.SendMessageAsync(promptMessage);

                                        // Log the action
                                        Console.WriteLine($"Second handler: Sent deck submission prompt to user {e.User.Username} ({e.User.Id})");

                                        // The detailed instruction message is already in the deck prompt above
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error handling deck button (second handler): {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error processing your deck submission request. " +
                                                "Please type your deck code directly in this channel.");
                                        }
                                        catch { }
                                    }
                                }
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

                // Determine if we need a followup interaction (if the interaction was already acknowledged)
                bool followUpInteraction = true; // Default to using followup interaction as safer approach

                if (followUpInteraction)
                {
                    // Followup interaction - use followup message
                    var response = new DiscordFollowupMessageBuilder()
                        .WithContent($"You have been added to the tournament '{tournamentName}'.");

                    var message = await e.Interaction.CreateFollowupMessageAsync(response);

                    // Delete the message after 10 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10000);
                        try
                        {
                            await message.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to auto-delete signup message: {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Regular interaction - use channel message
                    var message = await e.Channel.SendMessageAsync($"You have been added to the tournament '{tournamentName}'.");

                    // Delete the message after 10 seconds
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10000);
                        try
                        {
                            await message.DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to auto-delete signup message: {ex.Message}");
                        }
                    });
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
                    _logger.LogError($"Failed to send error message: {msgEx.Message}");
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
                string formattedTime = $"<t:{((DateTimeOffset)signup.ScheduledStartTime).ToUnixTimeSeconds()}:F>";
                builder.AddField("Scheduled Start Time", formattedTime);
            }

            if (signup.Participants?.Count > 0)
            {
                // Sort participants by any seeds
                var sortedParticipants = signup.Participants
                    .Select(p => new
                    {
                        Player = p,
                        Seed = signup.Seeds?.FirstOrDefault(s => s.Player?.Id == p.Id || s.PlayerId == p.Id)?.Seed ?? 0,
                        DisplayName = p.DisplayName
                    })
                    .OrderBy(p => p.Seed == 0) // Seeded players first
                    .ThenBy(p => p.Seed)
                    .ThenBy(p => p.DisplayName)
                    .ToList();

                StringBuilder participantsText = new StringBuilder();
                int totalParticipants = sortedParticipants.Count;

                // Calculate the number of rows needed
                int rowsNeeded = (int)Math.Ceiling(totalParticipants / 2.0);

                for (int i = 0; i < rowsNeeded; i++)
                {
                    // Left column
                    int leftIndex = i * 2;
                    if (leftIndex < totalParticipants)
                    {
                        var leftPlayer = sortedParticipants[leftIndex];
                        string leftSeedDisplay = leftPlayer.Seed > 0 ? $"[Seed #{leftPlayer.Seed}]" : "";
                        participantsText.Append($"{leftIndex + 1}. <@{leftPlayer.Player.Id}> {leftSeedDisplay}");

                        // Add padding between columns
                        participantsText.Append("    ");

                        // Right column
                        int rightIndex = leftIndex + 1;
                        if (rightIndex < totalParticipants)
                        {
                            var rightPlayer = sortedParticipants[rightIndex];
                            string rightSeedDisplay = rightPlayer.Seed > 0 ? $"[Seed #{rightPlayer.Seed}]" : "";
                            participantsText.Append($"{rightIndex + 1}. <@{rightPlayer.Player.Id}> {rightSeedDisplay}");
                        }

                        participantsText.AppendLine();
                    }
                }

                string finalText = participantsText.ToString();

                // If the text is too long, truncate it
                if (finalText.Length > 1024)
                {
                    finalText = finalText.Substring(0, 1020) + "...";
                }

                builder.AddField($"Participants ({sortedParticipants.Count})", finalText);
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

        private async Task CleanupDeckSubmissionMessages(DiscordChannel channel, ulong userId)
        {
            try
            {
                // Get recent messages in the channel
                var messages = channel.GetMessagesAsync(50);
                var messageList = new List<DiscordMessage>();

                // Manually collect messages from the async enumerable
                await foreach (var message in messages)
                {
                    messageList.Add(message);
                }

                // Find and delete deck submission related messages for this user
                var messagesToDelete = messageList.Where(m =>
                    (m.Author?.IsBot == true && m.Content?.Contains("Please enter your") == true && m.Content?.Contains(userId.ToString()) == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Please review your deck code") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("deck code submission") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("map ban selections") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Please submit your deck code") == true)
                ).ToList();

                foreach (var message in messagesToDelete)
                {
                    try
                    {
                        await message.DeleteAsync();
                        // Add a small delay to avoid rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up deck submission messages: {ex.Message}");
            }
        }

        private async Task CleanupMapBanMessages(DiscordChannel channel)
        {
            try
            {
                // Get recent messages in the channel
                var messages = channel.GetMessagesAsync(50);
                var messageList = new List<DiscordMessage>();

                // Manually collect messages from the async enumerable
                await foreach (var message in messages)
                {
                    messageList.Add(message);
                }

                // Find and delete map ban related messages
                var messagesToDelete = messageList.Where(m =>
                    (m.Author?.IsBot == true && m.Content?.Contains("map ban") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("Map Ban") == true) ||
                    (m.Author?.IsBot == true && m.Content?.Contains("scroll to see all map options") == true) ||
                    (m.Author?.IsBot == true && m.Embeds?.Any(e => e.Title?.Contains("Map Ban") == true) == true)
                ).ToList();

                foreach (var message in messagesToDelete)
                {
                    try
                    {
                        await message.DeleteAsync();
                        // Add a small delay to avoid rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete map ban message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up map ban messages: {ex.Message}");
            }
        }

        // Helper method to find a tournament match that is linked to a specific round
        private (Tournament, Tournament.Match)? FindTournamentMatchForRound(Round round)
        {
            if (round == null)
                return null;

            // Search all tournaments
            foreach (var tournament in _tournamentManager.GetAllTournaments())
            {
                foreach (var group in tournament.Groups)
                {
                    foreach (var match in group.Matches)
                    {
                        if (match.LinkedRound == round)
                        {
                            return (tournament, match);
                        }
                    }
                }

                // Also check playoff matches
                foreach (var match in tournament.PlayoffMatches)
                {
                    if (match.LinkedRound == round)
                    {
                        return (tournament, match);
                    }
                }
            }

            return null;
        }

        private async Task HandleJoinTournamentButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                // Extract tournament name from button ID
                string tournamentName = e.Id.Replace("join_tournament_", "");

                // Get the signup
                var signup = _tournamentManager.GetSignup(tournamentName);
                if (signup == null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Tournament signup not found.")
                        .AsEphemeral());
                    return;
                }

                // Check if the player is already in the tournament
                if (signup.Participants.Any(p => p.Id == e.User.Id))
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("You're already participating in this tournament.")
                        .AsEphemeral());
                    return;
                }

                // Cast the user to DiscordMember with null check
                var member = e.User as DiscordMember;
                if (member is null)
                {
                    await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                        .WithContent("Unable to add you to the tournament: You must be a server member.")
                        .AsEphemeral());
                    return;
                }

                // Add the member to the tournament
                signup.Participants.Add(member);

                // Update the signup
                _tournamentManager.UpdateSignup(signup);
                await _tournamentManager.SaveAllData();

                // Update the signup message
                await UpdateSignupMessage(sender, signup);

                // Notify the user
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .WithContent($"You've joined the tournament '{tournamentName}'!")
                    .AsEphemeral());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling join tournament button");
                await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                    .WithContent($"Error joining tournament: {ex.Message}")
                    .AsEphemeral());
            }
        }

        // Handler for confirm deck buttons
        private async Task HandleConfirmDeckButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Only try to defer if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // Extract the user ID from the button ID
                string userIdStr = e.Id.Replace("confirm_deck_", "");
                if (!ulong.TryParse(userIdStr, out ulong userId))
                {
                    _logger.LogError($"Failed to parse user ID from confirm_deck button: {userIdStr}");

                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("Error processing deck confirmation: Invalid user ID"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("Error processing deck confirmation: Invalid user ID");
                    }
                    return;
                }

                // Find the tournament round
                var round = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                    r.Teams is not null &&
                    r.Teams.Any(t => t.Thread?.Id == e.Channel.Id));

                if (round == null)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"{e.User.Mention} Could not find an active tournament round for this channel."));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find an active tournament round for this channel.");
                    }
                    return;
                }

                // Find the team and participant
                var team = round.Teams?.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                if (team == null)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"{e.User.Mention} Could not find your team data."));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your team data.");
                    }
                    return;
                }

                var participant = team.Participants?.FirstOrDefault(p =>
                    p is not null && p.Player is not null && p.Player.Id == userId);

                if (participant == null || participant.TempDeckCode == null)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"{e.User.Mention} Could not find your participant data or temporary deck code."));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your participant data or temporary deck code.");
                    }
                    return;
                }

                // Only allow the user who submitted the deck to confirm it
                if (e.User.Id != userId)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"{e.User.Mention} Only the user who submitted the deck can confirm it."));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"{e.User.Mention} Only the user who submitted the deck can confirm it.");
                    }
                    return;
                }

                // Store the confirmed deck code
                participant.Deck = participant.TempDeckCode;

                // Also store in deck history with the current map if available
                if (round.Maps != null && round.Maps.Count > 0 && round.Cycle < round.Maps.Count)
                {
                    // Get the current map based on the cycle
                    int mapIndex = Math.Min(round.Cycle, round.Maps.Count - 1);
                    if (mapIndex >= 0 && mapIndex < round.Maps.Count)
                    {
                        string mapName = round.Maps[mapIndex];
                        if (participant.DeckHistory == null)
                        {
                            participant.DeckHistory = new Dictionary<string, string>();
                        }
                        participant.DeckHistory[mapName] = participant.Deck;
                    }
                }

                // Clear temporary deck code
                participant.TempDeckCode = null;

                // Update the message to show it's been confirmed
                var confirmedEmbed = new DiscordEmbedBuilder()
                    .WithTitle("Deck Code Confirmed")
                    .WithDescription($"Your deck code has been successfully confirmed and submitted.")
                    .WithColor(DiscordColor.Green);

                // Send a confirmation message using the appropriate method
                if (hasBeenDeferred)
                {
                    await e.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder()
                            .WithContent($"{e.User.Mention} Your deck code has been confirmed!")
                            .AddEmbed(confirmedEmbed));
                }
                else
                {
                    await e.Channel.SendMessageAsync(
                        new DiscordMessageBuilder()
                            .WithContent($"{e.User.Mention} Your deck code has been confirmed!")
                            .AddEmbed(confirmedEmbed));
                }

                // Delete the confirmation message with the buttons
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not delete confirmation message: {ex.Message}");
                }

                // Save tournament state
                await _tournamentManager.SaveTournamentState(sender);

                // Check if all participants have submitted their decks
                bool allSubmitted = round.Teams?.All(t =>
                    t.Participants is not null &&
                    t.Participants.All(p => p is not null && !string.IsNullOrEmpty(p.Deck))) ?? false;

                if (allSubmitted)
                {
                    // All decks submitted - set InGame to true
                    round.InGame = true;

                    // Notify players
                    foreach (var t in round.Teams ?? new List<Round.Team>())
                    {
                        if (t.Thread is not null)
                        {
                            await t.Thread.SendMessageAsync("**All decks have been submitted!** The game will now proceed.");
                        }
                    }

                    // Save tournament state again
                    await _tournamentManager.SaveTournamentState(sender);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming deck");

                // Use the appropriate method for sending the error message
                if (hasBeenDeferred)
                {
                    await e.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder()
                            .WithContent($"{e.User.Mention} There was an error confirming your deck: {ex.Message}"));
                }
                else if (e.Channel is not null)
                {
                    await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error confirming your deck: {ex.Message}");
                }
            }
        }

        // Handler for revise deck buttons
        private async Task HandleReviseDeckButton(DiscordClient sender, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Only try to defer if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // Extract the user ID from the button ID
                string userIdStr = e.Id.Replace("revise_deck_", "");
                if (!ulong.TryParse(userIdStr, out ulong userId))
                {
                    _logger.LogError($"Failed to parse user ID from revise_deck button: {userIdStr}");

                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("Error processing deck revision: Invalid user ID"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("Error processing deck revision: Invalid user ID");
                    }
                    return;
                }

                // Only allow the user who submitted the deck to revise it
                if (e.User.Id != userId)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"{e.User.Mention} Only the user who submitted the deck can revise it."));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"{e.User.Mention} Only the user who submitted the deck can revise it.");
                    }
                    return;
                }

                // Delete the old confirmation message to reduce clutter
                try
                {
                    await e.Message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not delete the confirmation message: {ex.Message}");
                }

                // Clean up any existing deck submission prompts in the channel
                await CleanupDeckSubmissionMessages(e.Channel, userId);

                // Prompt the user to submit a new deck code
                var promptMessage = new DiscordMessageBuilder()
                    .WithContent($"{e.User.Mention} Please enter your revised deck code as a reply to this message.\n\n" +
                                "After submitting, you'll be able to review and confirm your deck code.");

                // Send the new prompt using the appropriate method
                if (hasBeenDeferred)
                {
                    await e.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder()
                            .WithContent($"{e.User.Mention} Please enter your revised deck code in this thread.\n\n" +
                                        "After submitting, you'll be able to review and confirm your deck code."));
                }

                // Always send the channel message as well for clarity
                await e.Channel.SendMessageAsync(promptMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising deck");

                // Use the appropriate method for sending the error message
                if (hasBeenDeferred)
                {
                    await e.Interaction.CreateFollowupMessageAsync(
                        new DiscordFollowupMessageBuilder()
                            .WithContent($"{e.User.Mention} There was an error processing your deck revision request: {ex.Message}"));
                }
                else if (e.Channel is not null)
                {
                    await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error processing your deck revision request: {ex.Message}");
                }
            }
        }

        // Handler for game winner dropdown
        private async Task HandleGameWinnerDropdown(DiscordClient sender, ComponentInteractionCreatedEventArgs e, bool hasBeenDeferred)
        {
            try
            {
                // Only try to defer if not already deferred
                if (!hasBeenDeferred)
                {
                    await SafeDeferAsync(e.Interaction);
                }

                // First identify the thread/channel and associated round
                if (e.Channel is null || e.Channel.Type != DiscordChannelType.PrivateThread)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("⚠️ This dropdown should only be used in tournament match threads"));
                    }
                    else
                    {
                        if (e.Channel is not null)
                        {
                            await e.Channel.SendMessageAsync("⚠️ This dropdown should only be used in tournament match threads");
                        }
                        else
                        {
                            _logger.LogWarning("Cannot send error message - channel is null");
                        }
                    }
                    return;
                }

                // Find the tournament round associated with this thread
                var tournamentRound = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                    r.Teams != null && r.Teams.Any(t => t.Thread is not null && t.Thread.Id == e.Channel.Id));

                if (tournamentRound == null)
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("⚠️ Could not find tournament round for this thread"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("⚠️ Could not find tournament round for this thread");
                    }
                    return;
                }

                if (e.Values == null || !e.Values.Any())
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("⚠️ No winner selected"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("⚠️ No winner selected");
                    }
                    return;
                }

                // Process game result
                string selectedValue = e.Values[0];

                // Parse the winner ID from the value format "game_winner:userId" or "game_winner:draw"
                if (!selectedValue.StartsWith("game_winner:"))
                {
                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent("⚠️ Invalid selection format"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync("⚠️ Invalid selection format");
                    }
                    return;
                }

                string winnerId = selectedValue.Substring("game_winner:".Length);

                try
                {
                    // Use the injected TournamentGameService 
                    await _tournamentGameService.HandleGameResultAsync(tournamentRound, e.Channel, winnerId, sender);

                    // Log success
                    _logger.LogInformation($"Successfully processed game result with winner {winnerId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling game result");

                    // Use a followup message if interaction was deferred
                    if (hasBeenDeferred)
                    {
                        await e.Interaction.CreateFollowupMessageAsync(
                            new DiscordFollowupMessageBuilder()
                                .WithContent($"⚠️ Error processing game result: {ex.Message}"));
                    }
                    else
                    {
                        await e.Channel.SendMessageAsync($"⚠️ Error processing game result: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling game winner dropdown");

                if (e.Channel is not null)
                {
                    await e.Channel.SendMessageAsync($"⚠️ Error processing game result: {ex.Message}");
                }
            }
        }
    }
}
