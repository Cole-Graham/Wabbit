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
            using var headerPaint = new SKPaint
            {
                Color = GroupHeaderColor,
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
                TextSize = 16,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };

            foreach (var group in tournament.Groups)
            {
                // Draw group header
                canvas.DrawRect(Padding, yOffset, width - (Padding * 2), RowHeight, headerPaint);
                canvas.DrawText(group.Name, width / 2, yOffset + RowHeight - CellPadding, textPaint);
                yOffset += RowHeight;

                // Calculate column widths
                int nameWidth = (int)(width * 0.30); // Reduced from 0.35 to 0.30
                int statsWidth = (int)(width * 0.125);
                int statusWidth = (int)(width * 0.20); // Increased from 0.15 to 0.20

                // Draw header row
                int xPos = Padding;

                // Player column header
                canvas.DrawRect(xPos, yOffset, nameWidth, RowHeight, borderPaint);
                textPaint.TextAlign = SKTextAlign.Center;
                textPaint.TextSize = 14;
                canvas.DrawText("Player", xPos + (nameWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += nameWidth;

                // Wins column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("W", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // Draws column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("D", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // Losses column header
                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("L", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                // Points column header
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

                // Draw each participant row
                foreach (var participant in sortedParticipants)
                {
                    xPos = Padding;

                    // Player name
                    canvas.DrawRect(xPos, yOffset, nameWidth, RowHeight, borderPaint);
                    textPaint.TextAlign = SKTextAlign.Left;
                    textPaint.TextSize = 16;
                    textPaint.Color = TextColor;

                    // Draw the participant name
                    textPaint.TextAlign = SKTextAlign.Left;
                    textPaint.Color = TextColor;

                    // Get the display name, with fallback for mock objects
                    string displayName = participant.Player?.DisplayName ?? string.Empty;

                    // If DisplayName is empty, try using the string representation
                    if (string.IsNullOrEmpty(displayName) && participant.Player is not null)
                    {
                        displayName = participant.Player.ToString() ?? string.Empty;
                    }

                    // If still empty, try to extract a name from the Player's ToString representation
                    if (string.IsNullOrEmpty(displayName) && participant.Player is not null)
                    {
                        // Try to get the name from the ToString representation
                        var typeString = participant.Player.ToString();
                        if (!string.IsNullOrEmpty(typeString) && typeString != "null")
                        {
                            displayName = typeString;
                        }
                    }

                    // If still empty, use the default fallback text
                    if (string.IsNullOrEmpty(displayName))
                    {
                        displayName = "Unknown Player";
                    }

                    canvas.DrawText(displayName, xPos + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
                    xPos += nameWidth;

                    // Wins
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    textPaint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText(participant.Wins.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // Draws
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Draws.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // Losses
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Losses.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // Points
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    canvas.DrawText(participant.Points.ToString(), xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    xPos += statsWidth;

                    // Status (qualified for playoffs, etc)
                    canvas.DrawRect(xPos, yOffset, statusWidth, RowHeight, borderPaint);
                    if (participant.AdvancedToPlayoffs)
                    {
                        textPaint.Color = WinColor;
                        canvas.DrawText("Qualified", xPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    }
                    else if (group.IsComplete)
                    {
                        textPaint.Color = LossColor;
                        canvas.DrawText("Eliminated", xPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    }
                    else
                    {
                        textPaint.Color = TextColor;
                        canvas.DrawText("Pending", xPos + (statusWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                    }

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