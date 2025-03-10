using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wabbit.Models;
using DSharpPlus.Entities;
using MatchType = Wabbit.Models.MatchType;

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
        /// Generates a standings image for a tournament
        /// </summary>
        public static string GenerateStandingsImage(Tournament tournament)
        {
            // Calculate sizes
            int width = 900; // Increase width to accommodate standings

            int groupSectionHeight = CalculateGroupSectionHeight(tournament);
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
            using var stream = File.OpenWrite(fileName);
            data.SaveTo(stream);

            return fileName;
        }

        private static int CalculateGroupSectionHeight(Tournament tournament)
        {
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
            canvas.DrawRect(Padding, yOffset, width - (Padding * 2), HeaderHeight, groupHeaderPaint);
            textPaint.TextSize = 24;
            textPaint.TextAlign = SKTextAlign.Center;
            canvas.DrawText("Group Stage", width / 2, yOffset + HeaderHeight - CellPadding, textPaint);
            yOffset += HeaderHeight;

            // Debug information
            Console.WriteLine($"Drawing group standings for {tournament.Groups.Count} groups");

            foreach (var group in tournament.Groups)
            {
                // Draw group header
                canvas.DrawRect(Padding, yOffset, width - (Padding * 2), RowHeight, headerPaint);
                canvas.DrawText(group.Name, width / 2, yOffset + RowHeight - CellPadding, textPaint);
                yOffset += RowHeight;

                // Calculate column widths
                int nameWidth = (int)(width * 0.30);
                int statsWidth = (int)(width * 0.125);
                int statusWidth = (int)(width * 0.20);

                // Draw header row
                int xPos = Padding;

                // Player column header
                canvas.DrawRect(xPos, yOffset, nameWidth, RowHeight, borderPaint);
                textPaint.TextAlign = SKTextAlign.Center;
                textPaint.TextSize = 14;
                canvas.DrawText("Player", xPos + (nameWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += nameWidth;

                // W column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("W", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // D column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("D", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // L column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("L", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // P column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("P", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // Status column header
                canvas.DrawRect(xPos, yOffset, statusWidth, RowHeight, borderPaint);
                textPaint.TextSize = 12;
                canvas.DrawText("Status", xPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
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
                    xPos = Padding;

                    // Player name
                    canvas.DrawRect(xPos, yOffset, nameWidth, RowHeight, borderPaint);
                    textPaint.TextAlign = SKTextAlign.Left;
                    textPaint.TextSize = 16;
                    textPaint.Color = TextColor;

                    // Get the display name with detailed fallback logging
                    string displayName = "Unknown Player";

                    if (participant.Player is not null)
                    {
                        // Try the DisplayName property first
                        if (!string.IsNullOrEmpty(participant.Player.DisplayName))
                        {
                            displayName = participant.Player.DisplayName;
                            Console.WriteLine($"Using DisplayName: {displayName}");
                        }
                        // Then try ToString
                        else
                        {
                            string toStringValue = participant.Player.ToString();
                            if (!string.IsNullOrEmpty(toStringValue) && toStringValue != "null")
                            {
                                displayName = toStringValue;
                                Console.WriteLine($"Using ToString: {displayName}");
                            }
                            else
                            {
                                // Last resort, try to get type information
                                Console.WriteLine($"Player ToString returned null or empty, player type: {participant.Player.GetType().Name}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Player reference is null");
                    }

                    // Log stats for debugging
                    Console.WriteLine($"Player: {displayName}, W: {participant.Wins}, D: {participant.Draws}, L: {participant.Losses}, P: {participant.Points}");

                    canvas.DrawText(displayName, xPos + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
                    xPos += nameWidth;

                    // W column
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    textPaint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText(participant.Wins.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // D column
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Draws.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // L column
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Losses.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // P column
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Points.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // Status column
                    canvas.DrawRect(xPos, yOffset, statusWidth, RowHeight, borderPaint);
                    textPaint.TextAlign = SKTextAlign.Center;
                    textPaint.TextSize = 14;

                    // Draw status text with appropriate color
                    string statusText = "Pending";
                    if (participant.AdvancedToPlayoffs)
                    {
                        statusText = "Advanced";
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

                    canvas.DrawText(statusText, xPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    textPaint.Color = TextColor;
                    yOffset += RowHeight;
                }

                yOffset += GroupSpacing;
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
            var semifinals = tournament.PlayoffMatches?.Where(m => m?.Type == MatchType.Semifinal).ToList() ?? [];
            var finals = tournament.PlayoffMatches?.Where(m => m?.Type == MatchType.Final).ToList() ?? [];

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
                bool p1IsWinner = match.Result?.Winner?.Id == p1?.Player?.Id;

                if (p1IsWinner)
                {
                    canvas.DrawRect(x + 2, y + 30, width - 4, 20, winnerPaint);
                    textPaint.Color = SKColors.Black;
                }

                canvas.DrawText(p1Name, x + 10, y + 45, textPaint);

                if (match.IsComplete && p1 != null)
                {
                    scorePaint.Color = textPaint.Color;
                    canvas.DrawText(p1.Score.ToString(), x + width - 10, y + 45, scorePaint);
                }

                textPaint.Color = TextColor;
            }

            if (match.Participants?.Count > 1)
            {
                // Second participant
                var p2 = match.Participants[1];
                string p2Name = p2?.Display ?? "Unknown";
                bool p2IsWinner = match.Result?.Winner?.Id == p2?.Player?.Id;

                if (p2IsWinner)
                {
                    canvas.DrawRect(x + 2, y + 55, width - 4, 20, winnerPaint);
                    textPaint.Color = SKColors.Black;
                }

                canvas.DrawText(p2Name, x + 10, y + 70, textPaint);

                if (match.IsComplete && p2 != null)
                {
                    scorePaint.Color = textPaint.Color;
                    canvas.DrawText(p2.Score.ToString(), x + width - 10, y + 70, scorePaint);
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