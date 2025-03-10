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
            // Calculate dimensions based on the tournament structure
            int width = 800;
            int totalGroupHeight = CalculateGroupSectionHeight(tournament);
            int playoffsHeight = tournament.CurrentStage != TournamentStage.Groups ? CalculatePlayoffsSectionHeight(tournament) : 0;

            int height = HeaderHeight + totalGroupHeight +
                         (tournament.CurrentStage != TournamentStage.Groups ? PlayoffsSpacing + playoffsHeight : 0) +
                         FooterHeight;

            // Create the surface and canvas
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // Clear the canvas
            canvas.Clear(BackgroundColor);

            // Draw the tournament header
            DrawTournamentHeader(canvas, tournament, width);

            // Draw group stages
            int yOffset = HeaderHeight;
            DrawGroupStandings(canvas, tournament, width, ref yOffset);

            // Draw playoffs if applicable
            if (tournament.CurrentStage != TournamentStage.Groups)
            {
                yOffset += PlayoffsSpacing;
                DrawPlayoffs(canvas, tournament, width, ref yOffset);
            }

            // Draw footer with timestamp
            DrawFooter(canvas, width, height);

            // Save the image to a file
            string fileName = $"tournament_standings_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, fileName);

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }

            return filePath;
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
                TextAlign = SKTextAlign.Center
            };

            canvas.DrawText(tournament.Name, width / 2, HeaderHeight / 2 + 10, textPaint);

            // Draw current stage
            textPaint.TextSize = 18;
            canvas.DrawText($"Stage: {tournament.CurrentStage}", width / 2, HeaderHeight - 15, textPaint);
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
                IsAntialias = true
            };

            // Column widths (adjust as needed)
            int nameWidth = width / 2;
            int statsWidth = (width - nameWidth) / 5;

            foreach (var group in tournament.Groups)
            {
                // Draw group header
                canvas.DrawRect(Padding, yOffset, width - (2 * Padding), RowHeight, headerPaint);
                textPaint.TextAlign = SKTextAlign.Left;
                textPaint.TextSize = 18;
                canvas.DrawText(group.Name, Padding + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
                yOffset += RowHeight;

                // Draw column headers
                textPaint.TextSize = 14;
                int xPos = Padding;
                canvas.DrawRect(xPos, yOffset, nameWidth, RowHeight, borderPaint);
                canvas.DrawText("Player", xPos + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
                xPos += nameWidth;

                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("W", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("D", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("L", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                canvas.DrawText("P", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
                xPos += statsWidth;

                canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                textPaint.TextSize = 12;
                canvas.DrawText("Status", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
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
                    canvas.DrawText(participant.Player?.DisplayName ?? "Unknown", xPos + CellPadding, yOffset + RowHeight - CellPadding, textPaint);
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

                    // Status (advanced to playoffs)
                    canvas.DrawRect(xPos, yOffset, statsWidth, RowHeight, borderPaint);
                    textPaint.TextSize = 12;
                    if (participant.AdvancedToPlayoffs)
                    {
                        canvas.DrawText("Advanced", xPos + (statsWidth / 2), yOffset + RowHeight - CellPadding, textPaint);
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