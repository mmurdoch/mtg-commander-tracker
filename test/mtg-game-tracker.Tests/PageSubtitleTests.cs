using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using mtg_game_tracker.Data;
using mtg_game_tracker.Services;
using Xunit;

namespace mtg_game_tracker.Tests;

public class PageSubtitleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PageSubtitleTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"PageSubtitleTests-{Guid.NewGuid()}";

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

                services.RemoveAll<ICommanderCatalog>();
                services.AddSingleton<ICommanderCatalog>(new FakeCommanderCatalog("Meren of Clan Nel Toth"));
            });
        });
    }

    [Theory]
    [InlineData("/Decks", "Manage decks and their color identity.")]
    [InlineData("/People", "Manage players.")]
    [InlineData("/Games", "All saved games, newest first.")]
    [InlineData("/AddDeck", "Add a new deck.")]
    [InlineData("/AddGame", "Enter the game details quickly from your phone.")]
    [InlineData("/AddPerson", "Add a new player name to the tracker.")]
    public async Task Page_DoesNotRenderSubtitle(string url, string subtitle)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(subtitle, html);
    }
}
