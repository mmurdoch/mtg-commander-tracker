using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using mtg_game_tracker.Data;
using mtg_game_tracker.Services;
using Xunit;

namespace mtg_game_tracker.Tests;

public class AddDeckColorFilterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AddDeckColorFilterTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"AddDeckColorFilterTests-{Guid.NewGuid()}";

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
                services.AddSingleton<ICommanderCatalog>(new FakeCommanderCatalog("Atraxa, Praetors' Voice", "Meren of Clan Nel Toth"));
            });
        });
    }

    [Fact]
    public async Task AddDeckPage_CommanderIsComboBoxWithHiddenValueAndNoTextInputNamedCommander()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"CommanderFilter\"[^>]*role=\"combobox\"[^>]*>"), html);
        Assert.Matches(new Regex("<input[^>]*type=\"hidden\"[^>]*name=\"Commander\"[^>]*>"), html);
        Assert.Matches(new Regex("<ul[^>]*id=\"CommanderList\"[^>]*role=\"listbox\"[^>]*>"), html);
        Assert.DoesNotMatch(new Regex("<input[^>]*type=\"text\"[^>]*name=\"Commander\""), html);
    }

    [Fact]
    public async Task AddDeckPage_CommanderOptionsComeFromCatalogWithEncodedNames()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(
            new Regex("<li[^>]*data-value=\"Atraxa, Praetors&#x27; Voice\"[^>]*data-name=\"Atraxa, Praetors&#x27; Voice\"[^>]*>"),
            html);
        Assert.Contains("data-value=\"Meren of Clan Nel Toth\"", html);
    }

    [Fact]
    public async Task AddDeckPage_RendersColorFilterInputAndScript()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"ColorFilter\"[^>]*role=\"combobox\"[^>]*>"), html);
        Assert.Contains("/js/color-filter.js", html);
    }

    [Fact]
    public async Task AddDeckPage_ColorIdentityIsComboBoxWithHiddenValueAndNoSelect()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*type=\"hidden\"[^>]*name=\"ColorDataId\"[^>]*>"), html);
        Assert.Matches(new Regex("<ul[^>]*id=\"ColorList\"[^>]*role=\"listbox\"[^>]*>"), html);
        Assert.DoesNotContain("<select id=\"ColorDataId\"", html);
        Assert.DoesNotContain("name=\"ColorDataId\" class=\"form-select\"", html);
    }

    [Fact]
    public async Task AddDeckPage_ColorOptionsExposeNameAndAbbreviationForFiltering()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(
            new Regex("<li[^>]*data-name=\"Golgari\"[^>]*data-abbreviation=\"BG\"[^>]*>|<li[^>]*data-abbreviation=\"BG\"[^>]*data-name=\"Golgari\"[^>]*>"),
            html);
    }

    [Fact]
    public async Task AddDeckPage_OwnerIsComboBoxWithHiddenValueAndNoSelect()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"OwnerFilter\"[^>]*role=\"combobox\"[^>]*>"), html);
        Assert.Matches(new Regex("<input[^>]*type=\"hidden\"[^>]*name=\"OwnerId\"[^>]*>"), html);
        Assert.Matches(new Regex("<ul[^>]*id=\"OwnerList\"[^>]*role=\"listbox\"[^>]*>"), html);
        Assert.DoesNotContain("<select", html);
    }

    [Fact]
    public async Task ColorFilterScript_IsServed()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/color-filter.js");

        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetAddDeckHtmlAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/AddDeck");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
