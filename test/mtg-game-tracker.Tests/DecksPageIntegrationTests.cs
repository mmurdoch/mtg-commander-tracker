using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
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
                OwnerId = owner.Id,
                ColorDataId = golgari.Id
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Decks");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<th>Color Identity</th>", html);
        Assert.Contains("<td>Golgari (BG)</td>", html);
        Assert.DoesNotContain("??", html);
    }
}
