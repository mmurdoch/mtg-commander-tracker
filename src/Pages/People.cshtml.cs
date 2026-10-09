using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class PeopleModel : PageModel
    {
        private readonly AppDbContext _db;

        public PeopleModel(AppDbContext db)
        {
            _db = db;
        }

        public List<Person> People { get; set; } = new();

        public async Task OnGetAsync()
        {
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
        }
    }
}
