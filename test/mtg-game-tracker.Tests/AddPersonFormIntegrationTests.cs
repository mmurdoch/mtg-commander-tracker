using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using Xunit;

namespace mtg_game_tracker.Tests;

public class AddPersonFormIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AddPersonFormIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Unique per-instance database name so this test never touches the real SQLite file in App_Data.
        var inMemoryDbName = $"AddPersonFormIntegrationTests-{Guid.NewGuid()}";

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
    public async Task SubmittingAddPersonForm_PersistsPerson()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var getResponse = await client.GetAsync("/AddPerson");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(tokenMatch.Success, "Antiforgery token was not rendered on the Add Person form.");

        var nameInputMatch = Regex.Match(html, "<input[^>]*name=\"Name\"[^>]*>");
        Assert.True(nameInputMatch.Success, "The Name input was not rendered with a 'name' attribute.");

        var cookieHeader = string.Join("; ", getResponse.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0]));

        var request = new HttpRequestMessage(HttpMethod.Post, "/AddPerson")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = "Integration Person",
                ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value
            })
        };
        request.Headers.Add("Cookie", cookieHeader);

        var postResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.People.AnyAsync(p => p.Name == "Integration Person"));
    }

    [Fact]
    public async Task AddPersonPage_HasAddButtonAndAutofocusedNameInput()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/AddPerson");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var nameInputMatch = Regex.Match(html, "<input[^>]*name=\"Name\"[^>]*>");
        Assert.True(nameInputMatch.Success, "The Name input was not rendered with a 'name' attribute.");
        Assert.Contains("autofocus", nameInputMatch.Value);

        Assert.Matches(new Regex("<button[^>]*type=\"submit\"[^>]*>\\s*Add\\s*</button>"), html);
    }
}
