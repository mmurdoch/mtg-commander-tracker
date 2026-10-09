using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public class AddPersonModel : PageModel
    {
        private readonly AppDbContext _db;

        public AddPersonModel(AppDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public string Name { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? name = null)
        {
            var personName = name ?? Name;
            if (string.IsNullOrWhiteSpace(personName))
            {
                ModelState.AddModelError(nameof(Name), "Name is required.");
                return Page();
            }

            _db.People.Add(new Person { Name = personName.Trim() });
            await _db.SaveChangesAsync();

            return RedirectToPage("/People");
        }
    }
}
