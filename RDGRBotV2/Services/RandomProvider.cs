using RDGRBotV2.Services.Interfaces;

namespace RDGRBotV2.Services
{
    public class RandomProvider : IRandomProvider
    {
        private readonly Random _random = new();
        public Random Instance => _random;
    }
}
