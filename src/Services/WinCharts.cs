using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;

namespace mtg_game_tracker.Services
{
    public record ChartEntry(string Label, int Games, int Wins);

    // Entries keyed by game size (2, 3 or 4 players).
    public record WinCharts(
        IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> People,
        IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> Decks)
    {
        public static readonly int[] GameSizes = { 2, 3, 4 };
        public const int MaxPeople = 6;

        public static WinCharts Empty { get; } = new(
            GameSizes.ToDictionary(s => s, _ => (IReadOnlyList<ChartEntry>)new List<ChartEntry>()),
            GameSizes.ToDictionary(s => s, _ => (IReadOnlyList<ChartEntry>)new List<ChartEntry>()));

        public static async Task<WinCharts> LoadAsync(AppDbContext db, IReadOnlyDictionary<int, string> deckLabels)
        {
            var people = await db.People.ToListAsync();
            var deckIds = deckLabels.Keys.ToList();
            var games = await db.Games
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            var peopleCharts = new Dictionary<int, IReadOnlyList<ChartEntry>>();
            var deckCharts = new Dictionary<int, IReadOnlyList<ChartEntry>>();
            foreach (var size in GameSizes)
            {
                var ofSize = games.Where(g => g.Seats.Count == size).ToList();

                peopleCharts[size] = people
                    .Select(p =>
                    {
                        var played = ofSize.Where(g => g.Seats.Any(s => s.PlayerId == p.Id)).ToList();
                        return new ChartEntry(p.Name, played.Count, played.Count(g => g.WinnerId == p.Id));
                    })
                    .Where(e => e.Games > 0)
                    .OrderByDescending(e => e.Games)
                    .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxPeople)
                    .ToList();

                deckCharts[size] = deckIds
                    .Select(id =>
                    {
                        var played = ofSize
                            .Select(g => new { g.WinnerId, Seat = g.Seats.FirstOrDefault(s => s.DeckId == id) })
                            .Where(x => x.Seat is not null)
                            .ToList();
                        return new ChartEntry(deckLabels[id], played.Count, played.Count(x => x.WinnerId == x.Seat!.PlayerId));
                    })
                    .Where(e => e.Games > 0)
                    .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new WinCharts(peopleCharts, deckCharts);
        }
    }
}
