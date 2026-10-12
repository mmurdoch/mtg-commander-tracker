using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DeckMatchupsPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private int _atraxaId;
    private int _unusedId;

    public DeckMatchupsPageTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DeckMatchupsPageTests-{Guid.NewGuid()}";

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

    // Atraxa vs Meren: 4 games (1 win, 1 loss, a 3-way won by Korvold, a draw). Atraxa vs Korvold: 2 games (1 win, 1 loss).
    // Atraxa vs partner deck Tymna/Thrasios: 1 game won by Atraxa. Zur never plays Atraxa.
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

        Deck NewDeck(string commander, Person owner, string? name = null, string? second = null) =>
            new() { Commander = commander, SecondCommander = second, Name = name, OwnerId = owner.Id, ColorDataId = colorId };
        var atraxa = NewDeck("Atraxa", alice);
        var meren = NewDeck("Meren", bob, "Graveyard");
        var korvold = NewDeck("Korvold", carol);
        var tymna = NewDeck("Tymna", dave, second: "Thrasios");
        var zur = NewDeck("Zur", dave);
        db.Decks.AddRange(atraxa, meren, korvold, tymna, zur);
        await db.SaveChangesAsync();
        _atraxaId = atraxa.Id;
        _unusedId = zur.Id;

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
        AddGame(bob, (alice, atraxa), (bob, meren));
        AddGame(carol, (alice, atraxa), (bob, meren), (carol, korvold));
        AddGame(null, (alice, atraxa), (bob, meren));
        AddGame(alice, (alice, atraxa), (carol, korvold));
        AddGame(alice, (alice, atraxa), (dave, tymna));
        AddGame(dave, (bob, meren), (dave, zur));
        AddGame(alice, (alice, atraxa), (bob, null));
        await db.SaveChangesAsync();
    }

    private async Task<(IActionResult Result, DeckMatchupsModel Model)> LoadAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new DeckMatchupsModel(db);
        var result = await model.OnGetAsync(id);
        return (result, model);
    }

    [Fact]
    public async Task OnGet_CountsGamesWinsAndLossesAgainstEachOpponentDeck()
    {
        await SeedAsync();

        var (result, model) = await LoadAsync(_atraxaId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(3, model.Matchups.Count);
        var meren = model.Matchups.Single(m => m.Commander == "Meren");
        Assert.Equal((4, 1, 1), (meren.Games, meren.Wins, meren.Losses));
        var korvold = model.Matchups.Single(m => m.Commander == "Korvold");
        Assert.Equal((2, 1, 1), (korvold.Games, korvold.Wins, korvold.Losses));
        var tymna = model.Matchups.Single(m => m.Commander == "Tymna");
        Assert.Equal((1, 1, 0), (tymna.Games, tymna.Wins, tymna.Losses));
    }

    [Fact]
    public async Task OnGet_BreaksRecordsDownByGameSize()
    {
        await SeedAsync();

        var (_, model) = await LoadAsync(_atraxaId);

        var meren = model.Matchups.Single(m => m.Commander == "Meren");
        Assert.Equal(new MatchupRecord(3, 1, 1), meren.BySize[2]);
        Assert.Equal(new MatchupRecord(1, 0, 0), meren.BySize[3]);
        Assert.Equal(new MatchupRecord(0, 0, 0), meren.BySize[4]);
        var korvold = model.Matchups.Single(m => m.Commander == "Korvold");
        Assert.Equal(new MatchupRecord(1, 1, 0), korvold.BySize[2]);
        Assert.Equal(new MatchupRecord(1, 0, 1), korvold.BySize[3]);
    }

    [Fact]
    public async Task OnGet_ExcludesDecksThatNeverPlayedTheSelectedDeckAndTheDeckItself()
    {
        await SeedAsync();

        var (_, model) = await LoadAsync(_atraxaId);

        Assert.DoesNotContain(model.Matchups, m => m.Commander == "Zur");
        Assert.DoesNotContain(model.Matchups, m => m.DeckId == _atraxaId);
    }

    [Fact]
    public async Task OnGet_ExposesSelectedDeckAndDisplayLabels()
    {
        await SeedAsync();

        var (_, model) = await LoadAsync(_atraxaId);

        Assert.Equal("Atraxa", model.Selected!.Label);
        Assert.Equal("Atraxa", model.Selected.Commander);
        Assert.Null(model.Selected.SecondCommander);
        Assert.Equal("Meren (Graveyard)", model.Matchups.Single(m => m.Commander == "Meren").Label);
        var tymna = model.Matchups.Single(m => m.Commander == "Tymna");
        Assert.Equal("Thrasios", tymna.SecondCommander);
        Assert.Equal("Tymna / Thrasios", tymna.Label);
    }

    [Fact]
    public async Task OnGet_UnknownDeckIsNotFound()
    {
        await SeedAsync();

        var (result, _) = await LoadAsync(99999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnGet_ListsOnlyDecksActuallyPlayedAgainst()
    {
        await SeedAsync();

        var (result, model) = await LoadAsync(_unusedId);

        Assert.IsType<PageResult>(result);
        var only = Assert.Single(model.Matchups);
        Assert.Equal("Meren (Graveyard)", only.Label);
        Assert.Equal((1, 1, 0), (only.Games, only.Wins, only.Losses));
    }

    [Fact]
    public async Task Page_RendersSliderDefaultingToFiveAndData()
    {
        await SeedAsync();

        var response = await _factory.CreateClient().GetAsync($"/DeckMatchups?id={_atraxaId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var slider = Regex.Match(html, "<input[^>]*type=\"range\"[^>]*>").Value;
        Assert.Contains("min=\"1\"", slider);
        Assert.Contains("max=\"40\"", slider);
        Assert.Contains("value=\"5\"", slider);
        Assert.Contains("id=\"deck-matchups-data\"", html);
        Assert.Contains("/js/deck-matchups.js", html);
        Assert.Contains("id=\"matchup-chart\" data-deck-url=\"/DeckMatchups\"", html);
        Assert.Contains("Atraxa", html);
    }

    [Fact]
    public async Task Page_RendersPlayerCountFilterWithoutBackButton()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/DeckMatchups?id={_atraxaId}")).Content.ReadAsStringAsync();

        foreach (var option in new[] { "all", "2", "3", "4" })
        {
            Assert.Contains($"data-players=\"{option}\"", html);
        }
        Assert.Contains("data-players=\"all\" aria-pressed=\"true\"", html);
        Assert.DoesNotContain("Back to decks", html);
    }

    [Fact]
    public async Task Page_UnknownDeckReturns404()
    {
        await SeedAsync();

        var response = await _factory.CreateClient().GetAsync("/DeckMatchups?id=99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DecksPage_LinksEachDeckToItsMatchupsPage()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/Decks")).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"/DeckMatchups?id={_atraxaId}\"", html);
    }
}
