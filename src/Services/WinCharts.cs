using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;

namespace mtg_game_tracker.Services
{
    public record ChartEntry(string Label, int Games, int Wins, int? DeckId = null);

    // Months is the look-back window; null means all time.
    public record ChartTimeframe(string Key, string Label, int? Months);

    // Entries keyed by game size (2, 3 or 4 players). Seats are listed in turn order.
    public record WinCharts(
        IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> People,
        IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> Decks,
        IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> Seats)
    {
        public static readonly int[] GameSizes = { 2, 3, 4 };
        public static readonly string[] SeatNames = { "First", "Second", "Third", "Fourth" };
        public const int MaxPeople = 6;
        public const string AllTime = "all";

        public static readonly IReadOnlyList<ChartTimeframe> Timeframes = new[]
        {
            new ChartTimeframe(AllTime, "All time", null),
            new ChartTimeframe("12m", "Last 12 months", 12),
            new ChartTimeframe("6m", "Last 6 months", 6),
            new ChartTimeframe("3m", "Last 3 months", 3),
            new ChartTimeframe("1m", "Last month", 1),
        };

        public static WinCharts Empty { get; } = new(
            GameSizes.ToDictionary(s => s, _ => (IReadOnlyList<ChartEntry>)new List<ChartEntry>()),
            GameSizes.ToDictionary(s => s, _ => (IReadOnlyList<ChartEntry>)new List<ChartEntry>()),
            GameSizes.ToDictionary(s => s, _ => (IReadOnlyList<ChartEntry>)new List<ChartEntry>()));

        public static async Task<IReadOnlyDictionary<string, WinCharts>> LoadByTimeframeAsync(
            AppDbContext db, IReadOnlyDictionary<int, string> deckLabels, DateTime today)
        {
            var result = new Dictionary<string, WinCharts>();
            foreach (var timeframe in Timeframes)
            {
                DateTime? since = timeframe.Months is int months ? today.AddMonths(-months) : null;
                result[timeframe.Key] = await LoadAsync(db, deckLabels, since);
            }
            return result;
        }

        public static async Task<WinCharts> LoadAsync(AppDbContext db, IReadOnlyDictionary<int, string> deckLabels, DateTime? since = null)
        {
            var people = await db.People.ToListAsync();
            var deckIds = deckLabels.Keys.ToList();
            var games = await db.Games
                .Where(g => since == null || g.Date >= since)
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.OrderBy(p => p.Id).Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            var peopleCharts = new Dictionary<int, IReadOnlyList<ChartEntry>>();
            var deckCharts = new Dictionary<int, IReadOnlyList<ChartEntry>>();
            var seatCharts = new Dictionary<int, IReadOnlyList<ChartEntry>>();
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
                        return new ChartEntry(deckLabels[id], played.Count, played.Count(x => x.WinnerId == x.Seat!.PlayerId), id);
                    })
                    .Where(e => e.Games > 0)
                    .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                seatCharts[size] = ofSize.Count == 0
                    ? new List<ChartEntry>()
                    : Enumerable.Range(0, size)
                        .Select(i => new ChartEntry(
                            SeatNames[i],
                            ofSize.Count,
                            ofSize.Count(g => g.WinnerId is int winner && g.Seats[i].PlayerId == winner)))
                        .ToList();
            }

            return new WinCharts(peopleCharts, deckCharts, seatCharts);
        }
    }
}
