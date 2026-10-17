using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Pages
{
    public record NetworkNode(int DeckId, string Label, int Games);

    public record NetworkEdge(int Source, int Target, int Games);

    public class DeckNetworkModel : PageModel
    {
        private readonly AppDbContext _db;

        public DeckNetworkModel(AppDbContext db)
        {
            _db = db;
        }

        public List<NetworkNode> Nodes { get; private set; } = new();

        public List<NetworkEdge> Edges { get; private set; } = new();

        public async Task OnGetAsync()
        {
            var decks = await _db.Decks.Include(d => d.Owner).ToListAsync();
            var labels = Deck.BuildDisplayNames(decks);

            var games = await _db.Games
                .Select(g => g.Participants.Where(p => p.DeckId != null).Select(p => p.DeckId!.Value).ToList())
                .ToListAsync();

            var pairs = new Dictionary<(int, int), int>();
            var played = new Dictionary<int, int>();
            foreach (var deckIds in games)
            {
                var distinct = deckIds.Where(labels.ContainsKey).Distinct().Order().ToList();
                if (distinct.Count < 2)
                {
                    continue;
                }

                foreach (var id in distinct)
                {
                    played[id] = played.GetValueOrDefault(id) + 1;
                }

                for (var i = 0; i < distinct.Count; i++)
                {
                    for (var j = i + 1; j < distinct.Count; j++)
                    {
                        var key = (distinct[i], distinct[j]);
                        pairs[key] = pairs.GetValueOrDefault(key) + 1;
                    }
                }
            }

            Edges = pairs
                .Select(p => new NetworkEdge(p.Key.Item1, p.Key.Item2, p.Value))
                .OrderBy(e => e.Source)
                .ThenBy(e => e.Target)
                .ToList();

            Nodes = played
                .Select(p => new NetworkNode(p.Key, labels[p.Key], p.Value))
                .OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
