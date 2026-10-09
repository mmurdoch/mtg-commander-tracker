using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class DecksModel : PageModel
    {
        private readonly AppDbContext _db;

        public DecksModel(AppDbContext db)
        {
            _db = db;
        }

        public List<Deck> Decks { get; set; } = new();

        public async Task OnGetAsync()
        {
            Decks = await _db.Decks.Include(d => d.ColorData).Include(d => d.Owner).OrderBy(d => d.Commander).ThenBy(d => d.Name).ToListAsync();
        }
    }
}
