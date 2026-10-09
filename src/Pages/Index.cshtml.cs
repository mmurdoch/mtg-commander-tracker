using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;

        public IndexModel(AppDbContext db)
        {
            _db = db;
        }

        public List<RecentGameViewModel> RecentGames { get; set; } = new();

        public async Task OnGetAsync()
        {
            RecentGames = await _db.Games
                .Include(g => g.Winner)
                .Include(g => g.Participants)
                .OrderByDescending(g => g.Date)
                .Take(10)
                .Select(g => new RecentGameViewModel
                {
                    Date = g.Date,
                    WinnerName = g.Winner != null ? g.Winner.Name : "TBD",
                    Notes = g.Notes,
                    Participants = g.Participants
                        .Select(p => p.Player != null ? p.Player.Name : "Unknown")
                        .ToList()
                })
                .ToListAsync();
        }
    }

    public class RecentGameViewModel
    {
        public DateTime Date { get; set; }
        public string WinnerName { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public List<string> Participants { get; set; } = new();
    }
}
