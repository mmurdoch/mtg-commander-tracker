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

        public string DisplayName
        {
            get
            {
                var commanders = string.IsNullOrEmpty(SecondCommander) ? Commander : $"{Commander} / {SecondCommander}";
                return string.IsNullOrWhiteSpace(Name) ? commanders : $"{commanders} ({Name})";
            }
        }

        private string CommanderKey => $"{Commander}/{SecondCommander}".ToUpperInvariant();

        // Appends the owner's name to decks whose commander(s) are shared with another owner's deck.
        public static Dictionary<int, string> BuildDisplayNames(IEnumerable<Deck> decks)
        {
            var list = decks.ToList();
            var sharedKeys = list
                .GroupBy(d => d.CommanderKey)
                .Where(g => g.Select(d => d.OwnerId).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            return list.ToDictionary(
                d => d.Id,
                d => sharedKeys.Contains(d.CommanderKey) && d.Owner is not null
                    ? $"{d.DisplayName} - {d.Owner.Name}"
                    : d.DisplayName);
        }
    }
}
