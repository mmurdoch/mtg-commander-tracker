using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Models;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class AddPersonAndDeckTests
{
    private static FakeCommanderCatalog Catalog() =>
        new("Niv-Mizzet", "Tymna the Weaver", "Thrasios, Triton Hero", "Kozilek, the Great Distortion")
        {
            ColorIdentities =
            {
                ["Niv-Mizzet"] = "UR",
                ["Tymna the Weaver"] = "WB",
                ["Thrasios, Triton Hero"] = "GU",
                ["Kozilek, the Great Distortion"] = "",
            }
        };

    private static int ColorId(string name) => ColorIdentitySeeder.All.Single(c => c.Name == name).Id;

    private static async Task<(AppDbContext Context, Person Owner)> CreateContextWithOwnerAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var owner = new Person { Name = "Alice" };
        context.People.Add(owner);
        await context.SaveChangesAsync();
        return (context, owner);
    }

    [Fact]
    public async Task AddPersonPagePostsAndPersistsPerson()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var model = new AddPersonModel(context);

        var result = await model.OnPostAsync("Alice");

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.People.SingleAsync();
        Assert.Equal("Alice", saved.Name);
    }

    [Fact]
    public async Task AddDeckPagePostsAndPersistsDeckWithColorIdentityFromCommander()
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", owner.Id);

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.Decks.SingleAsync();
        Assert.Equal("Arcane", saved.Name);
        Assert.Equal("Niv-Mizzet", saved.Commander);
        Assert.Equal(owner.Id, saved.OwnerId);
        Assert.Equal(ColorId("Izzet"), saved.ColorDataId);
    }

    [Fact]
    public async Task AddDeckPageWithoutNamePersistsDeck()
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync(null, "Niv-Mizzet", owner.Id);

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.Decks.SingleAsync();
        Assert.Null(saved.Name);
        Assert.Equal("Niv-Mizzet", saved.Commander);
    }

    [Fact]
    public async Task AddDeckPageWithSecondCommanderPersistsItAndCombinesColorIdentities()
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Partners", "Tymna the Weaver", owner.Id, " Thrasios, Triton Hero ");

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.Decks.SingleAsync();
        Assert.Equal("Thrasios, Triton Hero", saved.SecondCommander);
        Assert.Equal(ColorId("Witch-Maw"), saved.ColorDataId);
    }

    [Fact]
    public async Task AddDeckPageWithColorlessCommanderPersistsColorlessIdentity()
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync(null, "Kozilek, the Great Distortion", owner.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ColorId("Colorless"), (await context.Decks.SingleAsync()).ColorDataId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task AddDeckPageWithoutSecondCommanderPersistsNull(string? secondCommander)
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", owner.Id, secondCommander);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null((await context.Decks.SingleAsync()).SecondCommander);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task AddDeckPageWithoutCommanderReturnsPageWithValidationErrorAndDoesNotPersist(string? commander)
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Arcane", commander, owner.Id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(await context.Decks.AnyAsync());
    }

    [Fact]
    public async Task AddDeckPageWithoutOwnerReturnsPageWithValidationErrorAndDoesNotPersist()
    {
        var (context, _) = await CreateContextWithOwnerAsync();
        await using var __ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Single(model.People);
        Assert.False(await context.Decks.AnyAsync());
    }

    [Theory]
    [InlineData("Unknown Commander", null, "Commander")]
    [InlineData("Niv-Mizzet", "Unknown Partner", "SecondCommander")]
    public async Task AddDeckPageWithCommanderLackingColorIdentityReturnsPageWithErrorAndDoesNotPersist(
        string commander, string? secondCommander, string errorKey)
    {
        var (context, owner) = await CreateContextWithOwnerAsync();
        await using var _ = context;
        var model = new AddDeckModel(context, Catalog());

        var result = await model.OnPostAsync("Arcane", commander, owner.Id, secondCommander);

        Assert.IsType<PageResult>(result);
        Assert.True(model.ModelState[errorKey]?.Errors.Count > 0);
        Assert.False(await context.Decks.AnyAsync());
    }

    [Fact]
    public async Task AddDeckPageGetLoadsExistingPeopleForOwnerDropdown()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        context.People.AddRange(new Person { Name = "Bob" }, new Person { Name = "Alice" });
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context, new FakeCommanderCatalog());

        await model.OnGetAsync();

        Assert.Equal(new[] { "Alice", "Bob" }, model.People.Select(p => p.Name));
    }

    [Fact]
    public async Task AddDeckPageGetLoadsCommandersAndTheirColorsFromCatalog()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var catalog = new FakeCommanderCatalog("Atraxa, Praetors' Voice", "Meren of Clan Nel Toth")
        {
            ColorIdentities = { ["Meren of Clan Nel Toth"] = "BG" }
        };
        var model = new AddDeckModel(context, catalog);

        await model.OnGetAsync();

        Assert.Equal(new[] { "Atraxa, Praetors' Voice", "Meren of Clan Nel Toth" }, model.Commanders);
        Assert.Equal("BG", model.CommanderColors["Meren of Clan Nel Toth"]);
    }

    [Fact]
    public async Task AddDeckPageReRenderAfterValidationErrorExposesComputedColorIdentity()
    {
        var (context, _) = await CreateContextWithOwnerAsync();
        await using var __ = context;
        var model = new AddDeckModel(context, Catalog());

        await model.OnPostAsync("Arcane", "Niv-Mizzet", null);

        Assert.Equal("Izzet", model.ColorIdentity?.Name);
    }

    [Fact]
    public void ColorIdentitySeeder_ContainsAllThirtyTwoColorIdentitiesWithUniqueIdsAndNames()
    {
        var colors = ColorIdentitySeeder.All;

        Assert.Equal(32, colors.Count);
        Assert.Equal(colors.Count, colors.Select(c => c.Id).Distinct().Count());
        Assert.Equal(colors.Count, colors.Select(c => c.Name).Distinct().Count());

        Assert.Contains(colors, c => c.Name == "Colorless" && c.Abbreviation == "C");
        Assert.Contains(colors, c => c.Name == "Mono White" && c.Abbreviation == "W");
        Assert.Contains(colors, c => c.Name == "Azorius" && c.Abbreviation == "WU");
        Assert.Contains(colors, c => c.Name == "Esper" && c.Abbreviation == "WUB");
        Assert.Contains(colors, c => c.Name == "Yore-Tiller" && c.Abbreviation == "WUBR");
        Assert.Contains(colors, c => c.Name == "Five Colour" && c.Abbreviation == "WUBRG");
    }

    [Theory]
    [InlineData("", "Colorless")]
    [InlineData("G", "Mono Green")]
    [InlineData("UB", "Dimir")]
    [InlineData("BU", "Dimir")]
    [InlineData("WWUU", "Azorius")]
    [InlineData("GRB", "Jund")]
    [InlineData("WUBRG", "Five Colour")]
    public void ColorIdentitySeeder_FindIgnoresOrderAndDuplicates(string letters, string expectedName)
    {
        Assert.Equal(expectedName, ColorIdentitySeeder.Find(letters)?.Name);
    }

    [Fact]
    public void ColorIdentitySeeder_FindReturnsNullForUnknownLetters()
    {
        Assert.Null(ColorIdentitySeeder.Find("X"));
    }
}
