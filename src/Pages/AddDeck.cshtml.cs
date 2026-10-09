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

            if (string.IsNullOrWhiteSpace(deckCommander))
            {
                ModelState.AddModelError(nameof(Commander), "Commander is required.");
            }
            else
            {
                ColorIdentity = ResolveColorIdentity(deckCommander, deckSecondCommander);
            }

            if (selectedOwnerId is null)
            {
                ModelState.AddModelError(nameof(OwnerId), "Please select an owner.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            _db.Decks.Add(new Deck
            {
                Name = string.IsNullOrWhiteSpace(deckName) ? null : deckName.Trim(),
                Commander = deckCommander.Trim(),
                SecondCommander = string.IsNullOrWhiteSpace(deckSecondCommander) ? null : deckSecondCommander.Trim(),
                OwnerId = selectedOwnerId!.Value,
                ColorDataId = ColorIdentity!.Id
            });

            await _db.SaveChangesAsync();

            return RedirectToPage("/Decks");
        }

        private ColorData? ResolveColorIdentity(string commander, string? secondCommander)
        {
            var letters = new List<string>();
            foreach (var (key, value) in new[] { (nameof(Commander), commander), (nameof(SecondCommander), secondCommander) })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!CommanderColors.TryGetValue(value.Trim(), out var colors))
                {
                    ModelState.AddModelError(key, $"Could not determine the color identity of '{value.Trim()}'.");
                    return null;
                }

                letters.Add(colors);
            }

            var identity = ColorIdentitySeeder.Find(string.Concat(letters));
            if (identity is null)
            {
                ModelState.AddModelError(nameof(Commander), "Could not determine the color identity.");
            }

            return identity;
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

