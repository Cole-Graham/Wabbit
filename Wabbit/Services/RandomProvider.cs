using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    public class RandomProvider : IRandomProvider
    {
        private readonly Random _random = new();
        public Random Instance => _random;
    }
}
