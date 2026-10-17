using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DecksPageWinStatsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DecksPageWinStatsTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DecksPageWinStatsTests-{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(inMemoryDbName));
            });
        });
    }

    // Atraxa 2P 2/3; Meren 2P 1/3; Korvold only in 3P (won); 4P is a draw; 5P game ignored; Zur has no games.
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var colorId = ColorIdentitySeeder.All.First().Id;

        var alice = new Person { Name = "Alice" };
        var bob = new Person { Name = "Bob" };
        var carol = new Person { Name = "Carol" };
        var dave = new Person { Name = "Dave" };
        db.People.AddRange(alice, bob, carol, dave);
        await db.SaveChangesAsync();

        Deck NewDeck(string commander, Person owner) =>
            new() { Commander = commander, OwnerId = owner.Id, ColorDataId = colorId };
        var atraxa = NewDeck("Atraxa", alice);
        var meren = NewDeck("Meren", bob);
        var korvold = NewDeck("Korvold", carol);
        var krenko = NewDeck("Krenko", dave);
        var zur = NewDeck("Zur", dave);
        db.Decks.AddRange(atraxa, meren, korvold, krenko, zur);
        await db.SaveChangesAsync();

        void AddGame(Person? winner, params (Person Player, Deck? Deck)[] seats)
        {
            var game = new Game { Date = new DateTime(2024, 1, 1), WinnerId = winner?.Id };
            foreach (var (player, deck) in seats)
            {
                game.Participants.Add(new Participant { PlayerId = player.Id, DeckId = deck?.Id });
            }
            db.Games.Add(game);
        }

        AddGame(alice, (alice, atraxa), (bob, meren));
        AddGame(alice, (alice, atraxa), (bob, meren));
        AddGame(bob, (alice, atraxa), (bob, meren));
        AddGame(carol, (alice, atraxa), (bob, meren), (carol, korvold));
        AddGame(null, (alice, atraxa), (bob, meren), (carol, korvold), (dave, krenko));
        AddGame(dave, (alice, atraxa), (bob, meren), (carol, korvold), (dave, krenko), (bob, null));
        AddGame(dave, (carol, null), (dave, krenko));
        await db.SaveChangesAsync();
    }

    private async Task<List<DeckWinStats>> LoadStatsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new DecksModel(db);
        await model.OnGetAsync();
        return model.Decks;
    }

    [Fact]
    public async Task OnGet_ComputesWinRecordsForTwoThreeAndFourPlayerGames()
    {
        await SeedAsync();

        var stats = await LoadStatsAsync();

        var atraxa = stats.Single(s => s.Deck.Commander == "Atraxa");
        Assert.Equal((3, 2), (atraxa.TwoPlayer.Games, atraxa.TwoPlayer.Wins));
        Assert.Equal((1, 0), (atraxa.ThreePlayer.Games, atraxa.ThreePlayer.Wins));
        Assert.Equal((1, 0), (atraxa.FourPlayer.Games, atraxa.FourPlayer.Wins));

        var meren = stats.Single(s => s.Deck.Commander == "Meren");
        Assert.Equal((3, 1), (meren.TwoPlayer.Games, meren.TwoPlayer.Wins));

        var korvold = stats.Single(s => s.Deck.Commander == "Korvold");
        Assert.Null(korvold.TwoPlayer.Percent);
        Assert.Equal(100, korvold.ThreePlayer.Percent);
    }

    [Fact]
    public async Task OnGet_CountsPlayersWithoutDeckTowardPlayerCountAndIgnoresLargeGames()
    {
        await SeedAsync();

        var krenko = (await LoadStatsAsync()).Single(s => s.Deck.Commander == "Krenko");

        Assert.Equal((1, 1), (krenko.TwoPlayer.Games, krenko.TwoPlayer.Wins));
        Assert.Equal((1, 0), (krenko.FourPlayer.Games, krenko.FourPlayer.Wins));
    }

    [Fact]
    public async Task OnGet_DeckWithNoGamesHasNoPercentages()
    {
        await SeedAsync();

        var zur = (await LoadStatsAsync()).Single(s => s.Deck.Commander == "Zur");

        Assert.Null(zur.TwoPlayer.Percent);
        Assert.Null(zur.ThreePlayer.Percent);
        Assert.Null(zur.FourPlayer.Percent);
    }

    [Fact]
    public async Task OnGet_KeepsExistingOrderByCommanderThenName()
    {
        await SeedAsync();

        var stats = await LoadStatsAsync();

        Assert.Equal(new[] { "Atraxa", "Korvold", "Krenko", "Meren", "Zur" }, stats.Select(s => s.Deck.Commander));
    }

    [Fact]
    public async Task DecksPage_RendersWinPercentagesAndSortableHeaders()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Decks");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-sortable", html);
        Assert.Contains("2-Player Win %", html);
        Assert.Contains("3-Player Win %", html);
        Assert.Contains("4-Player Win %", html);
        Assert.Contains("<td data-sort-value=\"66.6667\">67% (2/3)</td>", html);
        Assert.Contains("<td data-sort-value=\"33.3333\">33% (1/3)</td>", html);
        Assert.Contains("<td data-sort-value=\"100\">100% (1/1)</td>", html);
        Assert.Contains("<td data-sort-value=\"\">-</td>", html);
        Assert.Contains("table-sort.js", html);
    }
}
