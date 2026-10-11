using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;

namespace mtg_game_tracker.Tests;

public class CsvDataImporterTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static CsvImportSources Sources(
        string? people = null, string? decks = null, string? games = null, string? participants = null, string? winners = null) =>
        new(
            people ?? "ID,Name,Name (ID)\n1,Alice,Alice (1)\n2,Bob,Bob (2)\n",
            decks ?? "ID,Name,Commander,Commander (ID),Colour Identity,Theme,Decklist,Owner ID,Bracket,EDHREC Rank,Notes\n" +
                     "1,Arcane,\"Niv-Mizzet, Parun \",x,Izzet,Spells,http://x,1,2,75,note\n" +
                     "2,,Edgar Markov,x,Mardu,Vampires,,2,,2,\n",
            games ?? "ID,Date\n1,2025-01-03\n2,2025-01-09\n",
            participants ?? "ID,Game ID,Player ID,Deck ID\n1,1,1,1\n2,1,2,2\n3,2,1,1\n4,2,2,2\n",
            winners ?? "Game ID,Participant ID\n1,2\n");

    [Fact]
    public async Task ImportsRowsKeepingSourceIds()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        Assert.Equal(new[] { 1, 2 }, await _context.People.OrderBy(p => p.Id).Select(p => p.Id).ToArrayAsync());
        Assert.Equal("Bob", (await _context.People.SingleAsync(p => p.Id == 2)).Name);
        Assert.Equal(new DateTime(2025, 1, 9), (await _context.Games.SingleAsync(g => g.Id == 2)).Date);
        var participant = await _context.Participants.SingleAsync(p => p.Id == 3);
        Assert.Equal((2, 1, 1), (participant.GameId, participant.PlayerId, participant.DeckId));
    }

    [Fact]
    public async Task MapsDeckFields()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        var deck = await _context.Decks.SingleAsync(d => d.Id == 1);
        Assert.Equal("Arcane", deck.Name);
        Assert.Equal("Niv-Mizzet, Parun", deck.Commander);
        Assert.Null(deck.SecondCommander);
        Assert.Equal(1, deck.OwnerId);
        Assert.Equal(ColorIdentitySeeder.All.Single(c => c.Name == "Izzet").Id, deck.ColorDataId);
    }

    [Fact]
    public async Task BlankDeckNameBecomesNull()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        Assert.Null((await _context.Decks.SingleAsync(d => d.Id == 2)).Name);
    }

    [Fact]
    public async Task SplitsPartnerCommandersOnSlash()
    {
        var decks = "ID,Name,Commander,Commander (ID),Colour Identity,Theme,Decklist,Owner ID,Bracket,EDHREC Rank,Notes\n" +
                    "1,Real Life,\"Bruse Tarl, Boorish Herder / Ikra Shidiqi, the Usurper\",x,Dune-Brood,,,1,,,\n";

        await CsvDataImporter.ImportAsync(_context, Sources(decks: decks, participants: "ID,Game ID,Player ID,Deck ID\n", winners: "Game ID,Participant ID\n"));

        var deck = await _context.Decks.SingleAsync();
        Assert.Equal("Bruse Tarl, Boorish Herder", deck.Commander);
        Assert.Equal("Ikra Shidiqi, the Usurper", deck.SecondCommander);
    }

    [Fact]
    public async Task WinnerIsThePlayerOfTheWinningParticipant()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        Assert.Equal(2, (await _context.Games.SingleAsync(g => g.Id == 1)).WinnerId);
    }

    [Fact]
    public async Task GameWithoutWinnerHasNullWinner()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        var game = await _context.Games.SingleAsync(g => g.Id == 2);
        Assert.Null(game.WinnerId);
    }

    [Fact]
    public async Task ReplacesExistingData()
    {
        _context.People.Add(new Person { Name = "Old" });
        await _context.SaveChangesAsync();

        await CsvDataImporter.ImportAsync(_context, Sources());

        Assert.DoesNotContain(await _context.People.ToListAsync(), p => p.Name == "Old");
        Assert.Equal(2, await _context.People.CountAsync());
    }

    [Fact]
    public async Task ReplacesExistingDataThatReferencesOtherRows()
    {
        var owner = new Person { Name = "Old" };
        _context.Colors.Add(ColorIdentitySeeder.All[0]);
        _context.Decks.Add(new Deck { Commander = "Old", Owner = owner, ColorDataId = ColorIdentitySeeder.All[0].Id });
        await _context.SaveChangesAsync();

        await CsvDataImporter.ImportAsync(_context, Sources());

        Assert.DoesNotContain(await _context.Decks.ToListAsync(), d => d.Commander == "Old");
    }

    [Fact]
    public async Task NewRowsAfterImportContinueFromHighestId()
    {
        await CsvDataImporter.ImportAsync(_context, Sources());

        var person = new Person { Name = "Carol" };
        _context.People.Add(person);
        await _context.SaveChangesAsync();

        Assert.Equal(3, person.Id);
    }

    [Fact]
    public async Task AppliesParticipantPlayerOverrides()
    {
        var participants = "ID,Game ID,Player ID,Deck ID\n1,1,1,1\n2,1,99,2\n";

        await CsvDataImporter.ImportAsync(
            _context,
            Sources(participants: participants),
            new Dictionary<int, int> { [2] = 2 });

        Assert.Equal(2, (await _context.Participants.SingleAsync(p => p.Id == 2)).PlayerId);
    }

    [Fact]
    public async Task UnknownColourIdentityThrows()
    {
        var decks = "ID,Name,Commander,Commander (ID),Colour Identity,Theme,Decklist,Owner ID,Bracket,EDHREC Rank,Notes\n" +
                    "1,,X,x,Rainbow,,,1,,,\n";

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => CsvDataImporter.ImportAsync(_context, Sources(decks: decks)));

        Assert.Contains("Rainbow", ex.Message);
    }

    [Fact]
    public async Task UnknownDeckOwnerThrows()
    {
        var decks = "ID,Name,Commander,Commander (ID),Colour Identity,Theme,Decklist,Owner ID,Bracket,EDHREC Rank,Notes\n" +
                    "1,,X,x,Izzet,,,42,,,\n";

        await Assert.ThrowsAsync<InvalidDataException>(() => CsvDataImporter.ImportAsync(_context, Sources(decks: decks)));
    }

    [Fact]
    public async Task UnknownParticipantPlayerThrows()
    {
        var participants = "ID,Game ID,Player ID,Deck ID\n1,1,38,1\n";

        await Assert.ThrowsAsync<InvalidDataException>(() => CsvDataImporter.ImportAsync(_context, Sources(participants: participants, winners: "Game ID,Participant ID\n")));
    }

    [Fact]
    public async Task WinnerFromAnotherGameThrows()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => CsvDataImporter.ImportAsync(_context, Sources(winners: "Game ID,Participant ID\n1,3\n")));
    }

    [Fact]
    public async Task UnknownWinnerParticipantThrows()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => CsvDataImporter.ImportAsync(_context, Sources(winners: "Game ID,Participant ID\n1,99\n")));
    }

    [Fact]
    public async Task GameWithTwoWinnersThrows()
    {
        await Assert.ThrowsAsync<InvalidDataException>(
            () => CsvDataImporter.ImportAsync(_context, Sources(winners: "Game ID,Participant ID\n1,1\n1,2\n")));
    }

    [Fact]
    public async Task FailedImportLeavesExistingDataUntouched()
    {
        _context.People.Add(new Person { Name = "Old" });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CsvDataImporter.ImportAsync(_context, Sources(winners: "Game ID,Participant ID\n1,99\n")));

        Assert.Equal("Old", (await _context.People.SingleAsync()).Name);
    }

    [Fact]
    public async Task ToleratesBomAndWindowsLineEndings()
    {
        var people = "\uFEFFID,Name,Name (ID)\r\n1,Alice,Alice (1)\r\n2,Bob,Bob (2)\r\n";

        await CsvDataImporter.ImportAsync(_context, Sources(people: people));

        Assert.Equal(2, await _context.People.CountAsync());
    }
}
