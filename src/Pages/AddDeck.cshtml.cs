using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public class AddDeckModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ICommanderCatalog _commanderCatalog;

        public AddDeckModel(AppDbContext db, ICommanderCatalog commanderCatalog)
        {
            _db = db;
            _commanderCatalog = commanderCatalog;
        }

        public List<ColorData> Colors { get; set; } = new();
        public List<Person> People { get; set; } = new();
        public IReadOnlyList<string> Commanders { get; set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> CommanderColors { get; set; } = new Dictionary<string, string>();

        // Derived from the chosen commanders; shown read-only on the page.
        public ColorData? ColorIdentity { get; set; }

        [BindProperty]
        public string? Name { get; set; }

        [BindProperty]
        public string Commander { get; set; } = string.Empty;

        [BindProperty]
        public string? SecondCommander { get; set; }

        [BindProperty]
        public int? OwnerId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadListsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? name = null, string? commander = null, int? ownerId = null, string? secondCommander = null)
        {
            var deckName = name ?? Name;
            var deckCommander = commander ?? Commander;
            var selectedOwnerId = ownerId ?? OwnerId;
            var deckSecondCommander = secondCommander ?? SecondCommander;

            await LoadListsAsync();

            var draft = DeckFactory.Build(deckName, deckCommander, deckSecondCommander, selectedOwnerId, CommanderColors);
            ColorIdentity = draft.ColorIdentity;
            foreach (var (key, message) in draft.Errors)
            {
                ModelState.AddModelError(key, message);
            }

            if (!ModelState.IsValid || draft.Deck is null)
            {
                return Page();
            }

            _db.Decks.Add(draft.Deck);
            await _db.SaveChangesAsync();

            return RedirectToPage("/Decks");
        }

        private async Task LoadListsAsync()
        {
            var cancellationToken = HttpContext?.RequestAborted ?? default;
            Colors = await _db.Colors.OrderBy(c => c.Id).ToListAsync();
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
            Commanders = await _commanderCatalog.GetCommandersAsync(cancellationToken);
            CommanderColors = await _commanderCatalog.GetColorIdentitiesAsync(cancellationToken);
        }
    }
}

