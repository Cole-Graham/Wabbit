using RDGRBotV2.Models;

namespace RDGRBotV2.Misc
{
    public class OngoingRounds
    {
        public List<Round> TourneyRounds { get; set; } = [];
        public List<Regular1v1> RegularRounds { get; set; } = [];
    }
}
