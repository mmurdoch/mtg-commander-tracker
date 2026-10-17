using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public record DeckShare(string Label, int Games, double Percent, int? DeckId = null);

    // ExpectedPercent draws a marker at the win rate expected from chance alone; null for no marker.
    // OverallPercent adds a muted comparison bar, drawn in front only where it is shorter than the main bar.
    // Tooltip is shown on both bars so either one can be hovered. Url turns the label into a link.
    public record BarRow(string Label, double Percent, string Text, double? ExpectedPercent = null, double? OverallPercent = null, string? Tooltip = null, string? Url = null)
    {
        public bool Above => ExpectedPercent is double expected && Percent > expected;
        public bool OverallAbove => ExpectedPercent is double expected && OverallPercent > expected;
        public bool OverallInFront => OverallPercent < Percent;
    }

    // Counts has one entry per month in PersonModel.PlayMonths.
    public record DeckMonthlyPlays(int DeckId, string Label, IReadOnlyList<int> Counts);

    public class PersonModel : PageModel
    {
        public const int MaxDecks = 5;
        public const string UsageCount = "count";
        public const string UsagePercent = "percent";

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

        public IReadOnlyList<DateTime> PlayMonths { get; private set; } = Array.Empty<DateTime>();
        public IReadOnlyList<DeckMonthlyPlays> DeckPlays { get; private set; } = Array.Empty<DeckMonthlyPlays>();

        // Total plays of the owner's decks in each month of PlayMonths; the denominator for percent usage.
        public IReadOnlyList<int> MonthTotals { get; private set; } = Array.Empty<int>();

        public string SelectedTimeframe { get; private set; } = WinCharts.AllTime;
        public string SelectedUsage { get; private set; } = UsagePercent;

        public DateTime Today { get; set; } = DateTime.Today;

        public async Task<IActionResult> OnGetAsync(int id, string? timeframe = null, string? usage = null)
        {
            SelectedUsage = usage == UsageCount ? UsageCount : UsagePercent;
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
                .Select(g => (Id: g.Key, Label: labels[g.Key], Games: g.Count()))
                .OrderByDescending(d => d.Games)
                .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var total = counts.Sum(d => d.Games);

            var shares = counts.Take(MaxDecks).Select(d => new DeckShare(d.Label, d.Games, (double)d.Games / total * 100, d.Id)).ToList();
            var otherGames = counts.Skip(MaxDecks).Sum(d => d.Games);
            if (otherGames > 0)
            {
                shares.Add(new DeckShare("Other", otherGames, (double)otherGames / total * 100));
            }
            DeckShares = shares;

            await LoadDeckPlaysAsync(id, labels, since);

            return Page();
        }

        // Plays of the person's own decks (by anyone), bucketed by calendar month up to the current month.
        private async Task LoadDeckPlaysAsync(int ownerId, IReadOnlyDictionary<int, string> labels, DateTime? since)
        {
            var ownedIds = await _db.Decks.Where(d => d.OwnerId == ownerId).Select(d => d.Id).ToListAsync();
            var plays = await _db.Participants
                .Where(p => p.DeckId != null && ownedIds.Contains(p.DeckId.Value))
                .Where(p => since == null || p.Game!.Date >= since)
                .Select(p => new { DeckId = p.DeckId!.Value, p.Game!.Date })
                .ToListAsync();
            if (plays.Count == 0)
            {
                PlayMonths = Array.Empty<DateTime>();
                DeckPlays = Array.Empty<DeckMonthlyPlays>();
                MonthTotals = Array.Empty<int>();
                return;
            }

            var first = since ?? plays.Min(p => p.Date);
            var start = new DateTime(first.Year, first.Month, 1);
            var end = new DateTime(Today.Year, Today.Month, 1);
            var last = plays.Max(p => p.Date);
            if (last > end)
            {
                end = new DateTime(last.Year, last.Month, 1);
            }
            var months = new List<DateTime>();
            for (var month = start; month <= end; month = month.AddMonths(1))
            {
                months.Add(month);
            }
            PlayMonths = months;
            MonthTotals = months.Select(m => plays.Count(p => p.Date.Year == m.Year && p.Date.Month == m.Month)).ToList();

            DeckPlays = plays
                .GroupBy(p => p.DeckId)
                .Select(g => new DeckMonthlyPlays(
                    g.Key,
                    labels[g.Key],
                    months.Select(m => g.Count(p => p.Date.Year == m.Year && p.Date.Month == m.Month)).ToList()))
                .OrderByDescending(d => d.Counts.Sum())
                .ThenBy(d => d.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
