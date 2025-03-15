namespace Wabbit.Models
{
    /// <summary>
    /// Enum representing the different stages of a tournament match
    /// </summary>
    public enum MatchStage
    {
        /// <summary>
        /// Initial stage when the match is created
        /// </summary>
        Created,

        /// <summary>
        /// Map ban stage where teams select maps to ban
        /// </summary>
        MapBan,

        /// <summary>
        /// Deck submission stage where players submit their decks
        /// </summary>
        DeckSubmission,

        /// <summary>
        /// Game results stage where game outcomes are recorded
        /// </summary>
        GameResults,

        /// <summary>
        /// Match completed stage
        /// </summary>
        Completed
    }
}