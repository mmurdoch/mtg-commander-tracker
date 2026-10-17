using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public class DeckModel : PageModel
    {
        private readonly AppDbContext _db;

        public DeckModel(AppDbContext db)
        {
            _db = db;
        }

        public Deck? Deck { get; private set; }
        public string Label { get; private set; } = "";
        public IReadOnlyList<ChartEntry> WinsByPlayerCount { get; private set; } = Array.Empty<ChartEntry>();
        public IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> Seats { get; private set; } =
            new Dictionary<int, IReadOnlyList<ChartEntry>>();
        public IReadOnlyDictionary<int, IReadOnlyList<ChartEntry>> OverallSeats { get; private set; } =
            new Dictionary<int, IReadOnlyList<ChartEntry>>();

        public string SelectedTimeframe { get; private set; } = WinCharts.AllTime;

        public DateTime Today { get; set; } = DateTime.Today;

        public async Task<IActionResult> OnGetAsync(int id, string? timeframe = null)
        {
            var decks = await _db.Decks.Include(d => d.Owner).ToListAsync();
            Deck = decks.FirstOrDefault(d => d.Id == id);
            if (Deck is null)
            {
                return NotFound();
            }
            Label = Deck.BuildDisplayNames(decks)[id];

            var selected = WinCharts.Timeframes.FirstOrDefault(t => t.Key == timeframe) ?? WinCharts.Timeframes[0];
            SelectedTimeframe = selected.Key;
            DateTime? since = selected.Months is int months ? Today.AddMonths(-months) : null;

            var games = await _db.Games
                .Where(g => since == null || g.Date >= since)
                .Where(g => g.Participants.Any(p => p.DeckId == id))
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.OrderBy(p => p.Id).Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            // The deck wins when its pilot wins the game.
            var played = games
                .Select(g =>
                {
                    var seat = g.Seats.FindIndex(s => s.DeckId == id);
                    return (Count: g.Seats.Count, Seat: seat, Won: g.WinnerId == g.Seats[seat].PlayerId);
                })
                .ToList();

            WinsByPlayerCount = WinCharts.PlayerCounts
                .Select(s => new ChartEntry($"{s}-player", played.Count(g => g.Count == s), played.Count(g => g.Count == s && g.Won)))
                .ToList();

            Seats = WinCharts.PlayerCounts.ToDictionary(
                s => s,
                s => (IReadOnlyList<ChartEntry>)Enumerable.Range(0, s)
                    .Select(i =>
                    {
                        var atSeat = played.Where(g => g.Count == s && g.Seat == i).ToList();
                        return new ChartEntry(WinCharts.SeatNames[i], atSeat.Count, atSeat.Count(g => g.Won));
                    })
                    .ToList());

            OverallSeats = (await WinCharts.LoadAsync(_db, new Dictionary<int, string>(), since)).Seats;

            return Page();
        }
    }
}
