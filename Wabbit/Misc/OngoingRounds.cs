using Wabbit.Models;

namespace Wabbit.Misc
{
    public class OngoingRounds
    {
        public List<Round> TourneyRounds { get; set; } = [];
        public List<Regular1v1> RegularRounds { get; set; } = [];
        public List<Scrimmage> ScrimmageRounds { get; set; } = [];
        public List<Tournament> Tournaments { get; set; } = [];
        public List<TournamentSignup> TournamentSignups { get; set; } = [];

        /// <summary>
        /// Helper method to get a round by thread ID
        /// </summary>
        /// <param name="threadId">Discord channel ID of the team thread</param>
        /// <returns>The round or null if not found</returns>
        public Round? GetRoundByThreadIdOrDefault(ulong threadId)
        {
            if (threadId == 0) return null;

            return TourneyRounds.FirstOrDefault(r =>
                r.Teams is not null &&
                r.Teams.Any(t => t.Thread?.Id == threadId));
        }

        /// <summary>
        /// Helper method to get a scrimmage by thread ID
        /// </summary>
        /// <param name="threadId">Discord channel ID of the scrimmage thread</param>
        /// <returns>The scrimmage or null if not found</returns>
        public Scrimmage? GetScrimmageByThreadIdOrDefault(ulong threadId)
        {
            if (threadId == 0) return null;

            return ScrimmageRounds.FirstOrDefault(s => s.Thread.Id == threadId);
        }
    }
}
