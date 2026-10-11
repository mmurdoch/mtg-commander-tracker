using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Data
{
    public sealed record CsvImportSources(string People, string Decks, string Games, string Participants, string Winners);

    // One-off importer that replaces all people, decks, games and participants with spreadsheet CSV exports.
    public static class CsvDataImporter
    {
        private const string PartnerSeparator = " / ";

        public static async Task ImportAsync(
            AppDbContext context,
            CsvImportSources sources,
            IReadOnlyDictionary<int, int>? participantPlayerOverrides = null)
        {
            var people = ReadPeople(sources.People);
            var decks = ReadDecks(sources.Decks, people);
            var games = ReadGames(sources.Games);
            var participants = ReadParticipants(sources.Participants, people, decks, games, participantPlayerOverrides);
            ApplyWinners(sources.Winners, games, participants);

            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync();

            await context.Database.ExecuteSqlRawAsync("DELETE FROM Participants");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Games");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM Decks");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM People");
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM sqlite_sequence WHERE name IN ('Participants', 'Games', 'Decks', 'People')");

            var existingColorIds = await context.Colors.Select(c => c.Id).ToListAsync();
            context.Colors.AddRange(ColorIdentitySeeder.All.Where(c => !existingColorIds.Contains(c.Id)));

            context.People.AddRange(people.Values);
            context.Decks.AddRange(decks.Values);
            context.Games.AddRange(games.Values);
            context.Participants.AddRange(participants.Values);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            context.ChangeTracker.Clear();
        }

        private static Dictionary<int, Person> ReadPeople(string csv)
        {
            var people = new Dictionary<int, Person>();
            foreach (var row in ReadRows(csv))
            {
                var id = row.Int("ID");
                AddUnique(people, id, new Person { Id = id, Name = row.Text("Name") }, "People");
            }

            return people;
        }

        private static Dictionary<int, Deck> ReadDecks(string csv, Dictionary<int, Person> people)
        {
            var decks = new Dictionary<int, Deck>();
            foreach (var row in ReadRows(csv))
            {
                var id = row.Int("ID");
                var ownerId = row.Int("Owner ID");
                if (!people.ContainsKey(ownerId))
                {
                    throw new InvalidDataException($"Deck {id} has unknown Owner ID {ownerId}.");
                }

                var colourName = row.Text("Colour Identity");
                var colour = ColorIdentitySeeder.All.FirstOrDefault(c => string.Equals(c.Name, colourName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Deck {id} has unknown Colour Identity '{colourName}'.");

                var commanders = row.Text("Commander").Split(PartnerSeparator, 2, StringSplitOptions.TrimEntries);
                if (string.IsNullOrWhiteSpace(commanders[0]))
                {
                    throw new InvalidDataException($"Deck {id} has no Commander.");
                }

                var name = row.Text("Name");
                AddUnique(decks, id, new Deck
                {
                    Id = id,
                    Name = name.Length == 0 ? null : name,
                    Commander = commanders[0],
                    SecondCommander = commanders.Length > 1 && commanders[1].Length > 0 ? commanders[1] : null,
                    OwnerId = ownerId,
                    ColorDataId = colour.Id,
                }, "Decks");
            }

            return decks;
        }

        private static Dictionary<int, Game> ReadGames(string csv)
        {
            var games = new Dictionary<int, Game>();
            foreach (var row in ReadRows(csv))
            {
                var id = row.Int("ID");
                var dateText = row.Text("Date");
                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new InvalidDataException($"Game {id} has invalid Date '{dateText}'.");
                }

                AddUnique(games, id, new Game { Id = id, Date = date }, "Games");
            }

            return games;
        }

        private static Dictionary<int, Participant> ReadParticipants(
            string csv,
            Dictionary<int, Person> people,
            Dictionary<int, Deck> decks,
            Dictionary<int, Game> games,
            IReadOnlyDictionary<int, int>? playerOverrides)
        {
            var participants = new Dictionary<int, Participant>();
            foreach (var row in ReadRows(csv))
            {
                var id = row.Int("ID");
                var gameId = row.Int("Game ID");
                var playerId = playerOverrides is not null && playerOverrides.TryGetValue(id, out var overridden)
                    ? overridden
                    : row.Int("Player ID");
                var deckId = row.OptionalInt("Deck ID");

                if (!games.ContainsKey(gameId))
                {
                    throw new InvalidDataException($"Participant {id} has unknown Game ID {gameId}.");
                }

                if (!people.ContainsKey(playerId))
                {
                    throw new InvalidDataException($"Participant {id} has unknown Player ID {playerId}.");
                }

                if (deckId is not null && !decks.ContainsKey(deckId.Value))
                {
                    throw new InvalidDataException($"Participant {id} has unknown Deck ID {deckId}.");
                }

                AddUnique(participants, id, new Participant { Id = id, GameId = gameId, PlayerId = playerId, DeckId = deckId }, "Participants");
            }

            return participants;
        }

        // The winner column holds a participant, but Games.WinnerId references a person.
        private static void ApplyWinners(string csv, Dictionary<int, Game> games, Dictionary<int, Participant> participants)
        {
            var decided = new HashSet<int>();
            foreach (var row in ReadRows(csv))
            {
                var gameId = row.Int("Game ID");
                var participantId = row.Int("Participant ID");

                if (!games.TryGetValue(gameId, out var game))
                {
                    throw new InvalidDataException($"Winner row references unknown Game ID {gameId}.");
                }

                if (!participants.TryGetValue(participantId, out var participant))
                {
                    throw new InvalidDataException($"Game {gameId} has winner with unknown Participant ID {participantId}.");
                }

                if (participant.GameId != gameId)
                {
                    throw new InvalidDataException($"Winner participant {participantId} did not play in game {gameId}.");
                }

                if (!decided.Add(gameId))
                {
                    throw new InvalidDataException($"Game {gameId} has more than one winner.");
                }

                game.WinnerId = participant.PlayerId;
            }
        }

        private static void AddUnique<T>(Dictionary<int, T> rows, int id, T value, string table)
        {
            if (!rows.TryAdd(id, value))
            {
                throw new InvalidDataException($"Duplicate ID {id} in {table}.");
            }
        }

        private static IEnumerable<CsvRow> ReadRows(string csv)
        {
            var records = Parse(csv);
            if (records.Count == 0)
            {
                yield break;
            }

            var header = records[0].Select(h => h.Trim()).ToList();
            for (var i = 1; i < records.Count; i++)
            {
                yield return new CsvRow(header, records[i], i + 1);
            }
        }

        // RFC 4180 parsing: quoted fields may contain commas, doubled quotes and line breaks.
        private static List<string[]> Parse(string csv)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var hasContent = false;

            if (csv.Length > 0 && csv[0] == '\uFEFF')
            {
                csv = csv[1..];
            }

            void EndField()
            {
                fields.Add(field.ToString());
                field.Clear();
            }

            void EndRecord()
            {
                if (hasContent)
                {
                    EndField();
                    records.Add(fields.ToArray());
                }

                fields.Clear();
                field.Clear();
                hasContent = false;
            }

            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        hasContent = true;
                        break;
                    case ',':
                        EndField();
                        hasContent = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        EndRecord();
                        break;
                    default:
                        field.Append(c);
                        hasContent = true;
                        break;
                }
            }

            EndRecord();
            return records;
        }

        private sealed class CsvRow(List<string> header, string[] values, int lineNumber)
        {
            public string Text(string column)
            {
                var index = header.IndexOf(column);
                if (index < 0)
                {
                    throw new InvalidDataException($"Missing column '{column}'.");
                }

                return index < values.Length ? values[index].Trim() : string.Empty;
            }

            public int Int(string column) =>
                int.TryParse(Text(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : throw new InvalidDataException($"Row {lineNumber}: '{column}' is not a whole number.");

            public int? OptionalInt(string column) => Text(column).Length == 0 ? null : Int(column);
        }
    }
}
