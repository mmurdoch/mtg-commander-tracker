using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class PeoplePageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PeoplePageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"PeoplePageIntegrationTests-{Guid.NewGuid()}";

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

    // Alice 2P 2/3, Bob 2P 1/3; 3P: Carol won; 4P: draw; 5P game is ignored; Eve has no 2-4 player games.
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alice = new Person { Name = "Alice" };
        var bob = new Person { Name = "Bob" };
        var carol = new Person { Name = "Carol" };
        var dave = new Person { Name = "Dave" };
        var eve = new Person { Name = "Eve" };
        db.People.AddRange(alice, bob, carol, dave, eve);
        await db.SaveChangesAsync();

        void AddGame(Person? winner, params Person[] players)
        {
            var game = new Game { Date = new DateTime(2024, 1, 1), WinnerId = winner?.Id };
            foreach (var p in players)
            {
                game.Participants.Add(new Participant { PlayerId = p.Id });
            }
            db.Games.Add(game);
        }

        AddGame(alice, alice, bob);
        AddGame(bob, alice, bob);
        AddGame(alice, alice, bob);
        AddGame(carol, alice, bob, carol);
        AddGame(null, alice, bob, carol, dave);
        AddGame(eve, alice, bob, carol, dave, eve);
        await db.SaveChangesAsync();
    }

    private async Task<List<PersonWinStats>> LoadStatsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = new PeopleModel(db);
        await model.OnGetAsync();
        return model.People;
    }

    [Fact]
    public async Task OnGet_ComputesWinRecordsForTwoThreeAndFourPlayerGames()
    {
        await SeedAsync();

        var stats = await LoadStatsAsync();

        var alice = stats.Single(s => s.Person.Name == "Alice");
        Assert.Equal((3, 2), (alice.TwoPlayer.Games, alice.TwoPlayer.Wins));
        Assert.Equal((1, 0), (alice.ThreePlayer.Games, alice.ThreePlayer.Wins));
        Assert.Equal((1, 0), (alice.FourPlayer.Games, alice.FourPlayer.Wins));
        Assert.Equal(2.0 / 3 * 100, alice.TwoPlayer.Percent!.Value, 3);
        Assert.Equal(0, alice.ThreePlayer.Percent);

        var carol = stats.Single(s => s.Person.Name == "Carol");
        Assert.Null(carol.TwoPlayer.Percent);
        Assert.Equal(100, carol.ThreePlayer.Percent);
    }

    [Fact]
    public async Task OnGet_IgnoresGamesWithMoreThanFourPlayers()
    {
        await SeedAsync();

        var eve = (await LoadStatsAsync()).Single(s => s.Person.Name == "Eve");

        Assert.Equal(0, eve.TwoPlayer.Games + eve.ThreePlayer.Games + eve.FourPlayer.Games);
        Assert.Null(eve.FourPlayer.Percent);
    }

    [Fact]
    public async Task OnGet_OrdersPeopleByName()
    {
        await SeedAsync();

        var stats = await LoadStatsAsync();

        Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dave", "Eve" }, stats.Select(s => s.Person.Name));
    }

    [Fact]
    public async Task PeoplePage_RendersWinPercentagesAndSortableHeaders()
    {
        await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/People");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-sortable", html);
        Assert.Contains("2-Player Win %", html);
        Assert.Contains("3-Player Win %", html);
        Assert.Contains("4-Player Win %", html);
        Assert.Contains("<td data-sort-value=\"66.6667\">67% (2/3)</td>", html);
        Assert.Contains("<td data-sort-value=\"0\">0% (0/1)</td>", html);
        Assert.Contains("<td data-sort-value=\"100\">100% (1/1)</td>", html);
        Assert.Contains("<td data-sort-value=\"\">-</td>", html);
        Assert.Contains("table-sort.js", html);
    }
}
