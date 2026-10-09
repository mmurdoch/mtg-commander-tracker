using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class AddGameModel : PageModel
    {
        private readonly AppDbContext _db;

        public AddGameModel(AppDbContext db)
        {
            _db = db;
        }

        public List<Person> People { get; set; } = new();
        public List<Deck> Decks { get; set; } = new();

        [BindProperty]
        public AddGameInput Input { get; set; } = new();

        public async Task OnGetAsync()
        {
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
            Decks = await _db.Decks.OrderBy(d => d.Name).ToListAsync();

            if (!Input.ParticipantInputs.Any())
            {
                Input.ParticipantInputs.Add(new ParticipantInput());
                Input.ParticipantInputs.Add(new ParticipantInput());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
            Decks = await _db.Decks.OrderBy(d => d.Name).ToListAsync();

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var game = new Game
            {
                Date = Input.Date,
                Notes = Input.Notes,
            };

            foreach (var item in Input.ParticipantInputs.Where(x => x.PlayerId > 0))
            {
                game.Participants.Add(new Participant
                {
                    PlayerId = item.PlayerId,
                    DeckId = item.DeckId
                });
            }

            if (Input.WinnerPlayerId > 0)
            {
                game.WinnerId = Input.WinnerPlayerId;
            }

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Confirmation");
        }
    }

    public class AddGameInput
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public List<ParticipantInput> ParticipantInputs { get; set; } = new();
        public int WinnerPlayerId { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class ParticipantInput
    {
        public int PlayerId { get; set; }
        public int? DeckId { get; set; }
    }
}
