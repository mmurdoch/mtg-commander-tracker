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

public class PersonPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private int _aliceId;
    private int _evaId;

    public PersonPageTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"PersonPageTests-{Guid.NewGuid()}";

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

    // Alice: 2P 8 games/2 wins (seat 1: 7/2, seat 2: 1/0), 3P 2/1 (seat 1: 1/0, seat 3: 1/1), 4P 1/0 (draw).
    // Her decks: Atraxa x3, Breya x2, Ceta..Gorm x1 each; the 4P game has no deck.
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var colorId = ColorIdentitySeeder.All.First().Id;

        var alice = new Person { Name = "Alice" };
        var bob = new Person { Name = "Bob" };
        var carol = new Person { Name = "Carol" };
        var dave = new Person { Name = "Dave" };
        var eva = new Person { Name = "Eva" };
        db.People.AddRange(alice, bob, carol, dave, eva);
        await db.SaveChangesAsync();

        var decks = new[] { "Atraxa", "Breya", "Ceta", "Dina", "Edgar", "Fynn", "Gorm" }
            .Select(c => new Deck { Commander = c, OwnerId = alice.Id, ColorDataId = colorId })
            .ToList();
        db.Decks.AddRange(decks);
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

        AddGame(alice, (alice, decks[0]), (bob, null));
        AddGame(bob, (bob, null), (alice, decks[0]));
        AddGame(alice, (alice, decks[0]), (bob, null));
        AddGame(alice, (bob, null), (carol, null), (alice, decks[1]));
        AddGame(bob, (alice, decks[1]), (bob, null), (carol, null));
        for (var i = 2; i < decks.Count; i++)
        {
            AddGame(bob, (alice, decks[i]), (bob, null));
        }
        AddGame(null, (alice, null), (bob, null), (carol, null), (dave, null));
        await db.SaveChangesAsync();

        _aliceId = alice.Id;
        _evaId = eva.Id;
    }

    private async Task<PersonModel> LoadAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new PersonModel(db);
        var result = await model.OnGetAsync(id);
        Assert.IsType<PageResult>(result);
        return model;
    }

    [Fact]
    public async Task OnGet_UnknownPersonIsNotFound()
    {
        await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new PersonModel(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.IsType<NotFoundResult>(await model.OnGetAsync(99999));
    }

    [Fact]
    public async Task OnGet_SummarizesWinsByPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_aliceId);

        Assert.Equal("Alice", model.Person!.Name);
        Assert.Equal(new[] { "2-player", "3-player", "4-player" }, model.WinsByPlayerCount.Select(e => e.Label));
        Assert.Equal(new[] { (8, 2), (2, 1), (1, 0) }, model.WinsByPlayerCount.Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_DeckSharesShowTopFiveAndOtherAsPercentOfGamesWithADeck()
    {
        await SeedAsync();

        var model = await LoadAsync(_aliceId);

        Assert.Equal(new[] { "Atraxa", "Breya", "Ceta", "Dina", "Edgar", "Other" }, model.DeckShares.Select(d => d.Label));
        Assert.Equal(new[] { 3, 2, 1, 1, 1, 2 }, model.DeckShares.Select(d => d.Games));
        Assert.Equal(new[] { 30.0, 20, 10, 10, 10, 20 }, model.DeckShares.Select(d => d.Percent));
    }

    [Fact]
    public async Task OnGet_DeckSharesOmitOtherWhenFiveOrFewerDecks()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var colorId = ColorIdentitySeeder.All.First().Id;
            var deck = new Deck { Commander = "Zur", OwnerId = _evaId, ColorDataId = colorId };
            db.Decks.Add(deck);
            await db.SaveChangesAsync();
            var game = new Game { Date = new DateTime(2024, 1, 1), WinnerId = _evaId };
            game.Participants.Add(new Participant { PlayerId = _evaId, DeckId = deck.Id });
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }

        var model = await LoadAsync(_evaId);

        var only = Assert.Single(model.DeckShares);
        Assert.Equal(("Zur", 1, 100.0), (only.Label, only.Games, only.Percent));
    }

    [Fact]
    public async Task OnGet_SeatWinsAreListedInTurnOrderForEachPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_aliceId);

        Assert.Equal(new[] { "First", "Second" }, model.Seats[2].Select(e => e.Label));
        Assert.Equal(new[] { (7, 2), (1, 0) }, model.Seats[2].Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (1, 0), (0, 0), (1, 1) }, model.Seats[3].Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (1, 0), (0, 0), (0, 0), (0, 0) }, model.Seats[4].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_OverallSeatWinsCoverEveryonesGamesOfThatPlayerCount()
    {
        await SeedAsync();

        var model = await LoadAsync(_aliceId);

        Assert.Equal(new[] { (8, 3), (8, 5) }, model.OverallSeats[2].Select(e => (e.Games, e.Wins)));
        Assert.Equal(new[] { (2, 0), (2, 1), (2, 1) }, model.OverallSeats[3].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task PersonPage_OverallSeatBarsSitInFrontOnlyWhenBelowThePersonsBar()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        string Chart(int size)
        {
            var start = html.IndexOf($"data-chart=\"seats\" data-players=\"{size}\"", StringComparison.Ordinal);
            var next = html.IndexOf("data-chart=", start + 1, StringComparison.Ordinal);
            return html[start..(next < 0 ? html.Length : next)];
        }
        int Count(string chart, string cls) => System.Text.RegularExpressions.Regex.Matches(chart, cls).Count;

        // 2P: overall (38%, 63%) beats Alice (29%, 0%) in both seats.
        Assert.Equal(2, Count(Chart(2), "chart-bar-overall-behind"));
        Assert.Equal(0, Count(Chart(2), "chart-bar-overall-front"));
        // 3P third seat: Alice 100% vs overall 50%.
        Assert.Equal(1, Count(Chart(3), "chart-bar-overall-front"));
        Assert.Equal(2, Count(Chart(3), "chart-bar-overall-behind"));
    }

    [Fact]
    public async Task OnGet_TimeframeLimitsEveryChartToRecentGames()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bob = db.People.Single(p => p.Name == "Bob");
            var atraxa = db.Decks.Single(d => d.Commander == "Atraxa");
            var game = new Game { Date = new DateTime(2024, 3, 1), WinnerId = _aliceId };
            game.Participants.Add(new Participant { PlayerId = _aliceId, DeckId = atraxa.Id });
            game.Participants.Add(new Participant { PlayerId = bob.Id });
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }
        using var requestScope = _factory.Services.CreateScope();
        var model = new PersonModel(requestScope.ServiceProvider.GetRequiredService<AppDbContext>())
        {
            Today = new DateTime(2024, 3, 15)
        };

        await model.OnGetAsync(_aliceId, "1m");

        Assert.Equal("1m", model.SelectedTimeframe);
        Assert.Equal(new[] { (1, 1), (0, 0), (0, 0) }, model.WinsByPlayerCount.Select(e => (e.Games, e.Wins)));
        var share = Assert.Single(model.DeckShares);
        Assert.Equal(("Atraxa", 100.0), (share.Label, share.Percent));
        Assert.Equal(new[] { (1, 1), (1, 0) }, model.OverallSeats[2].Select(e => (e.Games, e.Wins)));
    }

    [Fact]
    public async Task OnGet_UnknownTimeframeFallsBackToAllTime()
    {
        await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new PersonModel(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        await model.OnGetAsync(_aliceId, "bogus");

        Assert.Equal("all", model.SelectedTimeframe);
        Assert.Equal(8, model.WinsByPlayerCount[0].Games);
    }

    [Fact]
    public async Task PersonPage_RendersTimeframeLinksWithSelectedOnePressed()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}&timeframe=1m")).Content.ReadAsStringAsync();

        foreach (var label in new[] { "All time", "Last 12 months", "Last 6 months", "Last 3 months", "Last month" })
        {
            Assert.Contains($">{label}</a>", html);
        }
        Assert.Matches("aria-pressed=\"true\"[^>]*>Last month</a>", html);
        Assert.Matches("aria-pressed=\"false\"[^>]*>All time</a>", html);
        Assert.Contains("timeframe=6m", html);
    }

    [Fact]
    public async Task PersonPage_SeatBarsBothShowPersonAndOverallValuesInTheirTooltip()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var start = html.IndexOf("data-chart=\"seats\" data-players=\"2\"", StringComparison.Ordinal);
        var chart = html[start..];
        // First seat: Alice 2/7 vs overall 3/8, on both the overall and the person's bar.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            chart, "class=\"chart-bar [^\"]*\"[^>]*title=\"Alice: 29% \\(2/7\\)(\\s|&#xA;)*Overall: 38% \\(3/8\\)\"");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task PersonPage_RendersThreeChartSections()
    {
        await SeedAsync();

        var response = await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Alice", html);
        Assert.Contains("data-chart=\"win-percent\"", html);
        Assert.Contains("data-chart=\"deck-share\"", html);
        foreach (var size in new[] { 2, 3, 4 })
        {
            Assert.Contains($"data-chart=\"seats\" data-players=\"{size}\"", html);
        }
        Assert.Contains("Atraxa", html);
        Assert.Contains("30% (3/10)", html);
    }

    [Fact]
    public async Task PersonPage_ExpectedMarkersSpanOnlyTheirOwnRow()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var winChart = html[html.IndexOf("data-chart=\"win-percent\"", StringComparison.Ordinal)..html.IndexOf("data-chart=\"deck-share\"", StringComparison.Ordinal)];
        var markers = System.Text.RegularExpressions.Regex.Matches(winChart, "class=\"chart-expected\" style=\"[^\"]*\"");
        Assert.Equal(3, markers.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Contains($"grid-row: {i + 1} / {i + 2}", markers[i].Value);
        }
    }

    [Fact]
    public async Task PersonPage_UnknownPersonReturnsNotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/Person?id=99999");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PersonPage_DeckSharesLinkToTheDeckPagesExceptOther()
    {
        await SeedAsync();
        int atraxaId;
        using (var scope = _factory.Services.CreateScope())
        {
            atraxaId = scope.ServiceProvider.GetRequiredService<AppDbContext>().Decks.Single(d => d.Commander == "Atraxa").Id;
        }

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var chart = html[html.IndexOf("data-chart=\"deck-share\"", StringComparison.Ordinal)..html.IndexOf("Win % by seat position", StringComparison.Ordinal)];
        Assert.Contains($"<a class=\"chart-label\" style=\"grid-row: 1\" title=\"Atraxa\" href=\"/Deck?id={atraxaId}\">Atraxa</a>", chart);
        Assert.Contains("<span class=\"chart-label\" style=\"grid-row: 6\" title=\"Other\">Other</span>", chart);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(chart, "<a [^>]*href=\"/Deck\\?id=").Count);
    }

    private async Task AddAtraxaGamesInMarchAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bob = db.People.Single(p => p.Name == "Bob");
        var atraxa = db.Decks.Single(d => d.Commander == "Atraxa");
        foreach (var (player, other) in new[] { (_aliceId, bob.Id), (bob.Id, _aliceId) })
        {
            var game = new Game { Date = new DateTime(2024, 3, 1), WinnerId = player };
            game.Participants.Add(new Participant { PlayerId = player, DeckId = atraxa.Id });
            game.Participants.Add(new Participant { PlayerId = other });
            db.Games.Add(game);
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OnGet_DeckPlaysByMonthCountsEveryPlayOfTheOwnersDecksIncludingEmptyMonths()
    {
        await SeedAsync();
        await AddAtraxaGamesInMarchAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new PersonModel(scope.ServiceProvider.GetRequiredService<AppDbContext>())
        {
            Today = new DateTime(2024, 3, 15)
        };

        await model.OnGetAsync(_aliceId);

        Assert.Equal(new[] { new DateTime(2024, 1, 1), new DateTime(2024, 2, 1), new DateTime(2024, 3, 1) }, model.PlayMonths);
        Assert.Equal(new[] { "Atraxa", "Breya", "Ceta", "Dina", "Edgar", "Fynn", "Gorm" }, model.DeckPlays.Select(d => d.Label));
        Assert.Equal(new[] { 3, 0, 2 }, model.DeckPlays[0].Counts);
        Assert.Equal(new[] { 2, 0, 0 }, model.DeckPlays[1].Counts);
    }

    [Fact]
    public async Task OnGet_DeckPlaysByMonthFollowTheTimeframe()
    {
        await SeedAsync();
        await AddAtraxaGamesInMarchAsync();
        using var scope = _factory.Services.CreateScope();
        var model = new PersonModel(scope.ServiceProvider.GetRequiredService<AppDbContext>())
        {
            Today = new DateTime(2024, 3, 15)
        };

        await model.OnGetAsync(_aliceId, "1m");

        Assert.Equal(new[] { new DateTime(2024, 2, 1), new DateTime(2024, 3, 1) }, model.PlayMonths);
        var atraxa = Assert.Single(model.DeckPlays);
        Assert.Equal("Atraxa", atraxa.Label);
        Assert.Equal(new[] { 0, 2 }, atraxa.Counts);
    }

    [Fact]
    public async Task OnGet_DeckPlaysByMonthAreEmptyWhenTheOwnersDecksWereNeverPlayed()
    {
        await SeedAsync();

        var model = await LoadAsync(_evaId);

        Assert.Empty(model.PlayMonths);
        Assert.Empty(model.DeckPlays);
    }

    [Fact]
    public async Task PersonPage_RendersDeckPlaysLineChartAfterTheSeatCharts()
    {
        await SeedAsync();
        await AddAtraxaGamesInMarchAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var start = html.IndexOf("data-chart=\"deck-plays\"", StringComparison.Ordinal);
        Assert.True(start > html.IndexOf("data-chart=\"seats\" data-players=\"4\"", StringComparison.Ordinal));
        var chart = html[start..];
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(chart, "<polyline ").Count);
        Assert.Contains("Jan 2024", chart);
        Assert.Contains("Mar 2024", chart);
        Assert.Contains("Atraxa", chart);
    }

    [Fact]
    public async Task PersonPage_EachDeckPlaysLineHasAWideInvisibleHoverTargetInsideItsSeriesGroup()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var chart = html[html.IndexOf("data-chart=\"deck-plays\"", StringComparison.Ordinal)..];
        var groups = System.Text.RegularExpressions.Regex.Matches(chart, "<g class=\"line-chart-series\".*?</g>", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Equal(7, groups.Count);
        Assert.All(groups, g => Assert.Contains("<path class=\"line-chart-hit\"", g.Value));
        Assert.All(groups, g => Assert.Matches("<path class=\"line-chart-hit\" d=\"M[\\d.]+,[\\d.]+( L[\\d.]+,[\\d.]+)*\"", g.Value));
    }

    [Fact]
    public async Task PersonPage_LegendEntriesCarryTheDeckIdAndTheHighlightScriptIsLoaded()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_aliceId}")).Content.ReadAsStringAsync();

        var chart = html[html.IndexOf("data-chart=\"deck-plays\"", StringComparison.Ordinal)..];
        var legend = chart[chart.IndexOf("<ul class=\"line-chart-legend", StringComparison.Ordinal)..];
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(legend, "<li data-deck=\"\\d+\">").Count);
        Assert.Contains("/js/deck-plays-chart.js", html);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [Fact]
    public void Stylesheet_PaintsDimmedDeckPlaysLinesAndSwatchesTheSameGrey()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "wwwroot", "css", "site.css"));

        var line = System.Text.RegularExpressions.Regex.Match(css, @"\.line-chart-series\.line-chart-dimmed\s*\{[^}]*stroke:\s*(#[0-9a-f]+)");
        var swatch = System.Text.RegularExpressions.Regex.Match(css, @"\.line-chart-dimmed\s+\.line-chart-swatch\s*\{[^}]*background:\s*(#[0-9a-f]+)");
        Assert.True(line.Success);
        Assert.True(swatch.Success);
        Assert.Equal(line.Groups[1].Value, swatch.Groups[1].Value);
        Assert.DoesNotContain("grayscale", css);
    }

    [Fact]
    public void Stylesheet_DeckPlaysHoverTargetIsAtLeast24UnitsWide()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "wwwroot", "css", "site.css"));

        var width = System.Text.RegularExpressions.Regex.Match(css, @"\.line-chart-hit\s*\{[^}]*stroke-width:\s*(\d+)");
        Assert.True(width.Success);
        Assert.True(int.Parse(width.Groups[1].Value) >= 24);
    }

    [Fact]
    public void Stylesheet_DimmedDeckPlaysElementsAreOpaqueSoOverlapsStayTheSameGrey()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "wwwroot", "css", "site.css"));

        Assert.DoesNotMatch(@"line-chart-dimmed[^{}]*\{[^}]*opacity", css);
        Assert.DoesNotMatch(@"line-chart-series[^{}]*\{[^}]*opacity", css);
    }

    [Fact]
    public async Task PersonPage_DeckPlaysChartShowsMessageWithoutPlays()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync($"/Person?id={_evaId}")).Content.ReadAsStringAsync();

        var chart = html[html.IndexOf("data-chart=\"deck-plays\"", StringComparison.Ordinal)..];
        Assert.Contains("No deck plays yet.", chart);
        Assert.DoesNotContain("<polyline", chart);
    }

    [Fact]
    public async Task PeoplePage_LinksEachNameToThePersonPage()
    {
        await SeedAsync();

        var html = await (await _factory.CreateClient().GetAsync("/People")).Content.ReadAsStringAsync();

        Assert.Contains($"<a href=\"/Person?id={_aliceId}\">Alice</a>", html);
    }
}
