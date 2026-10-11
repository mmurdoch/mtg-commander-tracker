using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using Xunit;

namespace mtg_game_tracker.Tests;

public class GamesPageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GamesPageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"GamesPageIntegrationTests-{Guid.NewGuid()}";

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

        var aliceDeck = new Deck { Commander = "Atraxa", Name = "Poison Pals", OwnerId = alice.Id, ColorDataId = colorId };
        var bobDeck = new Deck { Commander = "Meren of Clan Nel Toth", OwnerId = bob.Id, ColorDataId = colorId };
        var carolDeck = new Deck { Commander = "Korvold", OwnerId = carol.Id, ColorDataId = colorId };
        var daveDeck = new Deck { Commander = "Krenko", SecondCommander = "Tana", Name = "Goblin Party", OwnerId = dave.Id, ColorDataId = colorId };
        db.Decks.AddRange(aliceDeck, bobDeck, carolDeck, daveDeck);
        await db.SaveChangesAsync();

        db.Games.Add(new Game
        {
            Date = new DateTime(2024, 1, 1),
            WinnerId = alice.Id,
            Participants =
            {
                new Participant { PlayerId = alice.Id, DeckId = aliceDeck.Id },
                new Participant { PlayerId = bob.Id, DeckId = bobDeck.Id },
            }
        });
        db.Games.Add(new Game
        {
            Date = new DateTime(2024, 3, 1),
            Participants =
            {
                new Participant { PlayerId = alice.Id, DeckId = aliceDeck.Id },
                new Participant { PlayerId = bob.Id, DeckId = bobDeck.Id },
                new Participant { PlayerId = carol.Id, DeckId = carolDeck.Id },
                new Participant { PlayerId = dave.Id, DeckId = daveDeck.Id },
            }
        });
        for (var i = 0; i < 12; i++)
        {
            db.Games.Add(new Game
            {
                Date = new DateTime(2023, 1, 1).AddDays(i),
                WinnerId = bob.Id,
                Participants =
                {
                    new Participant { PlayerId = alice.Id, DeckId = aliceDeck.Id },
                    new Participant { PlayerId = bob.Id, DeckId = bobDeck.Id },
                }
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GamesPage_ListsAllGamesNewestFirst()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Games");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(14, CountOccurrences(html, "class=\"game-card"));
        Assert.True(html.IndexOf("2024-03-01", StringComparison.Ordinal) < html.IndexOf("2024-01-01", StringComparison.Ordinal));
        Assert.True(html.IndexOf("2024-01-01", StringComparison.Ordinal) < html.IndexOf("2023-01-12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GamesPage_ShowsDecksAndPeopleAndHighlightsWinner()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var html = await (await client.GetAsync("/Games")).Content.ReadAsStringAsync();

        Assert.Contains("Atraxa (Poison Pals)", html);
        Assert.Contains("Meren of Clan Nel Toth <span", html);
        Assert.Contains("Korvold <span", html);
        Assert.Contains("Krenko / Tana (Goblin Party)", html);
        Assert.Contains("Alice", html);
        Assert.Contains("Dave", html);

        var jan2024 = GameCard(html, "2024-01-01");
        Assert.Equal(1, CountOccurrences(jan2024, "game-winner"));
        Assert.Contains("Winner", jan2024);
        Assert.DoesNotContain("Draw", jan2024);
        var winnerEntry = jan2024.Substring(jan2024.IndexOf("game-winner", StringComparison.Ordinal));
        Assert.True(winnerEntry.IndexOf("Alice", StringComparison.Ordinal) < winnerEntry.IndexOf("Bob", StringComparison.Ordinal));
        Assert.DoesNotContain("Bob", winnerEntry.Substring(0, winnerEntry.IndexOf("Alice", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GamesPage_ShowsDrawWhenNoWinner()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var html = await (await client.GetAsync("/Games")).Content.ReadAsStringAsync();

        var mar2024 = GameCard(html, "2024-03-01");
        Assert.Contains("Draw", mar2024);
        Assert.DoesNotContain("game-winner", mar2024);
        Assert.DoesNotContain("Winner", mar2024);
    }

    [Fact]
    public async Task GamesPage_ShowsDeleteButtonWithConfirmationOnEachGame()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var html = await (await client.GetAsync("/Games")).Content.ReadAsStringAsync();

        Assert.Equal(14, CountOccurrences(html, "Are you sure you want to delete this game?"));
        Assert.Equal(14, CountOccurrences(html, "name=\"id\""));
        Assert.Equal(14, CountOccurrences(html, "btn-delete-game"));
    }

    [Fact]
    public async Task GamesPage_ShowsAddGameButton()
    {
        var client = _factory.CreateClient();

        var html = await (await client.GetAsync("/Games")).Content.ReadAsStringAsync();

        Assert.Contains("<a href=\"/AddGame\" class=\"btn btn-primary\">Add game</a>", html);
    }

    [Fact]
    public async Task Layout_DoesNotShowAddGameLinkInTopMenu()
    {
        var client = _factory.CreateClient();

        var html = await (await client.GetAsync("/People")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("nav-link\" href=\"/AddGame\"", html);
    }

    [Fact]
    public async Task DeleteGame_RemovesGameAndParticipants()
    {
        await SeedAsync();
        var client = _factory.CreateClient();
        int id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await db.Games.FirstAsync(g => g.Date == new DateTime(2024, 3, 1))).Id;
        }

        var page = await (await client.GetAsync("/Games")).Content.ReadAsStringAsync();
        var token = ExtractToken(page);
        var response = await client.PostAsync("/Games?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = id.ToString(),
            ["__RequestVerificationToken"] = token
        }));
        response.EnsureSuccessStatusCode();

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.Games.AnyAsync(g => g.Id == id));
        Assert.False(await verifyDb.Set<Participant>().AnyAsync(p => p.GameId == id));
        Assert.Equal(13, await verifyDb.Games.CountAsync());
        Assert.DoesNotContain("2024-03-01", await response.Content.ReadAsStringAsync());
    }

    private static string ExtractToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Antiforgery token not found");
        start += marker.Length;
        return html.Substring(start, html.IndexOf('"', start) - start);
    }

    private static string GameCard(string html, string date)
    {
        var start = html.IndexOf(date, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Game dated {date} not found");
        var next = html.IndexOf("class=\"game-card", start, StringComparison.Ordinal);
        return next < 0 ? html.Substring(start) : html.Substring(start, next - start);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
