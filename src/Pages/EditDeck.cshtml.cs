using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public class EditDeckModel : PageModel
    {
        private readonly AppDbContext _db;

        public EditDeckModel(AppDbContext db)
        {
            _db = db;
        }

        public Deck? Deck { get; private set; }
        public List<Person> People { get; private set; } = new();

        [BindProperty]
        public string? Name { get; set; }

        [BindProperty]
        public int? OwnerId { get; set; }

        [BindProperty]
        public string? DeckListUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Deck = await _db.Decks.Include(d => d.Owner).FirstOrDefaultAsync(d => d.Id == id);
            if (Deck is null)
            {
                return NotFound();
            }

            Name = Deck.Name;
            OwnerId = Deck.OwnerId;
            DeckListUrl = Deck.DeckListUrl;
            await LoadPeopleAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            Deck = await _db.Decks.FirstOrDefaultAsync(d => d.Id == id);
            if (Deck is null)
            {
                return NotFound();
            }

            await LoadPeopleAsync();
            if (OwnerId is null || People.All(person => person.Id != OwnerId.Value))
            {
                ModelState.AddModelError(nameof(OwnerId), "Please select a valid owner.");
            }

            if (!DeckListUrlValidator.TryNormalize(DeckListUrl, out var normalizedDeckListUrl))
            {
                ModelState.AddModelError(nameof(DeckListUrl), "Please enter a valid URL.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            Deck.Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
            Deck.OwnerId = OwnerId!.Value;
            Deck.DeckListUrl = normalizedDeckListUrl;
            await _db.SaveChangesAsync();

            return RedirectToPage("/Decks");
        }

        private async Task LoadPeopleAsync()
        {
            People = await _db.People.OrderBy(person => person.Name).ToListAsync();
        }
    }
}
