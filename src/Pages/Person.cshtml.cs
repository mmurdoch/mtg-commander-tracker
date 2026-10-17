using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public record DeckShare(string Label, int Games, double Percent);

    // ExpectedPercent draws a marker at the win rate expected from chance alone; null for no marker.
    // OverallPercent adds a muted comparison bar, drawn in front only where it is shorter than the main bar.
    // Tooltip is shown on both bars so either one can be hovered.
    public record BarRow(string Label, double Percent, string Text, double? ExpectedPercent = null, double? OverallPercent = null, string? Tooltip = null)
    {
        public bool Above => ExpectedPercent is double expected && Percent > expected;
        public bool OverallAbove => ExpectedPercent is double expected && OverallPercent > expected;
        public bool OverallInFront => OverallPercent < Percent;
    }

    public class PersonModel : PageModel
    {
        public const int MaxDecks = 5;

        private readonly AppDbContext _db;

        public PersonModel(AppDbContext db)
        {
            _db = db;
        }

        public Person? Person { get; private set; }
        public IReadOnlyList<ChartEntry> WinsByPlayerCount { get; private set; } = Array.Empty<ChartEntry>();
        public IReadOnlyList<DeckShare> DeckShares { get; private set; } = Array.Empty<DeckShare>();
        public IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> Seats { get; private set; } =
            new Dictionary<int, IReadOnlyList<ChartEntry>>();
        public IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> OverallSeats { get; private set; } =
            new Dictionary<int, IReadOnlyList<ChartEntry>>();

        public string SelectedTimeframe { get; private set; } = WinCharts.AllTime;

        public DateTime Today { get; set; } = DateTime.Today;

        public async Task<IActionResult> OnGetAsync(int id, string? timeframe = null)
        {
            Person = await _db.People.FirstOrDefaultAsync(p => p.Id == id);
            if (Person is null)
            {
                return NotFound();
            }

            var selected = WinCharts.Timeframes.FirstOrDefault(t => t.Key == timeframe) ?? WinCharts.Timeframes[0];
            SelectedTimeframe = selected.Key;
            DateTime? since = selected.Months is int months ? Today.AddMonths(-months) : null;

            var games = await _db.Games
                .Where(g => since == null || g.Date >= since)
                .Where(g => g.Participants.Any(p => p.PlayerId == id))
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.OrderBy(p => p.Id).Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            var sized = WinCharts.PlayerCounts.ToDictionary(s => s, s => games.Where(g => g.Seats.Count == s).ToList());

            WinsByPlayerCount = WinCharts.PlayerCounts
                .Select(s => new ChartEntry($"{s}-player", sized[s].Count, sized[s].Count(g => g.WinnerId == id)))
                .ToList();

            Seats = WinCharts.PlayerCounts.ToDictionary(
                s => s,
                s => (IReadOnlyList<ChartEntry>)Enumerable.Range(0, s)
                    .Select(i =>
                    {
                        var atSeat = sized[s].Where(g => g.Seats[i].PlayerId == id).ToList();
                        return new ChartEntry(WinCharts.SeatNames[i], atSeat.Count, atSeat.Count(g => g.WinnerId == id));
                    })
                    .ToList());

            OverallSeats = (await WinCharts.LoadAsync(_db, new Dictionary<int, string>(), since)).Seats;

            var deckIds = games
                .Select(g => g.Seats.First(s => s.PlayerId == id).DeckId)
                .OfType<int>()
                .ToList();
            var decks = await _db.Decks.Include(d => d.Owner).ToListAsync();
            var labels = Deck.BuildDisplayNames(decks);
            var counts = deckIds
                .GroupBy(d => d)
                .Where(g => labels.ContainsKey(g.Key))
                .Select(g => (Label: labels[g.Key], Games: g.Count()))
                .OrderByDescending(d => d.Games)
                .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var total = counts.Sum(d => d.Games);

            var shares = counts.Take(MaxDecks).Select(d => new DeckShare(d.Label, d.Games, (double)d.Games / total * 100)).ToList();
            var otherGames = counts.Skip(MaxDecks).Sum(d => d.Games);
            if (otherGames > 0)
            {
                shares.Add(new DeckShare("Other", otherGames, (double)otherGames / total * 100));
            }
            DeckShares = shares;

            return Page();
        }
    }
}
