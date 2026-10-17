using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DeckPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private int _atraxaId;
    private int _zurId;

    public DeckPageTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DeckPageTests-{Guid.NewGuid()}";

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

    // Atraxa (Alice): 2P 3 games/2 wins (seat 1: 2/2, seat 2: 1/0), 3P 1/1 (seat 3), 4P 1/0 (draw, seat 1).
    // Breya (Alice) plays one 2P game from seat 2 and wins it. Zur never plays.
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

        Deck NewDeck(string commander) => new() { Commander = commander, OwnerId = alice.Id, ColorDataId = colorId };
        var atraxa = NewDeck("Atraxa");
        var breya = NewDeck("Breya");
        var zur = NewDeck("Zur");
        db.Decks.AddRange(atraxa, breya, zur);
        await db.SaveChangesAsync();
        _atraxaId = atraxa.Id;
        _zurId = zur.Id;

        void AddGame(Person? winner, params (Person Player, Deck? Deck)[] seats)
        {
            var game = new Game { Date = new DateTime(2024, 1, 1), WinnerId = winner?.Id };
            foreach (var (player, deck) in seats)
            {
                game.Participants.Add(new Participant { PlayerId = player.Id, DeckId = deck?.Id });
            }
            db.Games.Add(game);
        }

        AddGame(alice, (alice, atraxa), (bob, null));
        AddGame(bob, (bob, null), (alice, atraxa));
        AddGame(alice, (alice, atraxa), (bob, null));
        AddGame(alice, (bob, null), (carol, null), (alice, atraxa));
        AddGame(null, (alice, atraxa), (bob, null), (carol, null), (dave, null));
        AddGame(alice, (bob, null), (alice, breya));
        await db.SaveChangesAsync();
    }

    private async Task<DeckModel> LoadAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var model = new DeckModel(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.IsType<PageResult>(await model.OnGetAsync(id));
        return model;
    }

    [Fact]
    public async Task OnGet_UnknownDeckIsNotFound()
    {
        await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new DeckModel(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.IsType<NotFoundResult>(await model.OnGetAsync(99999));
    }

    [Fact]
    public async Task OnGet_ExposesTheDeckAndItsDisplayLabel()
    {
        await SeedAsync();

        var model = await LoadAsync(_atraxaId);

        Assert.Equal(_atraxaId, model.Deck!.Id);
        Assert.Equal("Atraxa", model.Label);
    }

    [Fact]
    public async Task OnGet_SummarizesWinsByPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_atraxaId);

        Assert.Equal(new[] { "2-player", "3-player", "4-player" }, model.WinsByPlayerCount.Select(e => e.Label));
        Assert.Equal(new[] { (3, 2), (1, 1), (1, 0) }, model.WinsByPlayerCount.Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_SeatWinsAreListedInTurnOrderForEachPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_atraxaId);

        Assert.Equal(new[] { "First", "Second" }, model.Seats[2].Select(e => e.Label));
        Assert.Equal(new[] { (2, 2), (1, 0) }, model.Seats[2].Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (0, 0), (0, 0), (1, 1) }, model.Seats[3].Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (1, 0), (0, 0), (0, 0), (0, 0) }, model.Seats[4].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_OverallSeatWinsCoverEveryonesGamesOfThatPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_atraxaId);

        Assert.Equal(new[] { (4, 3), (4, 1) }, model.OverallSeats[2].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_DeckWithNoGamesHasEmptyCharts()
    {
        await SeedAsync();

        var model = await LoadAsync(_zurId);

        Assert.All(model.WinsByPlayerCount, e => Assert.Equal((0, 0), (e.Games, e.Wins)));
        Assert.All(model.Seats.Values.SelectMany(s => s), e => Assert.Equal(0, e.Games));
    }

    [Fact]
    public async Task OnGet_TimeframeLimitsEveryChartToRecentGames()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = db.People.Single(p => p.Name == "Alice");
            var bob = db.People.Single(p => p.Name == "Bob");
            var game = new Game { Date = new DateTime(2024, 3, 1), WinnerId = alice.Id };
            game.Participants.Add(new Participant { PlayerId = alice.Id, DeckId = _atraxaId });
            game.Participants.Add(new Participant { PlayerId = bob.Id });
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }
        using var requestScope = _factory.Services.CreateScope();
        var model = new DeckModel(requestScope.ServiceProvider.GetRequiredService<AppDbContext>())
        {
            Today = new DateTime(2024, 3, 15)
        };

        await model.OnGetAsync(_atraxaId, "1m");

        Assert.Equal("1m", model.SelectedTimeframe);
        Assert.Equal(new[] { (1, 1), (0, 0), (0, 0) }, model.WinsByPlayerCount.Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (1, 1), (1, 0) }, model.OverallSeats[2].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_UnknownTimeframeFallsBackToAllTime()
    {
        await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new DeckModel(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        await model.OnGetAsync(_atraxaId, "bogus");

        Assert.Equal("all", model.SelectedTimeframe);
        Assert.Equal(3, model.WinsByPlayerCount[0].Games);
    }

    [Fact]
    public async Task DeckPage_RendersWinAndSeatChartsButNotDecksPlayed()
    {
        await SeedAsync();

        var response = await _factory.CreateClient().GetAsync($"/Deck?id={_atraxaId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Atraxa", html);
        Assert.Contains("data-chart=\"win-percent\"", html);
        Assert.Contains("67% (2/3)", html);
        Assert.DoesNotContain("data-chart=\"deck-share\"", html);
        Assert.DoesNotContain("Decks played", html);
        foreach (var size in new[] { 2, 3, 4 })
        {
            Assert.Contains($"data-chart=\"seats\" data-players=\"{size}\"", html);
        }
    }

    [Fact]
    public async Task DeckPage_RendersTimeframeLinksWithSelectedOnePressed()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Deck?id={_atraxaId}&timeframe=1m")).Content.ReadAsStringAsync();

        Assert.Matches("aria-pressed=\"true\"[^>]*>Last month</a>", html);
        Assert.Matches("aria-pressed=\"false\"[^>]*>All time</a>", html);
        Assert.Contains("timeframe=6m", html);
    }

    [Fact]
    public async Task DeckPage_LinksToMatchupsAndNetworkForTheDeck()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Deck?id={_atraxaId}")).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"/DeckMatchups?id={_atraxaId}\"", html);
        Assert.Contains($"href=\"/DeckNetwork?deck={_atraxaId}\"", html);
    }

    [Fact]
    public async Task DeckPage_UnknownDeckReturnsNotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/Deck?id=99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DecksPage_LinksNameAndCommanderToTheDeckPage()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/Decks")).Content.ReadAsStringAsync();

        Assert.Contains($"<a href=\"/Deck?id={_atraxaId}\">Atraxa</a>", html);
        Assert.DoesNotContain("/DeckMatchups?id=", html);
    }
}
