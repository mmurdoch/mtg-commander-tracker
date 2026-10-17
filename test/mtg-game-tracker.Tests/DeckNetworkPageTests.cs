using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DeckNetworkPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Dictionary<string, int> _ids = new();

    public DeckNetworkPageTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DeckNetworkPageTests-{Guid.NewGuid()}";

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

    // Atraxa-Meren: 3 games. Atraxa-Korvold: 2 games. Meren-Korvold: 1 game (the 3-way). Zur plays only a deckless seat, Tymna never plays.
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var colorId = ColorIdentitySeeder.All.First().Id;

        var alice = new Person { Name = "Alice" };
        var bob = new Person { Name = "Bob" };
        var carol = new Person { Name = "Carol" };
        db.People.AddRange(alice, bob, carol);
        await db.SaveChangesAsync();

        Deck NewDeck(string commander, Person owner, string? name = null) =>
            new() { Commander = commander, Name = name, OwnerId = owner.Id, ColorDataId = colorId };
        var atraxa = NewDeck("Atraxa", alice);
        var meren = NewDeck("Meren", bob, "Graveyard");
        var korvold = NewDeck("Korvold", carol);
        var zur = NewDeck("Zur", carol);
        var tymna = NewDeck("Tymna", alice);
        db.Decks.AddRange(atraxa, meren, korvold, zur, tymna);
        await db.SaveChangesAsync();
        _ids["Atraxa"] = atraxa.Id;
        _ids["Meren"] = meren.Id;
        _ids["Korvold"] = korvold.Id;
        _ids["Zur"] = zur.Id;
        _ids["Tymna"] = tymna.Id;

        void AddGame(params (Person Player, Deck? Deck)[] seats)
        {
            var game = new Game { Date = new DateTime(2024, 1, 1), WinnerId = seats[0].Player.Id };
            foreach (var (player, deck) in seats)
            {
                game.Participants.Add(new Participant { PlayerId = player.Id, DeckId = deck?.Id });
            }
            db.Games.Add(game);
        }

        AddGame((alice, atraxa), (bob, meren));
        AddGame((alice, atraxa), (bob, meren));
        AddGame((alice, atraxa), (bob, meren), (carol, korvold));
        AddGame((alice, atraxa), (carol, korvold));
        AddGame((carol, zur), (bob, null));
        await db.SaveChangesAsync();
    }

    private async Task<DeckNetworkModel> LoadAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new DeckNetworkModel(db);
        await model.OnGetAsync();
        return model;
    }

    private NetworkEdge Edge(DeckNetworkModel model, string a, string b) =>
        model.Edges.Single(e => new[] { e.Source, e.Target }.Order().SequenceEqual(new[] { _ids[a], _ids[b] }.Order()));

    [Fact]
    public async Task OnGet_CountsGamesBetweenEachPairOfDecks()
    {
        await SeedAsync();

        var model = await LoadAsync();

        Assert.Equal(3, model.Edges.Count);
        Assert.Equal(3, Edge(model, "Atraxa", "Meren").Games);
        Assert.Equal(2, Edge(model, "Atraxa", "Korvold").Games);
        Assert.Equal(1, Edge(model, "Meren", "Korvold").Games);
    }

    [Fact]
    public async Task OnGet_EachPairAppearsOnce()
    {
        await SeedAsync();

        var model = await LoadAsync();

        Assert.Equal(model.Edges.Count, model.Edges.Select(e => (Math.Min(e.Source, e.Target), Math.Max(e.Source, e.Target))).Distinct().Count());
        Assert.All(model.Edges, e => Assert.NotEqual(e.Source, e.Target));
    }

    [Fact]
    public async Task OnGet_NodesAreOnlyDecksWithAnOpponentDeck()
    {
        await SeedAsync();

        var model = await LoadAsync();

        Assert.Equal(new[] { "Atraxa", "Korvold", "Meren (Graveyard)" }, model.Nodes.Select(n => n.Label).Order());
        Assert.Equal(model.Nodes.Select(n => n.DeckId).Order(), model.Edges.SelectMany(e => new[] { e.Source, e.Target }).Distinct().Order());
    }

    [Fact]
    public async Task OnGet_NodeGamesIsTheNumberOfGamesTheDeckPlayedAgainstOtherDecks()
    {
        await SeedAsync();

        var model = await LoadAsync();

        Assert.Equal(4, model.Nodes.Single(n => n.DeckId == _ids["Atraxa"]).Games);
        Assert.Equal(3, model.Nodes.Single(n => n.DeckId == _ids["Meren"]).Games);
        Assert.Equal(2, model.Nodes.Single(n => n.DeckId == _ids["Korvold"]).Games);
    }

    [Fact]
    public async Task OnGet_NoGamesMeansNoNodesOrEdges()
    {
        var model = await LoadAsync();

        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
    }

    [Fact]
    public async Task Page_RendersContainerDataAndScript()
    {
        await SeedAsync();

        var response = await _factory.CreateClient().GetAsync("/DeckNetwork");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"deck-network\" data-deck-url=\"/DeckMatchups\"", html);
        Assert.Contains("id=\"deck-network-data\"", html);
        Assert.Contains("id=\"minDeckGames\"", html);
        Assert.Contains("id=\"network-filter\"", html);
        Assert.Contains("/js/deck-network.js", html);
        Assert.Matches(new Regex("\"games\":3"), html);
    }

    [Fact]
    public async Task Page_FocusesTheDeckNamedInTheQuery()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/DeckNetwork?deck={_ids["Atraxa"]}")).Content.ReadAsStringAsync();

        Assert.Contains($"data-deck-url=\"/DeckMatchups\" data-focus-deck=\"{_ids["Atraxa"]}\"", html);
    }

    [Fact]
    public async Task Page_HasNoFocusWithoutADeckInTheQuery()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/DeckNetwork")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("data-focus-deck", html);
    }

    [Fact]
    public async Task DecksPage_LinksToTheNetwork()
    {
        var response = await _factory.CreateClient().GetAsync("/Decks");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"/DeckNetwork\"", html);
    }
}
