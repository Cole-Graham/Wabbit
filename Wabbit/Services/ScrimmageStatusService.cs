using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.Models;
using Wabbit.Services.Interfaces;
using Wabbit.Misc;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing scrimmage status and related operations
    /// </summary>
    public class ScrimmageStatusService : IScrimmageStatusService
    {
        private readonly ILogger<ScrimmageStatusService> _logger;
        private readonly DiscordClient _client;
        private readonly IMapService _mapService;
        private readonly OngoingRounds _ongoingRounds;

        public ScrimmageStatusService(
            ILogger<ScrimmageStatusService> logger,
            DiscordClient client,
            IMapService mapService,
            OngoingRounds ongoingRounds)
        {
            _logger = logger;
            _client = client;
            _mapService = mapService;
            _ongoingRounds = ongoingRounds;
        }

        /// <inheritdoc/>
        public async Task<Scrimmage> CreateScrimmageAsync(
            DiscordChannel channel,
            DiscordUser player1,
            string? deck1,
            DiscordUser player2,
            string? deck2,
            ScrimmageGameType gameType,
            MatchLength matchLength,
            bool isRated,
            bool useTournamentMapPool)
        {
            // Validate arguments
            if (player1.Id == player2.Id)
            {
                throw new ArgumentException("A player cannot play against themselves");
            }

            // Create a thread for the scrimmage
            var threadChannel = await DiscordUtilities.CreateThreadAsync(
                channel,
                $"{gameType} Scrimmage: {player1.Username} vs {player2.Username}",
                _logger,
                DiscordChannelType.PrivateThread,
                DiscordAutoArchiveDuration.Day);

            if (threadChannel is null)
            {
                throw new InvalidOperationException("Failed to create thread for scrimmage");
            }

            // Create the scrimmage with the required thread property
            var scrimmage = new Scrimmage
            {
                Thread = threadChannel,
                GameType = gameType,
                MatchLength = matchLength,
                IsRated = isRated,
                UseTournamentMapPool = useTournamentMapPool,
                Status = ScrimmageStatus.Created
            };

            // Initialize teams
            scrimmage.TeamA = new ScrimmageTeam { Captain = player1 };
            if (deck1 != null)
                scrimmage.TeamA.SetDeckCode(player1, deck1);

            scrimmage.TeamB = new ScrimmageTeam { Captain = player2 };
            if (deck2 != null)
                scrimmage.TeamB.SetDeckCode(player2, deck2);

            // Post initial status message
            var statusMessage = await UpdateScrimmageStatusAsync(scrimmage);
            scrimmage.StatusMessage = statusMessage;

            // Add scrimmage to ongoing rounds
            _ongoingRounds.ScrimmageRounds.Add(scrimmage);

            // Generate first map
            // Get a random map name
            Map? map = null;
            string mapName;

            if (useTournamentMapPool)
            {
                // For now, just get any map since we don't have the tournament-specific maps
                map = _mapService.GetMapByName("Default Map");
                mapName = map?.Name ?? "Random Map";
            }
            else
            {
                // Get any map from the service
                map = _mapService.GetMapByName("Default Map");
                mapName = map?.Name ?? "Random Map";
            }

            scrimmage.Maps.Add(mapName);

            _logger.LogInformation($"Created scrimmage between {player1.Username} and {player2.Username} in {channel.Guild.Name}/{channel.Name}");
            return scrimmage;
        }

        /// <inheritdoc/>
        public async Task<DiscordMessage> UpdateScrimmageStatusAsync(Scrimmage scrimmage)
        {
            // Create the status embed
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"{GetGameTypeString(scrimmage.GameType)} Scrimmage: {GetMatchLengthString(scrimmage.MatchLength)}")
                .WithColor(GetStatusColor(scrimmage.Status))
                .WithFooter($"Created {scrimmage.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");

            // Add players
            embed.AddField("Team A", scrimmage.TeamA.Captain.Mention, true);
            embed.AddField("Team B", scrimmage.TeamB.Captain.Mention, true);

            // Add scores if games have been played
            if (scrimmage.TeamAScore > 0 || scrimmage.TeamBScore > 0)
            {
                embed.AddField("Score", $"{scrimmage.TeamAScore} - {scrimmage.TeamBScore}", true);
            }

            // Add deck codes if provided
            string? teamADeck = scrimmage.TeamA.DeckName;
            if (!string.IsNullOrEmpty(teamADeck))
            {
                embed.AddField("Team A Deck", teamADeck, true);
            }

            string? teamBDeck = scrimmage.TeamB.DeckName;
            if (!string.IsNullOrEmpty(teamBDeck))
            {
                embed.AddField("Team B Deck", teamBDeck, true);
            }

            // Add current map if available
            if (scrimmage.Maps.Count > 0)
            {
                string currentMap = scrimmage.Maps[scrimmage.Maps.Count - 1];
                embed.AddField("Current Map", currentMap, true);
            }

            // Add status message
            string statusMessage = scrimmage.Status switch
            {
                ScrimmageStatus.Created => "Match created. Waiting for players to ready up.",
                ScrimmageStatus.InProgress => "Match in progress.",
                ScrimmageStatus.Completed => $"Match completed. {(scrimmage.TeamAScore > scrimmage.TeamBScore ? scrimmage.TeamA.Captain.Username : scrimmage.TeamB.Captain.Username)} wins!",
                ScrimmageStatus.Cancelled => "Match cancelled.",
                _ => "Unknown status."
            };

            embed.WithDescription(statusMessage);

            // Additional info for rated matches
            if (scrimmage.IsRated)
            {
                embed.AddField("Rating", "This is a rated match", true);
            }

            // Additional info for tournament map pool
            if (scrimmage.UseTournamentMapPool)
            {
                embed.AddField("Maps", "Using tournament map pool", true);
            }

            // Create buttons
            var buttons = new List<DiscordComponent>();

            // Create message
            var builder = new DiscordMessageBuilder();
            builder.AddEmbed(embed.Build());

            if (buttons.Any())
            {
                builder.AddComponents(buttons);
            }

            // Send or update the message
            DiscordMessage message;
            if (scrimmage.StatusMessage == null)
            {
                message = await scrimmage.Thread.SendMessageAsync(builder);
            }
            else
            {
                message = await scrimmage.StatusMessage.ModifyAsync(builder);
            }

            return message;
        }

        /// <inheritdoc/>
        public async Task<bool> CompleteScrimmageAsync(Scrimmage scrimmage)
        {
            scrimmage.Status = ScrimmageStatus.Completed;
            scrimmage.CompletedAt = DateTimeOffset.UtcNow;

            await UpdateScrimmageStatusAsync(scrimmage);

            // Send completion message
            var message = await scrimmage.Thread.SendMessageAsync(
                "This scrimmage has been marked as completed. Thanks for playing!");
            scrimmage.Messages.Add(message);

            _logger.LogInformation("Scrimmage between {Player1} and {Player2} completed",
                scrimmage.TeamA.Captain.Username, scrimmage.TeamB.Captain.Username);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> CancelScrimmageAsync(Scrimmage scrimmage)
        {
            scrimmage.Status = ScrimmageStatus.Cancelled;
            scrimmage.CompletedAt = DateTimeOffset.UtcNow;

            await UpdateScrimmageStatusAsync(scrimmage);

            // Send cancellation message
            var message = await scrimmage.Thread.SendMessageAsync(
                "This scrimmage has been cancelled.");
            scrimmage.Messages.Add(message);

            _logger.LogInformation("Scrimmage between {Player1} and {Player2} cancelled",
                scrimmage.TeamA.Captain.Username, scrimmage.TeamB.Captain.Username);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RecordGameResultAsync(Scrimmage scrimmage, int winningPlayer)
        {
            // Validate arguments
            if (winningPlayer != 1 && winningPlayer != 2)
            {
                throw new ArgumentException("Winning player must be 1 or 2");
            }

            // Update scores
            if (winningPlayer == 1)
            {
                scrimmage.TeamAScore++;
            }
            else
            {
                scrimmage.TeamBScore++;
            }

            // Check if the match is completed
            int gamesToWin = GetGamesToWin(scrimmage.MatchLength);
            if (scrimmage.TeamAScore >= gamesToWin)
            {
                scrimmage.Status = ScrimmageStatus.Completed;
                _logger.LogInformation($"Match completed: {scrimmage.TeamA.Captain.Username} wins against {scrimmage.TeamB.Captain.Username}");
            }
            else if (scrimmage.TeamBScore >= gamesToWin)
            {
                scrimmage.Status = ScrimmageStatus.Completed;
                _logger.LogInformation($"Match completed: {scrimmage.TeamB.Captain.Username} wins against {scrimmage.TeamA.Captain.Username}");
            }
            else
            {
                // If the match wasn't already in progress, mark it as such
                if (scrimmage.Status != ScrimmageStatus.InProgress)
                {
                    scrimmage.Status = ScrimmageStatus.InProgress;
                }
            }

            // Update status message
            await UpdateScrimmageStatusAsync(scrimmage);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> AdvanceToNextGameAsync(Scrimmage scrimmage)
        {
            // Generate a new map
            Map? map = null;
            string mapName;

            if (scrimmage.UseTournamentMapPool)
            {
                // For now, just get any map since we don't have the tournament-specific maps
                map = _mapService.GetMapByName("Default Map");
                mapName = map?.Name ?? "Random Map";
            }
            else
            {
                // Get any map from the service
                map = _mapService.GetMapByName("Default Map");
                mapName = map?.Name ?? "Random Map";
            }

            // Add the new map to the list
            scrimmage.Maps.Add(mapName);

            // Increment current game number
            scrimmage.CurrentGameNumber++;

            // Update status message
            await UpdateScrimmageStatusAsync(scrimmage);

            // Log
            _logger.LogInformation($"Advanced to next game between {scrimmage.TeamA.Captain.Username} and {scrimmage.TeamB.Captain.Username} with map {mapName}");

            return true;
        }

        /// <inheritdoc/>
        public Task<Scrimmage?> GetScrimmageByThreadIdAsync(ulong threadId)
        {
            var scrimmage = _ongoingRounds.GetScrimmageByThreadIdOrDefault(threadId);
            return Task.FromResult(scrimmage);
        }

        /// <summary>
        /// Helper method to get a color based on scrimmage status
        /// </summary>
        private DiscordColor GetStatusColor(ScrimmageStatus status) => status switch
        {
            ScrimmageStatus.Created => DiscordColor.Yellow,
            ScrimmageStatus.InProgress => DiscordColor.Green,
            ScrimmageStatus.Completed => DiscordColor.Blue,
            ScrimmageStatus.Cancelled => DiscordColor.Red,
            _ => DiscordColor.Gray
        };

        /// <summary>
        /// Helper method to convert GameType to a readable string
        /// </summary>
        private string GetGameTypeString(ScrimmageGameType gameType) => gameType switch
        {
            ScrimmageGameType.OneVOne => "1v1",
            ScrimmageGameType.TwoVTwo => "2v2",
            ScrimmageGameType.ThreeVThree => "3v3",
            ScrimmageGameType.FourVFour => "4v4",
            _ => "Unknown"
        };

        /// <summary>
        /// Helper method to convert MatchLength to a readable string
        /// </summary>
        private string GetMatchLengthString(MatchLength matchLength) => matchLength switch
        {
            MatchLength.Bo1 => "Best of 1",
            MatchLength.Bo3 => "Best of 3",
            MatchLength.Bo5 => "Best of 5",
            _ => "Unknown"
        };

        /// <summary>
        /// Helper method to get the number of games needed to win a match
        /// </summary>
        private int GetGamesToWin(MatchLength matchLength) => matchLength switch
        {
            MatchLength.Bo3 => 2,
            MatchLength.Bo5 => 3,
            _ => 1 // Default to 1 for Bo1
        };
    }
}