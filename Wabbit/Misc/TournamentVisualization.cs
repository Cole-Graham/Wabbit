using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wabbit.Models;
using Wabbit.BotClient.Config;
using DSharpPlus;
using DSharpPlus.Entities;
using TournamentMatchType = Wabbit.Models.TournamentMatchType;
using Wabbit.Services.Interfaces;

namespace Wabbit.Misc
{
    public class TournamentVisualization
    {
        // Colors
        private static readonly SKColor BackgroundColor = new(30, 30, 30);
        private static readonly SKColor TextColor = new(255, 255, 255);
        private static readonly SKColor HeaderColor = new(50, 50, 50);
        private static readonly SKColor WinColor = new(75, 181, 67);
        private static readonly SKColor DrawColor = new(181, 181, 67);
        private static readonly SKColor LossColor = new(181, 67, 67);
        private static readonly SKColor BorderColor = new(100, 100, 100);
        private static readonly SKColor GroupHeaderColor = new(50, 100, 150);
        private static readonly SKColor PlayoffsColor = new(150, 100, 50);

        // Padding and sizing
        private const int Padding = 20;
        private const int CellPadding = 10;
        private const int HeaderHeight = 60;
        private const int RowHeight = 40;
        private const int FooterHeight = 40;
        private const int GroupSpacing = 30;
        private const int PlayoffsSpacing = 50;

