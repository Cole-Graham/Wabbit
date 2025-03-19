using System;

namespace Wabbit.Models
{
    public static class GameTypeExtensions 
    {
        /// <summary>
        /// Converts any specialized game type enum to its display string
        /// </summary>
        public static string ToDisplayString<T>(this T gameType) where T : Enum => gameType switch
        {
            // We can use pattern matching on the enum names since they're consistent across generated types
            { } when gameType.ToString() == nameof(GameType.OneVOne) => "1v1",
            { } when gameType.ToString() == nameof(GameType.TwoVTwo) => "2v2",
            { } when gameType.ToString() == nameof(GameType.ThreeVThree) => "3v3",
            { } when gameType.ToString() == nameof(GameType.FourVFour) => "4v4",
            _ => gameType?.ToString() ?? "Unknown"
        };
    }
}