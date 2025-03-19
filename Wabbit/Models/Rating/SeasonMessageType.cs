namespace Wabbit.Models.Rating
{
    /// <summary>
    /// Types of messages related to seasons
    /// </summary>
    public enum SeasonMessageType
    {
        /// <summary>
        /// General announcement about the season
        /// </summary>
        Announcement,

        /// <summary>
        /// Season leaderboard message
        /// </summary>
        Leaderboard,

        /// <summary>
        /// Season start announcement
        /// </summary>
        SeasonStart,

        /// <summary>
        /// Season end announcement
        /// </summary>
        SeasonEnd,

        /// <summary>
        /// Final rankings at season end
        /// </summary>
        FinalRankings,

        /// <summary>
        /// Update message about the season
        /// </summary>
        Update,

        /// <summary>
        /// Other type of message
        /// </summary>
        Other
    }
}