        /// <summary>
        /// Generates a standings image for a tournament and sends it to the configured standings channel if available
        /// </summary>
        /// <returns>Path to the generated image file</returns>
        public static async Task<string> GenerateStandingsImage(Tournament tournament, DiscordClient? client = null, ITournamentStateService? stateService = null)
        {
            // Calculate sizes
            bool useDoubleColumn = tournament.Groups.Count >= 4; // Use two columns for 4+ groups
            int width = useDoubleColumn ? 1800 : 900; // Double width for two columns

            int groupSectionHeight = CalculateGroupSectionHeight(tournament, useDoubleColumn);
            int playoffsSectionHeight = CalculatePlayoffsSectionHeight(tournament);

            int height = HeaderHeight + groupSectionHeight + playoffsSectionHeight + FooterHeight;

            // Create bitmap and canvas
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            // Draw background
            canvas.Clear(BackgroundColor);

            // Draw header
            DrawTournamentHeader(canvas, tournament, width);

            // Draw group standings
            int yOffset = HeaderHeight + Padding;
            DrawGroupStandings(canvas, tournament, width, ref yOffset);

            // Draw playoffs if applicable
            if (tournament.CurrentStage == TournamentStage.Playoffs || tournament.CurrentStage == TournamentStage.Complete)
            {
                DrawPlayoffs(canvas, tournament, width, ref yOffset);
            }

            // Draw footer
            DrawFooter(canvas, width, height);

            // Save to file
            string fileName = $"Images/tournament_{DateTime.Now:yyyyMMddHHmmss}.png";
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            // Create directory if it doesn't exist
            Directory.CreateDirectory("Images");

            using var stream = File.OpenWrite(fileName);
            data.SaveTo(stream);

            // If a client is provided and we have a configured standings channel, post the image there
            if (client != null && tournament.AnnouncementChannel?.Guild is not null)
            {
                try
                {
                    // Find the standings channel in the server config
                    var serverId = tournament.AnnouncementChannel.Guild.Id;
                    var server = ConfigManager.Config?.Servers?.FirstOrDefault(s => s?.ServerId == serverId);

                    if (server != null && server.StandingsChannelId.HasValue)
                    {
                        // Get the standings channel
                        var standingsChannel = await client.GetChannelAsync(server.StandingsChannelId.Value);
                        if (standingsChannel is not null)
                        {
                            // Create the embed
                            var embed = new DiscordEmbedBuilder()
                                .WithTitle($"🏆 Tournament Standings: {tournament.Name}")
                                .WithDescription($"Current standings as of {DateTime.Now}")
                                .WithColor(DiscordColor.Gold)
                                .WithFooter("Tournament Standings");

                            // Check if there's an existing visualization message to update
                            bool existingMessageUpdated = false;
                            if (tournament.RelatedMessages != null)
                            {
                                var existingVisualization = tournament.RelatedMessages
                                    .FirstOrDefault(m => m.Type == "StandingsVisualization" && m.ChannelId == standingsChannel.Id);

                                if (existingVisualization != null)
                                {
                                    try
                                    {
                                        // Try to get the existing message
                                        var existingMessage = await standingsChannel.GetMessageAsync(existingVisualization.MessageId);
                                        if (existingMessage is not null)
                                        {
                                            // Update the existing message with the new image
                                            using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                                            {
                                                var builder = new DiscordMessageBuilder()
                                                    .AddEmbed(embed)
                                                    .AddFile(Path.GetFileName(fileName), fileStream);

                                                await existingMessage.ModifyAsync(builder);
                                                existingMessageUpdated = true;
                                                Console.WriteLine($"Updated existing standings visualization for tournament {tournament.Name}");

                                                // Save tournament state after updating the message
                                                if (stateService != null)
                                                {
                                                    await stateService.SaveTournamentStateAsync(client);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error updating existing visualization message: {ex.Message}");
                                        // If we can't update the existing message, we'll create a new one below
                                    }
                                }
                            }

                            // If we couldn't update an existing message, create a new one
                            if (!existingMessageUpdated)
                            {
                                using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                                {
                                    var message = await standingsChannel.SendMessageAsync(new DiscordMessageBuilder()
                                        .AddEmbed(embed)
                                        .AddFile(Path.GetFileName(fileName), fileStream));

                                    // Store this message ID for later
                                    var relatedMessage = new Wabbit.Models.RelatedMessage
                                    {
                                        ChannelId = standingsChannel.Id,
                                        MessageId = message.Id,
                                        Type = "StandingsVisualization"
                                    };

                                    // Add to tournament's related messages
                                    if (tournament.RelatedMessages == null)
                                        tournament.RelatedMessages = new List<Wabbit.Models.RelatedMessage>();

                                    // Remove any old visualization messages first to avoid duplicates
                                    tournament.RelatedMessages.RemoveAll(m => m.Type == "StandingsVisualization");

                                    // Add the new message
                                    tournament.RelatedMessages.Add(relatedMessage);
                                }

                                // Save tournament state if state service is provided
                                if (stateService != null && client != null)
                                {
                                    try
                                    {
                                        await stateService.SaveTournamentStateAsync(client);
                                    }
                                    catch (Exception savEx)
                                    {
                                        Console.WriteLine($"Error saving tournament state: {savEx.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error posting standings to channel: {ex.Message}");
                }
            }

            return fileName;
        }

        private static int CalculateGroupSectionHeight(Tournament tournament, bool useDoubleColumn = false)
        {
            if (!useDoubleColumn)
            {
                // Single column layout - sum all group heights
                int totalHeight = 0;
                foreach (var group in tournament.Groups)
                {
                    // Group header
                    totalHeight += RowHeight;
                    // Column headers
                    totalHeight += RowHeight;
                    // Rows for each participant
                    totalHeight += group.Participants.Count * RowHeight;
                    // Spacing after group
                    totalHeight += GroupSpacing;
                }
                return totalHeight;
            }
            else
            {
                // Double column layout - calculate the height of the tallest column
                var leftGroups = tournament.Groups.Take((tournament.Groups.Count + 1) / 2).ToList();
                var rightGroups = tournament.Groups.Skip((tournament.Groups.Count + 1) / 2).ToList();

                int leftHeight = 0, rightHeight = 0;

                foreach (var group in leftGroups)
                {
                    leftHeight += RowHeight; // Group header
                    leftHeight += RowHeight; // Column headers
                    leftHeight += group.Participants.Count * RowHeight; // Participants
                    leftHeight += GroupSpacing; // Spacing
                }

                foreach (var group in rightGroups)
                {
                    rightHeight += RowHeight; // Group header
                    rightHeight += RowHeight; // Column headers
                    rightHeight += group.Participants.Count * RowHeight; // Participants
                    rightHeight += GroupSpacing; // Spacing
                }

                return Math.Max(leftHeight, rightHeight);
            }
        }

        private static int CalculatePlayoffsSectionHeight(Tournament tournament)
        {
            // Basic playoff section height calculation
            // Could be more sophisticated for complex brackets
            return 300; // Fixed height for now
        }

        private static void DrawTournamentHeader(SKCanvas canvas, Tournament tournament, int width)
        {
            using var paint = new SKPaint
            {
                Color = HeaderColor,
                IsAntialias = true
            };

            // Draw header background
            canvas.DrawRect(0, 0, width, HeaderHeight, paint);

            // Draw tournament name
            using var textPaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 28,
                IsAntialias = true,
                TextAlign = SKTextAlign.Left
            };

            // Position the tournament name on the left
            canvas.DrawText(tournament.Name, Padding * 2, HeaderHeight / 2 + 10, textPaint);

            // Draw current stage on the right
            textPaint.TextSize = 18;
            textPaint.TextAlign = SKTextAlign.Right;
            canvas.DrawText($"Stage: {tournament.CurrentStage}", width - Padding * 2, HeaderHeight / 2 + 10, textPaint);
        }

        private static void DrawGroupStandings(SKCanvas canvas, Tournament tournament, int width, ref int yOffset)
        {
            // Setup paint objects
            var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = BorderColor,
                StrokeWidth = 1
            };

            var headerPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = HeaderColor
            };

            var groupHeaderPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = GroupHeaderColor
            };

            var textPaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 16,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            // Draw section header
            int tableWidth = width - (Padding * 2);
            canvas.DrawRect(Padding, yOffset, tableWidth, HeaderHeight, groupHeaderPaint);
            textPaint.TextSize = 24;
            textPaint.TextAlign = SKTextAlign.Center;
            canvas.DrawText("Group Stage", width / 2, yOffset + HeaderHeight - CellPadding, textPaint);
            yOffset += HeaderHeight;

            // Debug information
            Console.WriteLine($"Drawing group standings for {tournament.Groups.Count} groups");

            bool useDoubleColumn = tournament.Groups.Count >= 4;
            int columnWidth = useDoubleColumn ? (width - (Padding * 3)) / 2 : width - (Padding * 2);
            int startYOffset = yOffset; // Remember starting Y position

            // Split groups for two-column layout
            if (useDoubleColumn)
            {
                int leftColYOffset = yOffset;
                int rightColYOffset = yOffset;
                var leftGroups = tournament.Groups.Take((tournament.Groups.Count + 1) / 2).ToList();
                var rightGroups = tournament.Groups.Skip((tournament.Groups.Count + 1) / 2).ToList();

                // Draw left column groups
                foreach (var group in leftGroups)
                {
                    DrawGroupTable(canvas, group, Padding, leftColYOffset, columnWidth, borderPaint, headerPaint, textPaint);
                    leftColYOffset += (2 + group.Participants.Count) * RowHeight + GroupSpacing;
                }

                // Draw right column groups
                foreach (var group in rightGroups)
                {
                    DrawGroupTable(canvas, group, Padding * 2 + columnWidth, rightColYOffset, columnWidth, borderPaint, headerPaint, textPaint);
                    rightColYOffset += (2 + group.Participants.Count) * RowHeight + GroupSpacing;
                }

                // Set new yOffset to the tallest column
                yOffset = Math.Max(leftColYOffset, rightColYOffset);
            }
            else
            {
                // Single column layout - original behavior
                foreach (var group in tournament.Groups)
                {
                    DrawGroupTable(canvas, group, Padding, yOffset, columnWidth, borderPaint, headerPaint, textPaint);
                    yOffset += (2 + group.Participants.Count) * RowHeight + GroupSpacing;
                }
            }
        }

        private static void DrawGroupTable(SKCanvas canvas, Tournament.Group group, int xPos, int yOffset, int tableWidth, SKPaint borderPaint, SKPaint headerPaint, SKPaint textPaint)
        {
            // Draw group header
            canvas.DrawRect(xPos, yOffset, tableWidth, RowHeight, headerPaint);
            textPaint.TextSize = 18; // Set consistent header text size
            textPaint.TextAlign = SKTextAlign.Center;
            canvas.DrawText(group.Name, xPos + tableWidth / 2, yOffset + RowHeight - CellPadding, textPaint);
            yOffset += RowHeight;

            // Calculate column widths
            int nameWidth = (int)(tableWidth * 0.35); // Width for player names
            int seedWidth = (int)(tableWidth * 0.10); // New dedicated seed column
            int statsWidth = (int)(tableWidth * 0.09); // Slightly smaller stats columns
            int statusWidth = tableWidth - nameWidth - seedWidth - (statsWidth * 4); // Ensure exact fit

            // Draw header row
            int colPos = xPos;

            // Player column header
            canvas.DrawRect(colPos, yOffset, nameWidth, RowHeight, borderPaint);
            textPaint.TextAlign = SKTextAlign.Center;
            textPaint.TextSize = 14;
            canvas.DrawText("Player", colPos + (nameWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += nameWidth;

            // Seed column header
            canvas.DrawRect(colPos, yOffset, seedWidth, RowHeight, borderPaint);
            canvas.DrawText("Seed", colPos + (seedWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += seedWidth;

            // Participant stats
            // W column
            canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
            canvas.DrawText("W", colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += statsWidth;

            // D column
            canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
            canvas.DrawText("D", colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += statsWidth;

            // L column
            canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
            canvas.DrawText("L", colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += statsWidth;

            // P column
            canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
            canvas.DrawText("P", colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            colPos += statsWidth;

            // Status column header
            canvas.DrawRect(colPos, yOffset, statusWidth, RowHeight, borderPaint);
            textPaint.TextSize = 12;
            canvas.DrawText("Status", colPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
            yOffset += RowHeight;

            // Sort participants by points
            var sortedParticipants = group.Participants
                .OrderByDescending(p => p.Points)
                .ThenByDescending(p => p.GamesWon)
                .ThenBy(p => p.GamesLost)
                .ToList();

            Console.WriteLine($"Group {group.Name} has {sortedParticipants.Count} participants");

            // Draw each participant row
            foreach (var participant in sortedParticipants)
            {
                colPos = xPos;

                // Player name
                canvas.DrawRect(colPos, yOffset, nameWidth, RowHeight, borderPaint);
                textPaint.TextAlign = SKTextAlign.Left;
                textPaint.TextSize = 16;
                textPaint.Color = TextColor;

                // Get the display name with detailed fallback logging
                string displayName = "Unknown Player";

                if (participant.Player is not null)
                {
                    // Try to get a meaningful name
                    if (participant.Player is DiscordMember member)
                    {
                        displayName = member.DisplayName ?? member.Username;
                    }
                    else if (participant.Player is DiscordUser user)
                    {
                        displayName = user.Username;
                    }
                    else
                    {
                        displayName = participant.Player.ToString() ?? "Unknown";
                    }
                }

                // Truncate if too long
                if (displayName.Length > 20)
                {
                    displayName = displayName.Substring(0, 17) + "...";
                }

                canvas.DrawText(displayName, colPos + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
                colPos += nameWidth;

                // Seed
                canvas.DrawRect(colPos, yOffset, seedWidth, RowHeight, borderPaint);
                textPaint.TextAlign = SKTextAlign.Center;
                string seedText = participant.Seed > 0 ? participant.Seed.ToString() : "-";
                canvas.DrawText(seedText, colPos + (seedWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                colPos += seedWidth;

                // Wins cell
                canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText(participant.Wins.ToString(), colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                colPos += statsWidth;

                // Draws cell
                canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText(participant.Draws.ToString(), colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                colPos += statsWidth;

                // Losses cell
                canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText(participant.Losses.ToString(), colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                colPos += statsWidth;

                // Points cell
                canvas.DrawRect(colPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText(participant.Points.ToString(), colPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                colPos += statsWidth;

                // Status cell
                canvas.DrawRect(colPos, yOffset, statusWidth, RowHeight, borderPaint);

                // Set status text and color based on advancement
                textPaint.TextAlign = SKTextAlign.Center;
                string statusText = participant.QualificationInfo ?? "";

                if (participant.AdvancedToPlayoffs)
                {
                    statusText = participant.QualificationInfo ?? "Advanced";
                    textPaint.Color = WinColor;
                }
                else if (group.IsComplete)
                {
                    statusText = "Eliminated";
                    textPaint.Color = LossColor;
                }
                else
                {
                    textPaint.Color = DrawColor;
                }

                canvas.DrawText(statusText, colPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                textPaint.Color = TextColor;
                yOffset += RowHeight;
            }
        }

        private static void DrawPlayoffs(SKCanvas canvas, Tournament tournament, int width, ref int yOffset)
        {
            using var headerPaint = new SKPaint
            {
                Color = PlayoffsColor,
                IsAntialias = true
            };

            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };

            using var textPaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 18,
                IsAntialias = true
            };

            // Draw playoffs header
            canvas.DrawRect(Padding, yOffset, width - (2 * Padding), RowHeight, headerPaint);
            textPaint.TextAlign = SKTextAlign.Left;
            canvas.DrawText("Playoffs", Padding + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
            yOffset += RowHeight + 20;

            // Process tournament data with null checks
            var semifinals = tournament.PlayoffMatches?.Where(m => m?.Type == TournamentMatchType.Semifinal).ToList() ?? [];
            var finals = tournament.PlayoffMatches?.Where(m => m?.Type == TournamentMatchType.Final).ToList() ?? [];
            var tiebreakers = tournament.PlayoffMatches?.Where(m => m?.Type == TournamentMatchType.ThirdPlaceTiebreaker).ToList() ?? [];

            // Draw tiebreakers if any exist
            if (tiebreakers.Any())
            {
                // Show a tiebreaker section
                textPaint.TextSize = 16;
                textPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText("Third Place Tiebreakers", width / 2, yOffset, textPaint);
                yOffset += 30;

                // Determine layout - arrange in grid if many matches
                int matchesPerRow = Math.Min(tiebreakers.Count, 2); // Max 2 per row
                int rowCount = (int)Math.Ceiling(tiebreakers.Count / (double)matchesPerRow);

                int matchHeight = 80;
                int matchWidth = (width - ((matchesPerRow + 1) * Padding)) / matchesPerRow;

                for (int row = 0; row < rowCount; row++)
                {
                    for (int col = 0; col < matchesPerRow; col++)
                    {
                        int index = row * matchesPerRow + col;
                        if (index >= tiebreakers.Count)
                            break;

                        int xPos = Padding + col * (matchWidth + Padding);
                        int yPos = yOffset + row * (matchHeight + 20);

                        // Set display position if not already set
                        if (string.IsNullOrEmpty(tiebreakers[index].DisplayPosition))
                            tiebreakers[index].DisplayPosition = "Tiebreaker";

                        DrawMatch(canvas, tiebreakers[index], xPos, yPos, matchWidth, matchHeight);
                    }
                }

                // Move yOffset down to account for all tiebreaker rows
                yOffset += rowCount * (matchHeight + 20) + 10;
            }

            // Draw semifinals
            if (semifinals.Any())
            {
                int matchHeight = 80;
                int matchWidth = (width - (3 * Padding)) / 2;

                textPaint.TextSize = 16;
                textPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText("Semifinals", width / 2, yOffset, textPaint);
                yOffset += 30;

                for (int i = 0; i < semifinals.Count; i++)
                {
                    int xPos = Padding + (i * (matchWidth + Padding));
                    DrawMatch(canvas, semifinals[i], xPos, yOffset, matchWidth, matchHeight);
                }

                yOffset += matchHeight + 30;
            }

            // Draw finals
            if (finals.Any())
            {
                int matchHeight = 80;
                int matchWidth = (width - (2 * Padding)) / 2;

                textPaint.TextSize = 16;
                textPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText("Final", width / 2, yOffset, textPaint);
                yOffset += 30;

                int xPos = width / 2 - matchWidth / 2;
                DrawMatch(canvas, finals[0], xPos, yOffset, matchWidth, matchHeight);

                yOffset += matchHeight + 20;
            }
        }

        private static void DrawMatch(SKCanvas canvas, Tournament.Match match, int x, int y, int width, int height)
        {
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2
            };

            using var textPaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 14,
                IsAntialias = true,
                TextAlign = SKTextAlign.Left
            };

            using var scorePaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 14,
                IsAntialias = true,
                TextAlign = SKTextAlign.Right
            };

            using var winnerPaint = new SKPaint
            {
                Color = WinColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            // Draw match box
            canvas.DrawRect(x, y, width, height, borderPaint);

            // Draw match title
            textPaint.TextAlign = SKTextAlign.Center;
            textPaint.TextSize = 16;
            canvas.DrawText(match.DisplayPosition, x + width / 2, y + 20, textPaint);

            // Draw each participant
            textPaint.TextAlign = SKTextAlign.Left;
            textPaint.TextSize = 14;

            if (match.Participants?.Count > 0)
            {
                // First participant
                var p1 = match.Participants[0];
                string p1Name = p1?.Display ?? "Unknown";
                bool p1IsWinner = false;

                // Compare players using appropriate method based on type
                if (match.Result?.Winner != null && p1?.Player != null)
                {
                    // If they're the same object reference
                    if (ReferenceEquals(match.Result.Winner, p1.Player))
                    {
                        p1IsWinner = true;
                    }
                    // Otherwise compare by ToString()
                    else
                    {
                        p1IsWinner = match.Result.Winner.ToString() == p1.Player.ToString();
                    }
                }

                if (p1IsWinner)
                {
                    canvas.DrawRect(x + 2, y + 30, width - 4, 20, winnerPaint);
                    textPaint.Color = SKColors.Black;
                }

                canvas.DrawText(p1Name, x + 10, y + 45, textPaint);

                if (match.Result != null && match.Participants.Count > 1)
                {
                    // Draw the score
                    string scoreText = $"{match.Participants[0].Score}-{match.Participants[1].Score}";
                    canvas.DrawText(scoreText, x + width - 40, y + 45, textPaint);
                }

                textPaint.Color = TextColor;
            }

            if (match.Participants?.Count > 1)
            {
                // Second participant
                var p2 = match.Participants[1];
                string p2Name = p2?.Display ?? "Unknown";
                bool p2IsWinner = false;

                // Compare players using appropriate method
                if (match.Result?.Winner != null && p2?.Player != null)
                {
                    // If they're the same object reference
                    if (ReferenceEquals(match.Result.Winner, p2.Player))
                    {
                        p2IsWinner = true;
                    }
                    // Otherwise compare by ToString()
                    else
                    {
                        p2IsWinner = match.Result.Winner.ToString() == p2.Player.ToString();
                    }
                }

                if (p2IsWinner)
                {
                    canvas.DrawRect(x + 2, y + 55, width - 4, 20, winnerPaint);
                    textPaint.Color = SKColors.Black;
                }

                canvas.DrawText(p2Name, x + 10, y + 70, textPaint);

                if (match.Result != null && match.Participants.Count > 1)
                {
                    // Draw the score
                    string scoreText = $"{match.Participants[0].Score}-{match.Participants[1].Score}";
                    canvas.DrawText(scoreText, x + width - 40, y + 70, textPaint);
                }

                textPaint.Color = TextColor;
            }
        }

        private static void DrawFooter(SKCanvas canvas, int width, int height)
        {
            using var textPaint = new SKPaint
            {
                Color = TextColor,
                TextSize = 12,
                IsAntialias = true,
                TextAlign = SKTextAlign.Right
            };

            canvas.DrawText($"Generated: {DateTime.Now}", width - Padding, height - 10, textPaint);
        }
    }
}