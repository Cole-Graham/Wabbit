namespace Wabbit.Models
{
    public class Map
    {
        public string Name { get; set; } = string.Empty;
        public string? Id { get; set; }
        public string? Thumbnail { get; set; }
        public string? Size { get; set; }
        public bool IsInRandomPool { get; set; } = false;
        public bool IsInTournamentPool { get; set; } = false;
    }
}
