using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class AddGameNewDeckTests
{
    private static FakeCommanderCatalog Catalog() =>
        new("Niv-Mizzet", "Tymna the Weaver", "Thrasios, Triton Hero")
        {
            ColorIdentities =
            {
                ["Niv-Mizzet"] = "UR",
                ["Tymna the Weaver"] = "WB",
                ["Thrasios, Triton Hero"] = "GU",
            }
        };

    private static async Task<(AppDbContext Context, Person Alice, Person Bob)> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var alice = new Person { Name = "Alice" };
        var bob = new Person { Name = "Bob" };
        context.People.AddRange(alice, bob);
        await context.SaveChangesAsync();
        return (context, alice, bob);
    }

    [Fact]
    public async Task AddDeckHandler_PersistsDeckWithColorIdentityAndReturnsItsOption()
    {
        var (context, alice, _) = await CreateContextAsync();
        await using var _ = context;
        var model = new AddGameModel(context, Catalog());

        var result = await model.OnPostAddDeckAsync("Arcane", "Niv-Mizzet", null, alice.Id);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Null(json.StatusCode);
        var response = Assert.IsType<AddDeckResponse>(json.Value);
        var saved = await context.Decks.SingleAsync();
        Assert.Equal(ColorIdentitySeeder.All.Single(c => c.Name == "Izzet").Id, saved.ColorDataId);
        Assert.Equal(saved.Id, response.DeckId);
        var option = Assert.Single(response.Decks);
        Assert.Equal(new DeckOption(saved.Id, "Niv-Mizzet (Arcane)", alice.Id), option);
    }

    [Fact]
    public async Task AddDeckHandler_ReturnsAllDecksSortedByLabelWithOwnerSuffixWhenCommanderIsShared()
    {
        var (context, alice, bob) = await CreateContextAsync();
        await using var _ = context;
        var izzet = ColorIdentitySeeder.All.Single(c => c.Name == "Izzet");
        context.Decks.Add(new Deck { Commander = "Niv-Mizzet", OwnerId = alice.Id, ColorDataId = izzet.Id });
        await context.SaveChangesAsync();
        var model = new AddGameModel(context, Catalog());

        var response = (AddDeckResponse)((JsonResult)await model.OnPostAddDeckAsync(null, "Niv-Mizzet", null, bob.Id)).Value!;

        Assert.Equal(new[] { "Niv-Mizzet - Alice", "Niv-Mizzet - Bob" }, response.Decks.Select(d => d.Label));
        Assert.Equal(bob.Id, response.Decks.Single(d => d.Id == response.DeckId).OwnerId);
    }

    [Fact]
    public async Task AddDeckHandler_SecondCommanderCombinesColorIdentities()
    {
        var (context, alice, _) = await CreateContextAsync();
        await using var _ = context;
        var model = new AddGameModel(context, Catalog());

        await model.OnPostAddDeckAsync(null, "Tymna the Weaver", "Thrasios, Triton Hero", alice.Id);

        var saved = await context.Decks.SingleAsync();
        Assert.Equal("Thrasios, Triton Hero", saved.SecondCommander);
        Assert.Equal(ColorIdentitySeeder.All.Single(c => c.Name == "Witch-Maw").Id, saved.ColorDataId);
    }

    [Theory]
    [InlineData(null, "Alice", "Commander")]
    [InlineData("  ", "Alice", "Commander")]
    [InlineData("Niv-Mizzet", null, "OwnerId")]
    [InlineData("Unknown Commander", "Alice", "Commander")]
    public async Task AddDeckHandler_InvalidInputReturns400WithKeyedErrorAndDoesNotPersist(
        string? commander, string? ownerName, string errorKey)
    {
        var (context, alice, _) = await CreateContextAsync();
        await using var _ = context;
        var model = new AddGameModel(context, Catalog());
        int? ownerId = ownerName is null ? null : alice.Id;

        var result = await model.OnPostAddDeckAsync(null, commander, null, ownerId);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(400, json.StatusCode);
        var response = Assert.IsType<AddDeckErrors>(json.Value);
        Assert.True(response.Errors.ContainsKey(errorKey));
        Assert.False(await context.Decks.AnyAsync());
    }

    [Fact]
    public async Task CommandersHandler_ReturnsEachCommanderWithItsColorLetters()
    {
        var (context, _, _) = await CreateContextAsync();
        await using var _ = context;
        var model = new AddGameModel(context, Catalog());

        var result = await model.OnGetCommandersAsync();

        var commanders = Assert.IsAssignableFrom<IReadOnlyList<CommanderOption>>(Assert.IsType<JsonResult>(result).Value);
        Assert.Contains(new CommanderOption("Niv-Mizzet", "UR"), commanders);
        Assert.Equal(3, commanders.Count);
    }
}

public class AddGameNewDeckPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AddGameNewDeckPageTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"AddGameNewDeckPageTests-{Guid.NewGuid()}";

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

    private async Task<string> GetHtmlAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/AddGame");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task AddGamePage_RendersNewDeckButtonInEveryParticipantRow()
    {
        var html = await GetHtmlAsync();

        Assert.Equal(10, Regex.Matches(html, "<button[^>]*class=\"[^\"]*new-deck-btn").Count);
    }

    [Fact]
    public async Task AddGamePage_NewDeckButtonSitsBesideTheDeckLabelInTheSameFlexRow()
    {
        var html = await GetHtmlAsync();

        Assert.Matches(
            "<div class=\"d-flex[^\"]*justify-content-between[^\"]*\">\\s*<label[^>]*for=\"Deck0Filter\">Deck</label>\\s*<button[^>]*new-deck-btn[^>]*>[^<]*</button>\\s*</div>\\s*<div data-combo",
            html);
    }

    [Fact]
    public async Task AddGamePage_RendersNewDeckModalOutsideTheGameFormWithItsOwnAntiforgeryToken()
    {
        var html = await GetHtmlAsync();

        var modal = html.IndexOf("id=\"newDeckModal\"", StringComparison.Ordinal);
        Assert.True(modal > html.IndexOf("</form>", StringComparison.Ordinal), "Modal must follow the game form.");
        var modalHtml = html[modal..];
        Assert.Matches("<form[^>]*id=\"newDeckForm\"[^>]*method=\"post\"", modalHtml);
        Assert.Contains("name=\"__RequestVerificationToken\"", modalHtml);
        Assert.Matches("<form[^>]*id=\"newDeckForm\"[^>]*action=\"[^\"]*handler=AddDeck\"", modalHtml);
        Assert.Contains("<script src=\"/js/add-deck-modal.js", html);
    }

    [Fact]
    public async Task AddGamePage_ModalCombosAreNotPickedUpByTheRowComboInitialiser()
    {
        var html = await GetHtmlAsync();

        Assert.Equal(20, Regex.Matches(html, "<div[^>]*data-combo").Count);
    }
}
