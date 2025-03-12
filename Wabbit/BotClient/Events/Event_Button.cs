using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

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
                                try
                                {
                                    // Use DeferredMessageUpdate to safely acknowledge the interaction
                                    try
                                    {
                                        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                    }
                                    catch (Exception) { /* Ignore if already acknowledged */ }

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
                                            // Create a message with a text input field
                                            var promptMessage = new DiscordMessageBuilder()
                                                .WithContent($"{e.User.Mention} Please enter your deck code in a reply to this message.\n\n" +
                                                            "**Type your deck code directly as a reply to this message.**");

                                            var deckPrompt = await e.Channel.SendMessageAsync(promptMessage);

                                            // Log the action
                                            Console.WriteLine($"Sent deck submission prompt to user {e.User.Username} ({e.User.Id})");

                                            // Set up a collector for the user's response
                                            // Since we can't directly set this up, we'll rely on the user to reply
                                            // and handle it elsewhere in the message creation event

                                            // Notify in the user's thread to explain what's happening
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} Please submit your deck code by replying to the message above. " +
                                                "Due to Discord API limitations, we've adjusted the submission process.");
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
                                // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                try
                                {
                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                }
                                catch (Exception ex)
                                {
                                    // Interaction might already be acknowledged
                                    Console.WriteLine($"Could not defer confirm deck interaction: {ex.Message}");
                                }

                                try
                                {
                                    // Extract the user ID from the button ID
                                    string userIdStr = s.Replace("confirm_deck_", "");
                                    if (!ulong.TryParse(userIdStr, out ulong userId))
                                    {
                                        Console.WriteLine($"Failed to parse user ID from confirm_deck button: {userIdStr}");
                                        return;
                                    }

                                    // Find the tournament round
                                    var roundForDeck = _roundsHolder.TourneyRounds.FirstOrDefault(r =>
                                        r.Teams is not null &&
                                        r.Teams.Any(t => t.Thread?.Id == e.Channel.Id));

                                    if (roundForDeck == null)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find an active tournament round for this channel.");
                                        return;
                                    }

                                    // Find the team and participant
                                    var teamForDeck = roundForDeck.Teams?.FirstOrDefault(t => t.Thread?.Id == e.Channel.Id);
                                    if (teamForDeck == null)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your team data.");
                                        return;
                                    }

                                    var deckParticipant = teamForDeck.Participants?.FirstOrDefault(p =>
                                        p is not null && p.Player is not null && p.Player.Id == userId);

                                    if (deckParticipant == null || deckParticipant.TempDeckCode == null)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Could not find your participant data or temporary deck code.");
                                        return;
                                    }

                                    // Only allow the user who submitted the deck to confirm it
                                    if (e.User.Id != userId)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Only the user who submitted the deck can confirm it.");
                                        return;
                                    }

                                    // Store the confirmed deck code
                                    deckParticipant.Deck = deckParticipant.TempDeckCode;

                                    // Also store in deck history with the current map if available
                                    if (roundForDeck.Maps != null && roundForDeck.Maps.Count > 0 && roundForDeck.Cycle < roundForDeck.Maps.Count)
                                    {
                                        // Get the current map based on the cycle
                                        int mapIndex = Math.Min(roundForDeck.Cycle, roundForDeck.Maps.Count - 1);
                                        if (mapIndex >= 0 && mapIndex < roundForDeck.Maps.Count)
                                        {
                                            string currentMap = roundForDeck.Maps[mapIndex];
                                            if (deckParticipant.DeckHistory == null)
                                            {
                                                deckParticipant.DeckHistory = new Dictionary<string, string>();
                                            }
                                            deckParticipant.DeckHistory[currentMap] = deckParticipant.Deck;
                                        }
                                    }

                                    // Clear temporary deck code
                                    deckParticipant.TempDeckCode = null;

                                    // Clean up any deck submission related messages to reduce clutter
                                    await CleanupDeckSubmissionMessages(e.Channel, userId);

                                    // Update the message to show it's been confirmed
                                    var confirmedEmbed = new DiscordEmbedBuilder()
                                        .WithTitle("Deck Code Confirmed")
                                        .WithDescription($"Your deck code has been successfully confirmed and submitted.")
                                        .WithColor(DiscordColor.Green);

                                    // Send a simple confirmation message that will be cleaned up later
                                    await e.Channel.SendMessageAsync(
                                        new DiscordMessageBuilder()
                                            .WithContent($"{e.User.Mention} Your deck code has been confirmed!")
                                            .AddEmbed(confirmedEmbed));

                                    // Delete the confirmation message with the buttons
                                    await e.Message.DeleteAsync();

                                    // Save tournament state
                                    await _tournamentManager.SaveTournamentState(sender);

                                    // Check if all participants have submitted their decks
                                    bool allSubmitted = roundForDeck.Teams?.All(t =>
                                        t.Participants is not null &&
                                        t.Participants.All(p => p is not null && !string.IsNullOrEmpty(p.Deck))) ?? false;

                                    if (allSubmitted)
                                    {
                                        // All decks submitted - set InGame to true
                                        roundForDeck.InGame = true;

                                        // Notify players
                                        foreach (var t in roundForDeck.Teams ?? [])
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
                                    Console.WriteLine($"Error confirming deck: {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error confirming your deck: {ex.Message}");
                                        }
                                        catch { }
                                    }
                                }
                                break;

                            case string s when s.StartsWith("revise_deck_"):
                                // Try to acknowledge the interaction, but don't fail if it's already been acknowledged
                                try
                                {
                                    await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);
                                }
                                catch (Exception ex)
                                {
                                    // Interaction might already be acknowledged
                                    Console.WriteLine($"Could not defer revise deck interaction: {ex.Message}");
                                }

                                try
                                {
                                    // Extract the user ID from the button ID
                                    string userIdStr = s.Replace("revise_deck_", "");
                                    if (!ulong.TryParse(userIdStr, out ulong userId))
                                    {
                                        Console.WriteLine($"Failed to parse user ID from revise_deck button: {userIdStr}");
                                        return;
                                    }

                                    // Only allow the user who submitted the deck to revise it
                                    if (e.User.Id != userId)
                                    {
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Only the user who submitted the deck can revise it.");
                                        return;
                                    }

                                    // Delete the old confirmation message to reduce clutter
                                    await e.Message.DeleteAsync();

                                    // Clean up any existing deck submission prompts in the channel
                                    await CleanupDeckSubmissionMessages(e.Channel, userId);

                                    // Prompt the user to submit a new deck code
                                    var promptMessage = new DiscordMessageBuilder()
                                        .WithContent($"{e.User.Mention} Please enter your revised deck code as a reply to this message.\n\n" +
                                                    "**Type your deck code directly as a reply to this message.**");

                                    // Send the new prompt
                                    await e.Channel.SendMessageAsync(promptMessage);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error revising deck: {ex.Message}");
                                    if (e.Channel is not null)
                                    {
                                        try
                                        {
                                            await e.Channel.SendMessageAsync($"{e.User.Mention} There was an error processing your deck revision request: {ex.Message}");
                                        }
                                        catch { }
                                    }
                                }
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
                                                        "**Type your deck code directly as a reply to this message.**");

                                        var deckPrompt = await e.Channel.SendMessageAsync(promptMessage);

                                        // Log the action
                                        Console.WriteLine($"Second handler: Sent deck submission prompt to user {e.User.Username} ({e.User.Id})");

                                        // Notify in the user's thread to explain what's happening
                                        await e.Channel.SendMessageAsync($"{e.User.Mention} Please submit your deck code by replying to the message above. " +
                                            "Due to Discord API limitations, we've adjusted the submission process.");
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
    }
}
