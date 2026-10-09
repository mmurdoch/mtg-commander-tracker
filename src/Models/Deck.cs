namespace mtg_game_tracker.Models
{
    public class Deck
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string Commander { get; set; } = string.Empty;
        public string? SecondCommander { get; set; }
        public int OwnerId { get; set; }
        public Person? Owner { get; set; }
        public int ColorDataId { get; set; }
        public ColorData? ColorData { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Commander : Name;
    }
}
