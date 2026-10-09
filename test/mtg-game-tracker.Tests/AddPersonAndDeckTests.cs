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
    public async Task AddDeckPagePostsAndPersistsDeck()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var owner = new Person { Name = "Alice" };
        context.People.Add(owner);
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context);

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", owner.Id, 1);

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.Decks.SingleAsync();
        Assert.Equal("Arcane", saved.Name);
        Assert.Equal("Niv-Mizzet", saved.Commander);
        Assert.Equal(owner.Id, saved.OwnerId);
        Assert.Equal(1, saved.ColorDataId);
    }

    [Fact]
    public async Task AddDeckPageWithoutNamePersistsDeck()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var owner = new Person { Name = "Alice" };
        context.People.Add(owner);
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context);

        var result = await model.OnPostAsync(null, "Niv-Mizzet", owner.Id, 1);

        Assert.IsType<RedirectToPageResult>(result);
        var saved = await context.Decks.SingleAsync();
        Assert.Null(saved.Name);
        Assert.Equal("Niv-Mizzet", saved.Commander);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task AddDeckPageWithoutCommanderReturnsPageWithValidationErrorAndDoesNotPersist(string? commander)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var owner = new Person { Name = "Alice" };
        context.People.Add(owner);
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context);

        var result = await model.OnPostAsync("Arcane", commander, owner.Id, 1);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(await context.Decks.AnyAsync());
    }

    [Fact]
    public async Task AddDeckPageWithoutOwnerReturnsPageWithValidationErrorAndDoesNotPersist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        context.People.Add(new Person { Name = "Alice" });
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context);

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", null, 1);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Single(model.People);
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
        var model = new AddDeckModel(context);

        await model.OnGetAsync();

        Assert.Equal(new[] { "Alice", "Bob" }, model.People.Select(p => p.Name));
    }

    [Fact]
    public async Task AddDeckPageWithoutColorIdentityReturnsPageWithValidationErrorAndDoesNotPersist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var owner = new Person { Name = "Alice" };
        context.People.Add(owner);
        await context.SaveChangesAsync();
        var model = new AddDeckModel(context);

        var result = await model.OnPostAsync("Arcane", "Niv-Mizzet", owner.Id, null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(await context.Decks.AnyAsync());
    }

    [Fact]
    public void ColorIdentitySeeder_ContainsAllThirtyOneColorIdentitiesWithUniqueIdsAndNames()
    {
        var colors = ColorIdentitySeeder.All;

        Assert.Equal(31, colors.Count);
        Assert.Equal(colors.Count, colors.Select(c => c.Id).Distinct().Count());
        Assert.Equal(colors.Count, colors.Select(c => c.Name).Distinct().Count());

        Assert.Contains(colors, c => c.Name == "Mono White" && c.Abbreviation == "W");
        Assert.Contains(colors, c => c.Name == "Azorius" && c.Abbreviation == "WU");
        Assert.Contains(colors, c => c.Name == "Esper" && c.Abbreviation == "WUB");
        Assert.Contains(colors, c => c.Name == "Yore-Tiller" && c.Abbreviation == "WUBR");
        Assert.Contains(colors, c => c.Name == "Five Colour" && c.Abbreviation == "WUBRG");
    }
}
