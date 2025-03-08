using Wabbit.Models;

namespace Wabbit.Misc
{
    public class OngoingRounds
    {
        public List<Round> TourneyRounds { get; set; } = [];
        public List<Regular1v1> RegularRounds { get; set; } = [];
    }
}
