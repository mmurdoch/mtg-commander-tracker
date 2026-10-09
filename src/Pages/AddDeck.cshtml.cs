using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using System.ComponentModel.DataAnnotations;

namespace mtg_game_tracker.Pages
{
    public class AddDeckModel : PageModel
    {
        private readonly AppDbContext _db;

        public AddDeckModel(AppDbContext db)
        {
            _db = db;
        }

        public List<ColorData> Colors { get; set; } = new();
        public List<Person> People { get; set; } = new();

        [BindProperty]
        public string? Name { get; set; }

        [BindProperty]
        public string Commander { get; set; } = string.Empty;

        [BindProperty]
        public int? OwnerId { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Please select a color identity.")]
        public int? ColorDataId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadListsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? name = null, string? commander = null, int? ownerId = null, int? colorDataId = null)
        {
            var deckName = name ?? Name;
            var deckCommander = commander ?? Commander;
            var selectedOwnerId = ownerId ?? OwnerId;
            var selectedColorId = colorDataId ?? ColorDataId;

            if (string.IsNullOrWhiteSpace(deckCommander))
            {
                ModelState.AddModelError(nameof(Commander), "Commander is required.");
            }

            if (selectedOwnerId is null)
            {
                ModelState.AddModelError(nameof(OwnerId), "Please select an owner.");
            }

            if (selectedColorId is null)
            {
                ModelState.AddModelError(nameof(ColorDataId), "Please select a color identity.");
            }

            if (!ModelState.IsValid)
            {
                await LoadListsAsync();
                return Page();
            }

            _db.Decks.Add(new Deck
            {
                Name = string.IsNullOrWhiteSpace(deckName) ? null : deckName.Trim(),
                Commander = deckCommander.Trim(),
                OwnerId = selectedOwnerId!.Value,
                ColorDataId = selectedColorId!.Value
            });

            await _db.SaveChangesAsync();

            return RedirectToPage("/Decks");
        }

        private async Task LoadListsAsync()
        {
            Colors = await _db.Colors.OrderBy(c => c.Id).ToListAsync();
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
        }
    }
}

