using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mtg_game_tracker.Data;
using mtg_game_tracker.Pages;
using Xunit;

namespace mtg_game_tracker.Tests;

public class AddGamePageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AddGamePageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var inMemoryDbName = $"AddGamePageIntegrationTests-{Guid.NewGuid()}";

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

    private async Task<string> GetHtmlAsync(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task RootPath_ShowsAddGamePage()
    {
        var html = await GetHtmlAsync("/");

        Assert.Contains("<h1 class=\"h3\">Add Game</h1>", html);
    }

    [Fact]
    public async Task GamesPage_StillAvailableAtGamesPath()
    {
        var html = await GetHtmlAsync("/Games");

        Assert.Contains("<h1 class=\"h3\">Games</h1>", html);
    }

    [Fact]
    public async Task AddGamePage_DoesNotRenderAddParticipantButton()
    {
        var html = await GetHtmlAsync("/AddGame");

        Assert.DoesNotContain("Add participant", html);
    }

    [Fact]
    public async Task AddGamePage_RendersPlayerCountRadiosAndSpinner()
    {
        var html = await GetHtmlAsync("/AddGame");

        Assert.Contains("Players", html);
        foreach (var value in new[] { "2", "3", "4", "more" })
        {
            Assert.Matches($"<input[^>]*type=\"radio\"[^>]*name=\"Input.PlayerCountOption\"[^>]*value=\"{value}\"", html);
        }

        var spinner = Regex.Match(html, "<input[^>]*name=\"Input.MorePlayerCount\"[^>]*>");
        Assert.True(spinner.Success, "Spinner not rendered.");
        Assert.Contains("type=\"number\"", spinner.Value);
        Assert.Contains("min=\"5\"", spinner.Value);
        Assert.Contains("max=\"10\"", spinner.Value);
        Assert.Contains("value=\"5\"", spinner.Value);
    }

    [Theory]
    [InlineData("2", 5, 2)]
    [InlineData("3", 5, 3)]
    [InlineData("4", 9, 4)]
    [InlineData("more", 5, 5)]
    [InlineData("more", 10, 10)]
    public void PlayerCount_ResolvesFromSelectedOption(string option, int more, int expected)
    {
        var input = new AddGameInput { PlayerCountOption = option, MorePlayerCount = more };

        Assert.Equal(expected, input.PlayerCount);
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(1, 5)]
    [InlineData(11, 10)]
    public void PlayerCount_ClampsMorePlayerCountBetween5And10(int more, int expected)
    {
        var input = new AddGameInput { PlayerCountOption = "more", MorePlayerCount = more };

        Assert.Equal(expected, input.PlayerCount);
    }

    [Fact]
    public async Task AddGamePage_DoesNotRenderNotesField()
    {
        var html = await GetHtmlAsync("/AddGame");

        Assert.DoesNotContain("Notes", html);
    }

    [Fact]
    public void Game_HasNoNotesProperty()
    {
        Assert.Null(typeof(mtg_game_tracker.Models.Game).GetProperty("Notes"));
    }

    [Fact]
    public async Task AddGamePage_DeckAndPlayerAreComboBoxesWithDeckFirst()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.People.Add(new mtg_game_tracker.Models.Person { Name = "Alice" });
            await db.SaveChangesAsync();
        }

        var html = await GetHtmlAsync("/AddGame");

        Assert.DoesNotContain("<select", html);
        Assert.Equal(20, Regex.Matches(html, "<div[^>]*data-combo").Count);
        Assert.Matches("<input[^>]*type=\"hidden\"[^>]*name=\"Input.ParticipantInputs\\[0\\].DeckId\"", html);
        Assert.Matches("<input[^>]*type=\"hidden\"[^>]*name=\"Input.ParticipantInputs\\[0\\].PlayerId\"", html);
        Assert.Matches("<li[^>]*data-value=\"\\d+\"[^>]*data-name=\"Alice\"", html);

        var deck = html.IndexOf("ParticipantInputs[0].DeckId", StringComparison.Ordinal);
        var player = html.IndexOf("ParticipantInputs[0].PlayerId", StringComparison.Ordinal);
        Assert.InRange(deck, 0, player - 1);
        Assert.Matches("<script src=\"/js/color-filter.js", html);
    }

    [Fact]
    public async Task AddGamePage_RendersParticipantRowsHiddenBeyondSelectedCount()
    {
        var html = await GetHtmlAsync("/AddGame");

        Assert.Equal(10, Regex.Matches(html, "class=\"card p-3 mb-3 participant-row").Count);
        Assert.Equal(6, Regex.Matches(html, "participant-row d-none").Count);
    }

    [Fact]
    public async Task AddGamePage_SubmitButtonIsLabelledAdd()
    {
        var html = await GetHtmlAsync("/AddGame");

        Assert.Matches(new Regex("<button[^>]*type=\"submit\"[^>]*>\\s*Add\\s*</button>"), html);
        Assert.DoesNotContain("Save game", html);
    }

    [Fact]
    public async Task SubmittingAddGameForm_PersistsGameWithParticipantsAndWinner()
    {
        var (game, aliceId, bobId, _) = await PostGameAsync(isDraw: false);

        Assert.Equal(bobId, game.WinnerId);
        Assert.Equal(new[] { aliceId, bobId }.OrderBy(x => x), game.Participants.Select(p => p.PlayerId).OrderBy(x => x));
    }

    [Fact]
    public async Task SubmittingAddGameForm_AsDraw_SavesNullWinner()
    {
        var (game, aliceId, bobId, _) = await PostGameAsync(isDraw: true);

        Assert.Null(game.WinnerId);
        Assert.Equal(2, game.Participants.Count);
    }

    [Fact]
    public async Task AddGamePage_RendersUncheckedDrawCheckboxAfterPlayerCount()
    {
        var html = await GetHtmlAsync("/AddGame");

        var draw = Regex.Match(html, "<input[^>]*type=\"checkbox\"[^>]*name=\"Input.IsDraw\"[^>]*>");
        Assert.True(draw.Success, "Draw checkbox not rendered.");
        Assert.DoesNotContain("checked", draw.Value);
        Assert.Contains("Game was a draw", html);
        Assert.InRange(html.IndexOf("Input.MorePlayerCount", StringComparison.Ordinal), 0, html.IndexOf("Input.IsDraw", StringComparison.Ordinal) - 1);
    }

    [Fact]
    public async Task AddGamePage_DeckOptionsListCommandersThenNameInParentheses()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new mtg_game_tracker.Models.Person { Name = "Owner" };
            db.People.Add(owner);
            await db.SaveChangesAsync();
            db.Decks.AddRange(
                new mtg_game_tracker.Models.Deck { Name = "Cats", Commander = "Alpha", SecondCommander = "Beta", OwnerId = owner.Id },
                new mtg_game_tracker.Models.Deck { Commander = "Gamma", OwnerId = owner.Id });
            await db.SaveChangesAsync();
        }

        var html = await GetHtmlAsync("/AddGame");

        Assert.Matches("<li[^>]*data-owner-id=\"\\d+\"[^>]*data-name=\"Alpha / Beta \\(Cats\\)\"[^>]*>Alpha / Beta \\(Cats\\)</li>", html);
        Assert.Matches("<li[^>]*data-name=\"Gamma\"[^>]*>Gamma</li>", html);
    }

    [Fact]
    public async Task AddGamePage_DeckOptionsAreSortedAlphabeticallyByLabel()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new mtg_game_tracker.Models.Person { Name = "Owner" };
            db.People.Add(owner);
            await db.SaveChangesAsync();
            db.Decks.AddRange(
                new mtg_game_tracker.Models.Deck { Name = "Zed", Commander = "Mango", OwnerId = owner.Id },
                new mtg_game_tracker.Models.Deck { Commander = "Zebra", OwnerId = owner.Id },
                new mtg_game_tracker.Models.Deck { Name = "Aaa", Commander = "banana", OwnerId = owner.Id },
                new mtg_game_tracker.Models.Deck { Commander = "Apple", OwnerId = owner.Id });
            await db.SaveChangesAsync();
        }

        var html = await GetHtmlAsync("/AddGame");

        var firstList = html.Substring(html.IndexOf("id=\"Deck0List\"", StringComparison.Ordinal));
        firstList = firstList.Substring(0, firstList.IndexOf("</ul>", StringComparison.Ordinal));
        var names = Regex.Matches(firstList, "data-name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(new[] { "Apple", "banana (Aaa)", "Mango (Zed)", "Zebra" }, names);
    }

    [Fact]
    public async Task SavingGame_RedirectsToAddGameWithSuccessBannerOnce()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false);

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        Assert.Matches("<div[^>]*class=\"[^\"]*alert-success[^\"]*\"[^>]*>[^<]*Game added successfully", html);
        Assert.True(html.IndexOf("alert-success", StringComparison.Ordinal) < html.IndexOf("<h1", StringComparison.Ordinal));

        var again = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("alert-success", again);
    }

    [Fact]
    public async Task SavingGame_BannerSummarisesDateParticipantsAndWinner()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false, withDecks: true);

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        var banner = Regex.Match(html, "<div[^>]*class=\"[^\"]*alert-success[^\"]*\"[^>]*>([^<]*)<").Groups[1].Value;
        Assert.Contains("2026-10-10", banner);
        Assert.Contains("Alice (Alpha)", banner);
        Assert.Contains("Bob", banner);
        Assert.Contains("Winner: Bob", banner);
    }

    [Fact]
    public async Task SavingDrawnGame_BannerSaysDrawInsteadOfWinner()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: true);

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        var banner = Regex.Match(html, "<div[^>]*class=\"[^\"]*alert-success[^\"]*\"[^>]*>([^<]*)<").Groups[1].Value;
        Assert.Contains("Draw", banner);
        Assert.DoesNotContain("Winner:", banner);
    }

    [Fact]
    public async Task SavingGame_KeepsSelectedPlayerCountOnTheResetForm()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false, playerCountOption: "3");

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        Assert.Matches("<input[^>]*id=\"players-3\"[^>]*checked", html);
        Assert.DoesNotMatch("<input[^>]*id=\"players-4\"[^>]*checked", html);
        Assert.DoesNotMatch("<input[^>]*name=\"Input.ParticipantInputs\\[0\\].PlayerId\"[^>]*value=\"[1-9]", html);
    }

    [Fact]
    public async Task SavingGame_FocusesFirstDeckComboBoxOnTheResetForm()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false);

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        var deck = Regex.Match(html, "<input[^>]*id=\"Deck0Filter\"[^>]*>");
        Assert.True(deck.Success);
        Assert.Contains("autofocus", deck.Value);
        Assert.Equal(1, Regex.Matches(html, "autofocus").Count);

        var again = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("autofocus", again);
    }

    [Fact]
    public async Task SavingGame_KeepsDateOnTheResetForm()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false, date: "2026-03-05");

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        var dateInput = Regex.Match(html, "<input[^>]*name=\"Input.Date\"[^>]*>");
        Assert.True(dateInput.Success);
        Assert.Contains("2026-03-05", dateInput.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingGame_KeepsMorePlayerCount()
    {
        var (_, _, _, client) = await PostGameAsync(isDraw: false, playerCountOption: "more", morePlayerCount: "7");

        var html = await (await client.GetAsync("/AddGame")).Content.ReadAsStringAsync();
        Assert.Matches("<input[^>]*id=\"players-more\"[^>]*checked", html);
        Assert.Matches("<input[^>]*name=\"Input.MorePlayerCount\"[^>]*value=\"7\"", html);
    }

    private async Task<(mtg_game_tracker.Models.Game Game, int AliceId, int BobId, HttpClient Client)> PostGameAsync(
        bool isDraw, string playerCountOption = "2", string morePlayerCount = "5", string date = "2026-10-10", bool withDecks = false)
    {
        int aliceId, bobId, aliceDeckId = 0;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alice = new mtg_game_tracker.Models.Person { Name = "Alice" };
            var bob = new mtg_game_tracker.Models.Person { Name = "Bob" };
            db.People.AddRange(alice, bob);
            await db.SaveChangesAsync();
            aliceId = alice.Id;
            bobId = bob.Id;
            if (withDecks)
            {
                var deck = new mtg_game_tracker.Models.Deck { Commander = "Alpha", OwnerId = alice.Id };
                db.Decks.Add(deck);
                await db.SaveChangesAsync();
                aliceDeckId = deck.Id;
            }
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var getResponse = await client.GetAsync("/AddGame");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(tokenMatch.Success, "Antiforgery token was not rendered.");

        // Browser submits every named input as rendered; only chosen players/winner are changed.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", tokenMatch.Groups[1].Value),
            new("Input.Date", date),
            new("Input.PlayerCountOption", playerCountOption),
            new("Input.MorePlayerCount", morePlayerCount),
        };
        for (var i = 0; i < 10; i++)
        {
            // A cleared player combo submits an empty PlayerId.
            var player = i == 0 ? aliceId.ToString() : i == 1 ? bobId.ToString() : "";
            fields.Add(new($"Input.ParticipantInputs[{i}].DeckId", i == 0 && aliceDeckId > 0 ? aliceDeckId.ToString() : ""));
            fields.Add(new($"Input.ParticipantInputs[{i}].PlayerId", player));
        }
        fields.Add(new("Input.WinnerPlayerId", bobId.ToString()));
        if (isDraw)
        {
            fields.Add(new("Input.IsDraw", "true"));
            fields.Add(new("Input.IsDraw", "false"));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/AddGame") { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", string.Join("; ", getResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));

        var postResponse = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, postResponse.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await verifyDb.Games.Include(g => g.Participants).SingleAsync();
        return (game, aliceId, bobId, client);
    }
}
