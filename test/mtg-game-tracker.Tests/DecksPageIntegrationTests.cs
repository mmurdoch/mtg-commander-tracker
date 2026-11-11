using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using System.Text.RegularExpressions;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DecksPageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DecksPageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"DecksPageIntegrationTests-{Guid.NewGuid()}";

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

    [Fact]
    public async Task DecksPage_RendersColorIdentityName()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var golgari = ColorIdentitySeeder.All.Single(c => c.Abbreviation == "BG");
            var owner = new Person { Name = "Alice" };
            db.People.Add(owner);
            await db.SaveChangesAsync();
            db.Decks.Add(new Deck
            {
                Commander = "Meren of Clan Nel Toth",
                DeckListUrl = "https://example.com/meren",
                OwnerId = owner.Id,
                ColorDataId = golgari.Id
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Decks");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(">Color Identity</button>", html);
        Assert.Contains(">Deck List</th>", html);
        Assert.Contains("<a href=\"https://example.com/meren\" target=\"_blank\" rel=\"noopener noreferrer\">View</a>", html);
        Assert.True(html.IndexOf(">Commander</button>", StringComparison.Ordinal) < html.IndexOf(">Deck List</th>", StringComparison.Ordinal));
        Assert.True(html.IndexOf(">Deck List</th>", StringComparison.Ordinal) < html.IndexOf(">Owner</button>", StringComparison.Ordinal));
        Assert.Contains("<td>Golgari (BG)</td>", html);
        Assert.DoesNotContain("??", html);
    }

    [Fact]
    public async Task DecksPage_DeckCanBeEditedWithoutChangingItsCommanders()
    {
        int deckId;
        int newOwnerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new Person { Name = "Alice" };
            var newOwner = new Person { Name = "Bob" };
            db.People.AddRange(owner, newOwner);
            await db.SaveChangesAsync();
            newOwnerId = newOwner.Id;

            var deck = new Deck
            {
                Name = "Old name",
                Commander = "Meren of Clan Nel Toth",
                SecondCommander = "Thrasios, Triton Hero",
                DeckListUrl = "https://example.com/old-deck",
                OwnerId = owner.Id,
                ColorDataId = ColorIdentitySeeder.All.Single(c => c.Abbreviation == "BG").Id
            };
            db.Decks.Add(deck);
            await db.SaveChangesAsync();
            deckId = deck.Id;
        }

        var client = _factory.CreateClient();
        var decksResponse = await client.GetAsync("/Decks");
        decksResponse.EnsureSuccessStatusCode();
        var decksHtml = await decksResponse.Content.ReadAsStringAsync();
        Assert.Contains($"href=\"/EditDeck/{deckId}\"", decksHtml);
        Assert.Contains(">Edit</a>", decksHtml);
        Assert.Contains("Deck List", decksHtml);

        var editResponse = await client.GetAsync($"/EditDeck/{deckId}");
        editResponse.EnsureSuccessStatusCode();
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Contains("value=\"https://example.com/old-deck\"", editHtml);
        var token = Regex.Match(editHtml, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(token.Success, "Antiforgery token was not rendered on the Edit Deck form.");

        var invalidPostResponse = await client.PostAsync($"/EditDeck/{deckId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Invalid update",
            ["OwnerId"] = newOwnerId.ToString(),
            ["DeckListUrl"] = "not a url",
            ["__RequestVerificationToken"] = token.Groups[1].Value
        }));
        invalidPostResponse.EnsureSuccessStatusCode();
        var invalidPostHtml = await invalidPostResponse.Content.ReadAsStringAsync();
        Assert.Contains("Please enter a valid URL.", invalidPostHtml);

        var postResponse = await client.PostAsync($"/EditDeck/{deckId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "New name",
            ["OwnerId"] = newOwnerId.ToString(),
            ["DeckListUrl"] = "https://example.com/new-deck",
            ["__RequestVerificationToken"] = token.Groups[1].Value
        }));
        postResponse.EnsureSuccessStatusCode();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedDeck = await verifyDb.Decks.SingleAsync(d => d.Id == deckId);
        Assert.Equal("New name", updatedDeck.Name);
        Assert.Equal(newOwnerId, updatedDeck.OwnerId);
        Assert.Equal("https://example.com/new-deck", updatedDeck.DeckListUrl);
        Assert.Equal("Meren of Clan Nel Toth", updatedDeck.Commander);
        Assert.Equal("Thrasios, Triton Hero", updatedDeck.SecondCommander);
    }
}
