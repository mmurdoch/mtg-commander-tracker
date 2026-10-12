using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public record MatchupDeck(int DeckId, string Label, string Commander, string? SecondCommander);

    public record MatchupRecord(int Games, int Wins, int Losses);

    // Wins and Losses are from the selected deck's point of view; other games were drawn or won by a third deck.
    // BySize is keyed by game size (2, 3 or 4 players); the top-level counts cover games of every size.
    public record DeckMatchup(int DeckId, string Label, string Commander, string? SecondCommander, int Games, int Wins, int Losses, IReadOnlyDictionary<int, MatchupRecord> BySize);

    public class DeckMatchupsModel : PageModel
    {
        private readonly AppDbContext _db;

        public DeckMatchupsModel(AppDbContext db)
        {
            _db = db;
        }

        public MatchupDeck? Selected { get; private set; }

        public List<DeckMatchup> Matchups { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var decks = await _db.Decks.Include(d => d.Owner).ToListAsync();
            if (decks.All(d => d.Id != id))
            {
                return NotFound();
            }

            var labels = Deck.BuildDisplayNames(decks);
            var byId = decks.ToDictionary(d => d.Id);
            MatchupDeck Describe(Deck d) => new(d.Id, labels[d.Id], d.Commander, string.IsNullOrEmpty(d.SecondCommander) ? null : d.SecondCommander);
            Selected = Describe(byId[id]);

            var games = await _db.Games
                .Where(g => g.Participants.Any(p => p.DeckId == id))
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            var tallies = new Dictionary<int, Dictionary<int, MatchupRecord>>();
            var allGames = new Dictionary<int, MatchupRecord>();
            foreach (var game in games)
            {
                var mine = game.Seats.First(s => s.DeckId == id);
                foreach (var seat in game.Seats.Where(s => s.DeckId is int other && other != id && byId.ContainsKey(other)))
                {
                    var opponentId = seat.DeckId!.Value;
                    var win = game.WinnerId == mine.PlayerId ? 1 : 0;
                    var loss = game.WinnerId == seat.PlayerId ? 1 : 0;
                    allGames[opponentId] = Add(allGames.GetValueOrDefault(opponentId), win, loss);
                    if (!tallies.TryGetValue(opponentId, out var bySize))
                    {
                        tallies[opponentId] = bySize = new Dictionary<int, MatchupRecord>();
                    }
                    bySize[game.Seats.Count] = Add(bySize.GetValueOrDefault(game.Seats.Count), win, loss);
                }
            }

            Matchups = tallies
                .Select(t =>
                {
                    var d = Describe(byId[t.Key]);
                    var total = allGames[t.Key];
                    var bySize = WinCharts.GameSizes.ToDictionary(s => s, s => t.Value.GetValueOrDefault(s, new MatchupRecord(0, 0, 0)));
                    return new DeckMatchup(d.DeckId, d.Label, d.Commander, d.SecondCommander, total.Games, total.Wins, total.Losses, bySize);
                })
                .OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Page();
        }

        private static MatchupRecord Add(MatchupRecord? record, int win, int loss) =>
            new((record?.Games ?? 0) + 1, (record?.Wins ?? 0) + win, (record?.Losses ?? 0) + loss);
    }
}
