using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class GamesModel : PageModel
    {
        private readonly AppDbContext _db;

        public GamesModel(AppDbContext db)
        {
            _db = db;
        }

        public List<GameViewModel> Games { get; set; } = new();

        public async Task OnGetAsync()
        {
            var games = await _db.Games
                .Include(g => g.Winner)
                .Include(g => g.Participants).ThenInclude(p => p.Player)
                .Include(g => g.Participants).ThenInclude(p => p.Deck)
                .AsSplitQuery()
                .ToListAsync();

            Games = games
                .OrderByDescending(g => g.Date)
                .ThenByDescending(g => g.Id)
                .Select(g => new GameViewModel
                {
                    Id = g.Id,
                    Date = g.Date,
                    HasWinner = g.WinnerId.HasValue,
                    Players = g.Participants
                        .OrderBy(p => p.Id)
                        .Select(p => new GamePlayerViewModel
                        {
                            PersonName = p.Player?.Name ?? "Unknown",
                            DeckName = p.Deck is null ? "No deck" : p.Deck.DisplayName,
                            IsWinner = g.WinnerId.HasValue && p.PlayerId == g.WinnerId
                        })
                        .ToList()
                })
                .ToList();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var game = await _db.Games
                .Include(g => g.Participants)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (game != null)
            {
                _db.Games.Remove(game);
                await _db.SaveChangesAsync();
            }
            return RedirectToPage();
        }

    }

    public class GameViewModel
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public bool HasWinner { get; set; }
        public List<GamePlayerViewModel> Players { get; set; } = new();
    }

    public class GamePlayerViewModel
    {
        public string PersonName { get; set; } = string.Empty;
        public string DeckName { get; set; } = string.Empty;
        public bool IsWinner { get; set; }
    }
}
