using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using mtg_game_tracker.Services;
using Xunit;

namespace mtg_game_tracker.Tests;

public class HomeChartsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomeChartsTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"HomeChartsTests-{Guid.NewGuid()}";

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

    // Seven people (P1..P7) each play i 2-player games against Zed, who always loses; P1 also plays a 3-player and a 5-player game.
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var colorId = ColorIdentitySeeder.All.First().Id;

        var zed = new Person { Name = "Zed" };
        var players = Enumerable.Range(1, 7).Select(i => new Person { Name = $"P{i}" }).ToList();
        db.People.Add(zed);
        db.People.AddRange(players);
        await db.SaveChangesAsync();

        Deck NewDeck(string commander, Person owner, string? name = null) =>
            new() { Commander = commander, Name = name, OwnerId = owner.Id, ColorDataId = colorId };
        var atraxa = NewDeck("Atraxa", players[0]);
        var meren = NewDeck("Meren", players[1], "Graveyard");
        var unused = NewDeck("Zur", zed);
        db.Decks.AddRange(atraxa, meren, unused);
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

        for (var i = 1; i <= 7; i++)
        {
            var player = players[i - 1];
            var deck = i == 1 ? atraxa : i == 2 ? meren : null;
            for (var n = 0; n < i; n++)
            {
                AddGame(player, (player, deck), (zed, null));
            }
        }

        AddGame(players[0], (players[0], atraxa), (players[1], meren), (zed, null));
        AddGame(players[0], (players[0], atraxa), (players[1], meren), (zed, null), (players[2], null), (players[3], null));
        await db.SaveChangesAsync();
    }

    private async Task<WinCharts> LoadChartsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new AddGameModel(db, new FakeCommanderCatalog())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new EmptyTempDataProvider())
        };
        await model.OnGetAsync();
        return model.Charts;
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task Charts_PeopleShowOnlyTheSixWithMostGamesForEachPlayerCount()
    {
        await SeedAsync();

        var charts = await LoadChartsAsync();

        var twoPlayer = charts.People[2];
        Assert.Equal(new[] { "Zed", "P7", "P6", "P5", "P4", "P3" }, twoPlayer.Select(e => e.Label));
        Assert.Equal((28, 0), (twoPlayer[0].Games, twoPlayer[0].Wins));
        Assert.Equal((7, 7), (twoPlayer[1].Games, twoPlayer[1].Wins));
    }

    [Fact]
    public async Task Charts_PeopleExcludeThoseWithNoGamesOfThatSizeAndIgnoreLargeGames()
    {
        await SeedAsync();

        var charts = await LoadChartsAsync();

        var threePlayer = charts.People[3];
        Assert.Equal(new[] { "P1", "P2", "Zed" }, threePlayer.Select(e => e.Label).OrderBy(l => l));
        Assert.Equal((1, 1), threePlayer.Where(e => e.Label == "P1").Select(e => (e.Games, e.Wins)).Single());
        Assert.Empty(charts.People[4]);
    }

    [Fact]
    public async Task Charts_DecksIncludeEveryDeckWithGamesOfThatSizeUsingDisplayNames()
    {
        await SeedAsync();

        var charts = await LoadChartsAsync();

        var twoPlayer = charts.Decks[2];
        Assert.Equal(new[] { "Atraxa", "Meren (Graveyard)" }, twoPlayer.Select(e => e.Label));
        Assert.Equal((1, 1), (twoPlayer[0].Games, twoPlayer[0].Wins));
        Assert.Equal((2, 2), (twoPlayer[1].Games, twoPlayer[1].Wins));
        Assert.Equal(new[] { "Atraxa", "Meren (Graveyard)" }, charts.Decks[3].Select(e => e.Label));
        Assert.Empty(charts.Decks[4]);
    }

    [Fact]
    public async Task Charts_DeckEntriesCarryDeckIdAndHomePageExposesMatchupsUrl()
    {
        await SeedAsync();

        var charts = await LoadChartsAsync();
        int atraxaId;
        using (var scope = _factory.Services.CreateScope())
        {
            atraxaId = scope.ServiceProvider.GetRequiredService<AppDbContext>().Decks.Single(d => d.Commander == "Atraxa").Id;
        }
        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Equal(atraxaId, charts.Decks[2].Single(e => e.Label == "Atraxa").DeckId);
        Assert.Equal(3, Regex.Matches(html, "data-chart=\"decks\"[^>]*data-deck-url=\"/DeckMatchups\"").Count);
    }

    [Fact]
    public async Task HomePage_RendersSixChartsAndDeckSlidersWithBounds()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        foreach (var kind in new[] { "people", "decks" })
        {
            foreach (var size in new[] { 2, 3, 4 })
            {
                Assert.Contains($"data-chart=\"{kind}\" data-players=\"{size}\"", html);
            }
        }

        var sliders = Regex.Matches(html, "<input[^>]*type=\"range\"[^>]*>");
        Assert.Equal(3, sliders.Count);
        foreach (Match slider in sliders)
        {
            Assert.Contains("min=\"1\"", slider.Value);
            Assert.Contains("max=\"40\"", slider.Value);
            Assert.Contains("value=\"5\"", slider.Value);
        }

        Assert.Contains("id=\"home-charts-data\"", html);
        Assert.Contains("/js/home-charts.js", html);
    }

    [Fact]
    public async Task Charts_TimeframesFilterGamesBeforeRankingAndSummarizing()
    {
        var today = new DateTime(2025, 6, 15);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var colorId = ColorIdentitySeeder.All.First().Id;
            var ann = new Person { Name = "Ann" };
            var bob = new Person { Name = "Bob" };
            db.People.AddRange(ann, bob);
            await db.SaveChangesAsync();
            var deck = new Deck { Commander = "Atraxa", OwnerId = ann.Id, ColorDataId = colorId };
            db.Decks.Add(deck);
            await db.SaveChangesAsync();

            void AddGame(DateTime date, Person winner) =>
                db.Games.Add(new Game
                {
                    Date = date,
                    WinnerId = winner.Id,
                    Participants =
                    {
                        new Participant { PlayerId = ann.Id, DeckId = deck.Id },
                        new Participant { PlayerId = bob.Id },
                    }
                });

            AddGame(today.AddDays(-10), ann);
            AddGame(today.AddMonths(-1), bob);        // on the cutoff of "last month"
            AddGame(today.AddMonths(-2), bob);
            AddGame(today.AddMonths(-5), ann);
            AddGame(today.AddMonths(-11), ann);
            AddGame(today.AddYears(-3), bob);
            await db.SaveChangesAsync();
        }

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var labels = await db2.Decks.ToDictionaryAsync(d => d.Id, d => d.Commander);

        var byTimeframe = await WinCharts.LoadByTimeframeAsync(db2, labels, today);

        Assert.Equal(new[] { "all", "12m", "6m", "3m", "1m" }, byTimeframe.Keys);
        (int Games, int Wins) Ann(string key) =>
            byTimeframe[key].People[2].Where(e => e.Label == "Ann").Select(e => (e.Games, e.Wins)).Single();
        Assert.Equal((6, 3), Ann("all"));
        Assert.Equal((5, 3), Ann("12m"));
        Assert.Equal((4, 2), Ann("6m"));
        Assert.Equal((3, 1), Ann("3m"));
        Assert.Equal((2, 1), Ann("1m"));
        Assert.Equal((2, 1), byTimeframe["1m"].Decks[2].Select(e => (e.Games, e.Wins)).Single());
    }

    [Fact]
    public async Task Charts_TimeframeWithNoGamesIsEmpty()
    {
        await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var byTimeframe = await WinCharts.LoadByTimeframeAsync(db, new Dictionary<int, string>(), new DateTime(2025, 6, 15));

        Assert.NotEmpty(byTimeframe["all"].People[2]);
        Assert.All(WinCharts.GameSizes, size => Assert.Empty(byTimeframe["1m"].People[size]));
    }

    [Fact]
    public async Task HomePage_RendersTimeframeSelectorNextToTitleWithAllTimeSelected()
    {
        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"timeframe-select\"", html);
        var expected = new[]
        {
            ("all", "All time"), ("12m", "Last 12 months"), ("6m", "Last 6 months"),
            ("3m", "Last 3 months"), ("1m", "Last month"),
        };
        var buttons = Regex.Matches(html, "<button[^>]*data-timeframe=\"([^\"]+)\"[^>]*>\\s*([^<]+?)\\s*</button>");
        Assert.Equal(expected, buttons.Select(m => (m.Groups[1].Value, m.Groups[2].Value)));
        var selected = buttons.Where(m => m.Value.Contains("aria-pressed=\"true\"")).ToList();
        Assert.Equal("all", Assert.Single(selected).Groups[1].Value);
        Assert.True(html.IndexOf("Top win percentages", StringComparison.Ordinal) < html.IndexOf("id=\"timeframe-select\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HomePage_EmbedsChartDataForEveryTimeframe()
    {
        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        var json = Regex.Match(html, "<script type=\"application/json\" id=\"home-charts-data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var doc = System.Text.Json.JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(json));
        foreach (var key in new[] { "all", "12m", "6m", "3m", "1m" })
        {
            Assert.True(doc.RootElement.GetProperty(key).TryGetProperty("people", out _));
        }
    }

    [Fact]
    public async Task HomePage_ShowsChartsFourThreeThenTwoPlayerWithTitleAndNoSubtitles()
    {
        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("Top win percentages", html);
        Assert.DoesNotContain("Win percentages</h2>", html);
        Assert.DoesNotContain("six with the most games", html);
        Assert.DoesNotContain("top five by win percentage", html);

        foreach (var kind in new[] { "people", "decks" })
        {
            var positions = new[] { 4, 3, 2 }
                .Select(size => html.IndexOf($"data-chart=\"{kind}\" data-players=\"{size}\"", StringComparison.Ordinal))
                .ToList();
            Assert.All(positions, p => Assert.True(p >= 0));
            Assert.Equal(positions.OrderBy(p => p), positions);
        }
    }

    [Fact]
    public async Task Charts_SeatsCountWinsBySeatPositionForEachGameSize()
    {
        await SeedAsync();

        var charts = await LoadChartsAsync();

        (string, int, int)[] Seats(int size) => charts.Seats[size].Select(e => (e.Label, e.Games, e.Wins)).ToArray();
        Assert.Equal(new[] { ("First", 28, 28), ("Second", 28, 0) }, Seats(2));
        Assert.Equal(new[] { ("First", 1, 1), ("Second", 1, 0), ("Third", 1, 0) }, Seats(3));
        Assert.Empty(charts.Seats[4]);
    }

    [Fact]
    public async Task Charts_SeatsFollowTheOrderPlayersWereEnteredAndTreatDrawsAsNoWin()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ann = new Person { Name = "Ann" };
            var bob = new Person { Name = "Bob" };
            db.People.AddRange(ann, bob);
            await db.SaveChangesAsync();
            db.Games.Add(new Game { Date = new DateTime(2024, 1, 1), WinnerId = bob.Id, Participants = { new Participant { PlayerId = ann.Id }, new Participant { PlayerId = bob.Id } } });
            db.Games.Add(new Game { Date = new DateTime(2024, 1, 2), Participants = { new Participant { PlayerId = bob.Id }, new Participant { PlayerId = ann.Id } } });
            await db.SaveChangesAsync();
        }

        var charts = await LoadChartsAsync();

        Assert.Equal(new[] { ("First", 2, 0), ("Second", 2, 1) }, charts.Seats[2].Select(e => (e.Label, e.Games, e.Wins)));
    }

    [Fact]
    public async Task HomePage_RendersSeatChartsBelowDecksAndEmbedsSeatData()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();

        var positions = new[] { 4, 3, 2 }
            .Select(size => html.IndexOf($"data-chart=\"seats\" data-players=\"{size}\"", StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.True(html.IndexOf("data-chart=\"decks\" data-players=\"2\"", StringComparison.Ordinal) < positions[0]);
        Assert.Contains("Seat position", html);

        var json = Regex.Match(html, "<script type=\"application/json\" id=\"home-charts-data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var doc = System.Text.Json.JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(json));
        Assert.True(doc.RootElement.GetProperty("all").TryGetProperty("seats", out _));
    }
}
