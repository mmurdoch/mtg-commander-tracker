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
                services.AddSingleton<ICommanderCatalog>(
                    new FakeCommanderCatalog("Atraxa, Praetors' Voice", "Meren of Clan Nel Toth")
                    {
                        ColorIdentities =
                        {
                            ["Atraxa, Praetors' Voice"] = "WUBG",
                            ["Meren of Clan Nel Toth"] = "BG",
                        }
                    });
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
    public async Task AddDeckPage_CommanderOptionsExposeTheirColorLetters()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<li[^>]*data-value=\"Meren of Clan Nel Toth\"[^>]*data-colors=\"BG\"[^>]*>"), html);
        Assert.Matches(new Regex("<li[^>]*data-value=\"Atraxa, Praetors&#x27; Voice\"[^>]*data-colors=\"WUBG\"[^>]*>"), html);
    }

    [Fact]
    public async Task AddDeckPage_ColorIdentityIsReadOnlyDisplayNotSelectable()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"ColorIdentityDisplay\"[^>]*readonly[^>]*>"), html);
        Assert.Contains("/js/color-identity.js", html);
        Assert.DoesNotContain("id=\"ColorFilter\"", html);
        Assert.DoesNotContain("id=\"ColorList\"", html);
        Assert.DoesNotContain("name=\"ColorDataId\"", html);
    }

    [Fact]
    public async Task AddDeckPage_ColorIdentityDisplayCarriesColorTableForClientSideLookup()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"ColorIdentityDisplay\"[^>]*data-colors=\"[^\"]*&quot;abbreviation&quot;:&quot;BG&quot;[^\"]*\"[^>]*>"), html);
    }

    [Fact]
    public async Task AddDeckPage_PostWithTwoCommandersCreatesDeckWithCombinedColorIdentity()
    {
        int ownerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new mtg_game_tracker.Models.Person { Name = "Alice" };
            db.People.Add(owner);
            await db.SaveChangesAsync();
            ownerId = owner.Id;
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Regex.Match(await GetAddDeckHtmlAsync(client), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);

        var response = await client.PostAsync("/AddDeck", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Commander"] = "Atraxa, Praetors' Voice",
            ["SecondCommander"] = "Meren of Clan Nel Toth",
            ["OwnerId"] = ownerId.ToString(),
            ["__RequestVerificationToken"] = token.Groups[1].Value,
        }));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var deck = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Decks.SingleAsync();
        Assert.Equal(ColorIdentitySeeder.All.Single(c => c.Name == "Witch-Maw").Id, deck.ColorDataId);
    }

    [Fact]
    public async Task AddDeckPage_StillLoadsComboBoxScript()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Contains("/js/color-filter.js", html);
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
    public async Task AddDeckPage_SecondCommanderIsOptionalComboBoxWithHiddenValue()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<input[^>]*id=\"SecondCommanderFilter\"[^>]*role=\"combobox\"[^>]*>"), html);
        Assert.Matches(new Regex("<input[^>]*type=\"hidden\"[^>]*name=\"SecondCommander\"[^>]*>"), html);
        Assert.Matches(new Regex("<ul[^>]*id=\"SecondCommanderList\"[^>]*role=\"listbox\"[^>]*>"), html);
        Assert.Contains("data-value=\"Meren of Clan Nel Toth\"", html.Substring(html.IndexOf("id=\"SecondCommanderList\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AddDeckPage_FieldsAreOrderedNameCommandersColorOwner()
    {
        var html = await GetAddDeckHtmlAsync();

        var order = new[] { "id=\"Name\"", "id=\"CommanderFilter\"", "id=\"SecondCommanderFilter\"", "id=\"ColorIdentityDisplay\"", "id=\"OwnerFilter\"" }
            .Select(marker => html.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public async Task AddDeckPage_NameIsFullWidthAndOtherFieldsAreHalfWidth()
    {
        var html = await GetAddDeckHtmlAsync();

        Assert.Matches(new Regex("<div class=\"col-12\">\\s*<label[^>]*>Name \\(optional\\)</label>"), html);
        Assert.Equal(4, Regex.Matches(html, "<div class=\"col-12 col-md-6\">").Count);
    }

    [Fact]
    public async Task ColorFilterScript_IsServed()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/color-filter.js");

        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetAddDeckHtmlAsync(HttpClient? client = null)
    {
        client ??= _factory.CreateClient();
        var response = await client.GetAsync("/AddDeck");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
