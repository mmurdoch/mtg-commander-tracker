using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public record DeckWinStats(Deck Deck, WinRecord TwoPlayer, WinRecord ThreePlayer, WinRecord FourPlayer);

    public class DecksModel : PageModel
    {
        private readonly AppDbContext _db;

        public DecksModel(AppDbContext db)
        {
            _db = db;
        }

        public List<DeckWinStats> Decks { get; set; } = new();

        public async Task OnGetAsync()
        {
            var decks = await _db.Decks.Include(d => d.ColorData).Include(d => d.Owner).OrderBy(d => d.Commander).ThenBy(d => d.Name).ToListAsync();
            var games = await _db.Games
                .Select(g => new
                {
                    g.WinnerId,
                    Seats = g.Participants.Select(p => new { p.PlayerId, p.DeckId }).ToList()
                })
                .ToListAsync();

            WinRecord RecordFor(int deckId, int size)
            {
                var played = games
                    .Where(g => g.Seats.Count == size)
                    .Select(g => new { g.WinnerId, Seat = g.Seats.FirstOrDefault(s => s.DeckId == deckId) })
                    .Where(x => x.Seat is not null)
                    .ToList();
                return new WinRecord(played.Count, played.Count(x => x.WinnerId == x.Seat!.PlayerId));
            }

            Decks = decks
                .Select(d => new DeckWinStats(d, RecordFor(d.Id, 2), RecordFor(d.Id, 3), RecordFor(d.Id, 4)))
                .ToList();
        }
    }
}
