using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public record WinRecord(int Games, int Wins)
    {
        public double? Percent => Games == 0 ? null : (double)Wins / Games * 100;
    }

    public record PersonWinStats(Person Person, WinRecord TwoPlayer, WinRecord ThreePlayer, WinRecord FourPlayer);

    public class PeopleModel : PageModel
    {
        private readonly AppDbContext _db;

        public PeopleModel(AppDbContext db)
        {
            _db = db;
        }

        public List<PersonWinStats> People { get; set; } = new();

        public async Task OnGetAsync()
        {
            var people = await _db.People.OrderBy(p => p.Name).ToListAsync();
            var games = await _db.Games
                .Select(g => new { g.WinnerId, PlayerIds = g.Participants.Select(p => p.PlayerId).ToList() })
                .ToListAsync();

            WinRecord RecordFor(int personId, int size)
            {
                var played = games.Where(g => g.PlayerIds.Count == size && g.PlayerIds.Contains(personId)).ToList();
                return new WinRecord(played.Count, played.Count(g => g.WinnerId == personId));
            }

            People = people
                .Select(p => new PersonWinStats(p, RecordFor(p.Id, 2), RecordFor(p.Id, 3), RecordFor(p.Id, 4)))
                .ToList();
        }
    }
}
