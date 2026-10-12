using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Services;

namespace mtg_game_tracker.Pages
{
    public class AddGameModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ICommanderCatalog _commanderCatalog;

        public AddGameModel(AppDbContext db, ICommanderCatalog commanderCatalog)
        {
            _db = db;
            _commanderCatalog = commanderCatalog;
        }

        public List<Person> People { get; set; } = new();
        public List<Deck> Decks { get; set; } = new();
        public Dictionary<int, string> DeckLabels { get; private set; } = new();

        public IReadOnlyDictionary<string, WinCharts> ChartsByTimeframe { get; private set; } =
            WinCharts.Timeframes.ToDictionary(t => t.Key, _ => WinCharts.Empty);

        public WinCharts Charts => ChartsByTimeframe[WinCharts.AllTime];

        public bool FocusFirstDeck { get; private set; }

        [BindProperty]
        public AddGameInput Input { get; set; } = new();

        public async Task OnGetAsync()
        {
            await LoadListsAsync();
            ChartsByTimeframe = await WinCharts.LoadByTimeframeAsync(_db, DeckLabels, DateTime.Today);

            // Carry over the settings of the game just saved; everything else starts blank.
            FocusFirstDeck = TempData.ContainsKey("SuccessMessage");
            if (TempData["PlayerCountOption"] is string option) Input.PlayerCountOption = option;
            if (TempData["MorePlayerCount"] is string more && int.TryParse(more, out var morePlayers)) Input.MorePlayerCount = morePlayers;
            if (TempData["Date"] is DateTime date) Input.Date = date;

            EnsureParticipantRows();
        }

        private async Task LoadListsAsync()
        {
            People = await _db.People.OrderBy(p => p.Name).ToListAsync();
            var decks = await _db.Decks.Include(d => d.Owner).ToListAsync();
            DeckLabels = Deck.BuildDisplayNames(decks);
            Decks = decks.OrderBy(d => DeckLabels[d.Id], StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void EnsureParticipantRows()
        {
            while (Input.ParticipantInputs.Count < AddGameInput.MaxPlayers)
            {
                Input.ParticipantInputs.Add(new ParticipantInput());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadListsAsync();

            if (!ModelState.IsValid)
            {
                ChartsByTimeframe = await WinCharts.LoadByTimeframeAsync(_db, DeckLabels, DateTime.Today);
                EnsureParticipantRows();
                return Page();
            }

            var game = new Game
            {
                Date = Input.Date,
            };

            foreach (var item in Input.ParticipantInputs.Take(Input.PlayerCount).Where(x => x.PlayerId > 0))
            {
                game.Participants.Add(new Participant
                {
                    PlayerId = item.PlayerId!.Value,
                    DeckId = item.DeckId
                });
            }

            if (!Input.IsDraw && Input.WinnerPlayerId > 0)
            {
                game.WinnerId = Input.WinnerPlayerId;
            }

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = BuildSuccessMessage(game);
            TempData["PlayerCountOption"] = Input.PlayerCountOption;
            TempData["MorePlayerCount"] = Input.MorePlayerCount.ToString();
            TempData["Date"] = Input.Date.Date;

            return RedirectToPage("/AddGame");
        }

        private string BuildSuccessMessage(Game game)
        {
            var names = People.ToDictionary(p => p.Id, p => p.Name);
            var players = game.Participants.Select(p =>
            {
                var name = names.GetValueOrDefault(p.PlayerId, "Unknown");
                return p.DeckId is int deckId && DeckLabels.TryGetValue(deckId, out var label) ? $"{name} ({label})" : name;
            });
            var result = game.WinnerId is int winnerId ? $"Winner: {names.GetValueOrDefault(winnerId, "Unknown")}" : "Draw";
            return $"Game added successfully. {game.Date:yyyy-MM-dd} \u2014 {string.Join(", ", players)}. {result}.";
        }

        // Loaded on demand so the page itself doesn't wait on the commander catalog.
        public async Task<IActionResult> OnGetCommandersAsync()
        {
            var cancellationToken = HttpContext?.RequestAborted ?? default;
            var names = await _commanderCatalog.GetCommandersAsync(cancellationToken);
            var colors = await _commanderCatalog.GetColorIdentitiesAsync(cancellationToken);
            IReadOnlyList<CommanderOption> options = names
                .Select(n => new CommanderOption(n, colors.TryGetValue(n, out var letters) ? letters : null))
                .ToList();
            return new JsonResult(options);
        }

        public async Task<IActionResult> OnPostAddDeckAsync(string? name, string? commander, string? secondCommander, int? ownerId)
        {
            var colors = await _commanderCatalog.GetColorIdentitiesAsync(HttpContext?.RequestAborted ?? default);
            var draft = DeckFactory.Build(name, commander, secondCommander, ownerId, colors);
            if (draft.Deck is null || draft.Errors.Count > 0)
            {
                return new JsonResult(new AddDeckErrors(draft.Errors)) { StatusCode = StatusCodes.Status400BadRequest };
            }

            _db.Decks.Add(draft.Deck);
            await _db.SaveChangesAsync();

            await LoadListsAsync();
            var options = Decks.Select(d => new DeckOption(d.Id, DeckLabels[d.Id], d.OwnerId)).ToList();
            return new JsonResult(new AddDeckResponse(draft.Deck.Id, options));
        }
    }

    public record CommanderOption(string Name, string? Colors);
    public record DeckOption(int Id, string Label, int OwnerId);
    public record AddDeckResponse(int DeckId, IReadOnlyList<DeckOption> Decks);
    public record AddDeckErrors(IReadOnlyDictionary<string, string> Errors);

    public class AddGameInput
    {
        public const int MinMorePlayers = 5;
        public const int MaxPlayers = 10;

        public DateTime Date { get; set; } = DateTime.Today;
        public string PlayerCountOption { get; set; } = "4";
        public int MorePlayerCount { get; set; } = MinMorePlayers;

        public int PlayerCount => PlayerCountOption switch
        {
            "2" => 2,
            "3" => 3,
            "more" => Math.Clamp(MorePlayerCount, MinMorePlayers, MaxPlayers),
            _ => 4,
        };
        public List<ParticipantInput> ParticipantInputs { get; set; } = new();
        public int WinnerPlayerId { get; set; }
        public bool IsDraw { get; set; }
    }

    public class ParticipantInput
    {
        public int? PlayerId { get; set; }
        public int? DeckId { get; set; }
    }
}
