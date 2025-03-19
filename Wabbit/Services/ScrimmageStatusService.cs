using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Wabbit.BotClient.Config;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    /// <summary>
    /// Service for managing scrimmage status and related operations
    /// </summary>
    public class ScrimmageStatusService : IScrimmageStatusService
    {
        private readonly ILogger<ScrimmageStatusService> _logger;
        private readonly DiscordClient _client;
        private readonly IRandomMapExt _randomMapService;
        private readonly ITournamentMapService _tournamentMapService;
        private readonly OngoingRounds _ongoingRounds;

        public ScrimmageStatusService(
            ILogger<ScrimmageStatusService> logger,
            DiscordClient client,
            IRandomMapExt randomMapService,
            ITournamentMapService tournamentMapService,
            OngoingRounds ongoingRounds)
        {
            _logger = logger;
            _client = client;
            _randomMapService = randomMapService;
            _tournamentMapService = tournamentMapService;
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
            // For rated matches, always use tournament map pool
            if (isRated)
                useTournamentMapPool = true;

            // Determine how many maps we need based on match length
            int mapCount = matchLength switch
            {
                MatchLength.Bo3 => 3,
                MatchLength.Bo5 => 5,
                _ => 1 // Default to 1 map for Bo1
            };

            // Determine if this is a 1v1 match based on game type
            bool isOneVOne = gameType == ScrimmageGameType.OneVOne;

            // Create a thread for this scrimmage
            var threadName = $"Scrimmage: {player1.Username} vs {player2.Username}";
            var thread = await DiscordUtilities.CreateThreadAsync(
                channel,
                threadName,
                _logger,
                DiscordChannelType.PublicThread,
                DiscordAutoArchiveDuration.Day);

            if (thread is null)
            {
                throw new InvalidOperationException("Failed to create thread for scrimmage");
            }

            // Get the maps for this scrimmage - from tournament or casual pool based on setting
            List<string> maps;
            if (useTournamentMapPool)
            {
                maps = _tournamentMapService.GetRandomMaps(isOneVOne, mapCount);
            }
            else
            {
                maps = _randomMapService.GetRandomMaps(isOneVOne, mapCount).ToList();
            }

            // Create the scrimmage object
            var scrimmage = new Scrimmage
            {
                Thread = thread,
                Player1 = player1,
                Deck1 = deck1,
                Player2 = player2,
                Deck2 = deck2,
                Maps = maps,
                Status = ScrimmageStatus.Created,
                GameType = gameType,
                MatchLength = matchLength,
                IsRated = isRated,
                UseTournamentMapPool = useTournamentMapPool,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Add to ongoing rounds
            _ongoingRounds.ScrimmageRounds.Add(scrimmage);

            // Create initial status message
            var statusMessage = await UpdateScrimmageStatusAsync(scrimmage);
            scrimmage.StatusMessage = statusMessage;

            // Try to add players to thread if they're members of the guild
            try
            {
                if (player1 is DiscordMember member1)
                    await thread.AddThreadMemberAsync(member1);

                if (player2 is DiscordMember member2)
                    await thread.AddThreadMemberAsync(member2);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add one or more members to the scrimmage thread: {Message}", ex.Message);
            }

            // Welcome message
            var welcomeMessage = await thread.SendMessageAsync(
                $"Welcome {player1.Mention} and {player2.Mention} to your scrimmage! " +
                $"Use this thread to coordinate your match.");

            scrimmage.Messages.Add(welcomeMessage);

            _logger.LogInformation("Created new scrimmage between {Player1} and {Player2}",
                player1.Username, player2.Username);

            return scrimmage;
        }

        /// <inheritdoc/>
        public async Task<DiscordMessage> UpdateScrimmageStatusAsync(Scrimmage scrimmage)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle($"Scrimmage: {scrimmage.Player1.Username} vs {scrimmage.Player2.Username}")
                .WithColor(GetStatusColor(scrimmage.Status));

            // Add game type and match type information
            embed.AddField("Game Type", GetGameTypeString(scrimmage.GameType), true);
            embed.AddField("Match Type", GetMatchLengthString(scrimmage.MatchLength), true);
            embed.AddField("Status", scrimmage.Status.ToString(), true);

            // Add if this is a rated match
            if (scrimmage.IsRated)
            {
                embed.AddField("Rated", "Yes - This match will affect ratings", true);
            }

            // Add map pool type
            embed.AddField("Map Pool", scrimmage.UseTournamentMapPool ? "Tournament" : "Casual", true);

            // Add player decks if specified
            if (scrimmage.Deck1 != null)
                embed.AddField($"{scrimmage.Player1.Username}'s Deck", scrimmage.Deck1, true);

            if (scrimmage.Deck2 != null)
                embed.AddField($"{scrimmage.Player2.Username}'s Deck", scrimmage.Deck2, true);

            // Add current score if match is in progress
            if (scrimmage.Status == ScrimmageStatus.InProgress)
            {
                embed.AddField("Score", $"{scrimmage.Player1.Username} {scrimmage.Player1Score} - {scrimmage.Player2Score} {scrimmage.Player2.Username}", false);
                embed.AddField("Current Game", $"Game {scrimmage.CurrentGameNumber} of {GetGamesToWin(scrimmage.MatchLength) * 2 - 1}", false);
            }

            // Add the maps
            var mapList = string.Join("\n", scrimmage.Maps.Select((map, i) =>
            {
                string indicator = i + 1 == scrimmage.CurrentGameNumber ? "➡️ " : "";
                return $"{indicator}{i + 1}. {map}";
            }));
            embed.AddField("Maps", mapList);

            // Add timestamp information
            embed.WithFooter($"Created {scrimmage.CreatedAt:g}");
            if (scrimmage.CompletedAt.HasValue)
            {
                embed.WithFooter($"Created {scrimmage.CreatedAt:g} | Completed {scrimmage.CompletedAt.Value:g}");
            }

            if (scrimmage.StatusMessage == null)
            {
                return await scrimmage.Thread.SendMessageAsync(embed);
            }
            else
            {
                return await scrimmage.StatusMessage.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
            }
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
                scrimmage.Player1.Username, scrimmage.Player2.Username);

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
                scrimmage.Player1.Username, scrimmage.Player2.Username);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> RecordGameResultAsync(Scrimmage scrimmage, int winningPlayer)
        {
            if (winningPlayer != 1 && winningPlayer != 2)
            {
                _logger.LogWarning("Invalid winning player {WinningPlayer} for scrimmage", winningPlayer);
                return false;
            }

            // Update the scrimmage status to in progress if not already
            if (scrimmage.Status != ScrimmageStatus.InProgress)
            {
                scrimmage.Status = ScrimmageStatus.InProgress;
            }

            // Update the score
            if (winningPlayer == 1)
            {
                scrimmage.Player1Score++;
            }
            else // winningPlayer == 2
            {
                scrimmage.Player2Score++;
            }

            // Check if the match is over
            int gamesToWin = GetGamesToWin(scrimmage.MatchLength);
            if (scrimmage.Player1Score >= gamesToWin || scrimmage.Player2Score >= gamesToWin)
            {
                // Match is complete
                await CompleteScrimmageAsync(scrimmage);

                string winnerName = scrimmage.Player1Score > scrimmage.Player2Score
                    ? scrimmage.Player1.Username
                    : scrimmage.Player2.Username;

                // Send a final message
                var finalMessage = await scrimmage.Thread.SendMessageAsync(
                    $"The match is complete! {winnerName} wins {scrimmage.Player1Score}-{scrimmage.Player2Score}!");
                scrimmage.Messages.Add(finalMessage);

                _logger.LogInformation("Scrimmage between {Player1} and {Player2} completed with score {Score1}-{Score2}",
                    scrimmage.Player1.Username, scrimmage.Player2.Username,
                    scrimmage.Player1Score, scrimmage.Player2Score);

                return true;
            }

            // Send a game completion message
            string gameWinnerName = winningPlayer == 1 ? scrimmage.Player1.Username : scrimmage.Player2.Username;
            var gameMessage = await scrimmage.Thread.SendMessageAsync(
                $"Game {scrimmage.CurrentGameNumber} complete! {gameWinnerName} wins on {scrimmage.Maps[scrimmage.CurrentGameNumber - 1]}.");
            scrimmage.Messages.Add(gameMessage);

            // Update the status message with new score
            await UpdateScrimmageStatusAsync(scrimmage);

            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> AdvanceToNextGameAsync(Scrimmage scrimmage)
        {
            // Can only advance if not already completed
            if (scrimmage.Status == ScrimmageStatus.Completed || scrimmage.Status == ScrimmageStatus.Cancelled)
            {
                return false;
            }

            // Check if we've reached the maximum games for this match
            int maxGames = scrimmage.MatchLength switch
            {
                MatchLength.Bo3 => 3,
                MatchLength.Bo5 => 5,
                _ => 1 // Default to 1 for Bo1
            };

            if (scrimmage.CurrentGameNumber >= maxGames)
            {
                // Already at the last game
                return false;
            }

            // Advance to next game
            scrimmage.CurrentGameNumber++;

            // Update the status message
            await UpdateScrimmageStatusAsync(scrimmage);

            // Send message about the next game
            var nextGameMessage = await scrimmage.Thread.SendMessageAsync(
                $"Moving to game {scrimmage.CurrentGameNumber} on map: {scrimmage.Maps[scrimmage.CurrentGameNumber - 1]}");
            scrimmage.Messages.Add(nextGameMessage);

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