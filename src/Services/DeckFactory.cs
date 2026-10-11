using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Services
{
    // Errors are keyed by the property name the form field is bound to.
    public sealed record DeckDraft(Deck? Deck, ColorData? ColorIdentity, IReadOnlyDictionary<string, string> Errors);

    public static class DeckFactory
    {
        public static DeckDraft Build(
            string? name,
            string? commander,
            string? secondCommander,
            int? ownerId,
            IReadOnlyDictionary<string, string> commanderColors)
        {
            var errors = new Dictionary<string, string>();
            ColorData? identity = null;

            if (string.IsNullOrWhiteSpace(commander))
            {
                errors["Commander"] = "Commander is required.";
            }
            else
            {
                identity = ResolveColorIdentity(commander, secondCommander, commanderColors, errors);
            }

            if (ownerId is null)
            {
                errors["OwnerId"] = "Please select an owner.";
            }

            if (errors.Count > 0 || identity is null)
            {
                return new DeckDraft(null, identity, errors);
            }

            var deck = new Deck
            {
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                Commander = commander!.Trim(),
                SecondCommander = string.IsNullOrWhiteSpace(secondCommander) ? null : secondCommander.Trim(),
                OwnerId = ownerId!.Value,
                ColorDataId = identity.Id
            };
            return new DeckDraft(deck, identity, errors);
        }

        private static ColorData? ResolveColorIdentity(
            string commander,
            string? secondCommander,
            IReadOnlyDictionary<string, string> commanderColors,
            Dictionary<string, string> errors)
        {
            var letters = new List<string>();
            foreach (var (key, value) in new[] { ("Commander", commander), ("SecondCommander", secondCommander) })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!commanderColors.TryGetValue(value.Trim(), out var colors))
                {
                    errors[key] = $"Could not determine the color identity of '{value.Trim()}'.";
                    return null;
                }

                letters.Add(colors);
            }

            var identity = ColorIdentitySeeder.Find(string.Concat(letters));
            if (identity is null)
            {
                errors["Commander"] = "Could not determine the color identity.";
            }

            return identity;
        }
    }
}
