using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DeckDisplayNameTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeckDisplayNameTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DeckDisplayNameTests-{Guid.NewGuid()}";

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

    private static Deck MakeDeck(int id, string commander, int ownerId, string ownerName, string? name = null, string? second = null) =>
        new()
        {
            Id = id,
            Commander = commander,
            SecondCommander = second,
            Name = name,
            OwnerId = ownerId,
            Owner = new Person { Id = ownerId, Name = ownerName }
        };

    [Fact]
    public void BuildDisplayNames_SameCommanderDifferentOwners_AppendsOwner()
    {
        var decks = new[]
        {
            MakeDeck(1, "Atla Palani", 1, "David"),
            MakeDeck(2, "Atla Palani", 2, "Rudi"),
        };

        var names = Deck.BuildDisplayNames(decks);

        Assert.Equal("Atla Palani - David", names[1]);
        Assert.Equal("Atla Palani - Rudi", names[2]);
    }

    [Fact]
    public void BuildDisplayNames_NamedDeck_PutsOwnerAfterName()
    {
        var decks = new[]
        {
            MakeDeck(1, "Atla Palani", 1, "David", name: "Eggs"),
            MakeDeck(2, "Atla Palani", 2, "Rudi"),
        };

        var names = Deck.BuildDisplayNames(decks);

        Assert.Equal("Atla Palani (Eggs) - David", names[1]);
        Assert.Equal("Atla Palani - Rudi", names[2]);
    }

    [Fact]
    public void BuildDisplayNames_UniqueCommander_OmitsOwner()
    {
        var decks = new[]
        {
            MakeDeck(1, "Atla Palani", 1, "David"),
            MakeDeck(2, "Meren of Clan Nel Toth", 2, "Rudi"),
        };

        var names = Deck.BuildDisplayNames(decks);

        Assert.Equal("Atla Palani", names[1]);
        Assert.Equal("Meren of Clan Nel Toth", names[2]);
    }

    [Fact]
    public void BuildDisplayNames_SameCommanderSameOwner_OmitsOwner()
    {
        var decks = new[]
        {
            MakeDeck(1, "Atla Palani", 1, "David", name: "Eggs"),
            MakeDeck(2, "Atla Palani", 1, "David", name: "Tokens"),
        };

        var names = Deck.BuildDisplayNames(decks);

        Assert.Equal("Atla Palani (Eggs)", names[1]);
        Assert.Equal("Atla Palani (Tokens)", names[2]);
    }

    [Fact]
    public void BuildDisplayNames_PartnerPairs_AreComparedAsAPair()
    {
        var decks = new[]
        {
            MakeDeck(1, "Tymna the Weaver", 1, "David", second: "Thrasios, Triton Hero"),
            MakeDeck(2, "Tymna the Weaver", 2, "Rudi", second: "Kraum, Ludevic's Opus"),
        };

        var names = Deck.BuildDisplayNames(decks);

        Assert.Equal("Tymna the Weaver / Thrasios, Triton Hero", names[1]);
        Assert.Equal("Tymna the Weaver / Kraum, Ludevic's Opus", names[2]);
    }

    [Fact]
    public async Task AddGamePage_DeckList_ShowsOwnerForSharedCommander()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var colors = ColorIdentitySeeder.All.First();
            var david = new Person { Name = "David" };
            var rudi = new Person { Name = "Rudi" };
            db.People.AddRange(david, rudi);
            await db.SaveChangesAsync();
            db.Decks.AddRange(
                new Deck { Commander = "Atla Palani", OwnerId = david.Id, ColorDataId = colors.Id },
                new Deck { Commander = "Atla Palani", OwnerId = rudi.Id, ColorDataId = colors.Id });
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateClient().GetAsync("/AddGame");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(">Atla Palani - David</li>", html);
        Assert.Contains(">Atla Palani - Rudi</li>", html);
    }
}
