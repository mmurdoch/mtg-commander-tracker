using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using mtg_game_tracker.Services;
using Xunit;

namespace mtg_game_tracker.Tests;

public sealed class ScryfallCommanderCatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"commander-catalog-{Guid.NewGuid()}");
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly List<HttpRequestMessage> _requests = new();
    private Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public ScryfallCommanderCatalogTests()
    {
        _respond = DefaultResponder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string CachePath => Path.Combine(_folder, "commanders.json");

    private ScryfallCommanderCatalog CreateCatalog()
    {
        var handler = new StubHandler(request =>
        {
            _requests.Add(request);
            return _respond(request);
        });
        return new ScryfallCommanderCatalog(
            new HttpClient(handler),
            CachePath,
            _time,
            NullLogger<ScryfallCommanderCatalog>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage DefaultResponder(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath == "/page2")
        {
            return Json("""{"object":"list","has_more":false,"data":[{"name":"Atraxa, Praetors' Voice","color_identity":["W","U","B","G"]},{"name":"Meren of Clan Nel Toth","color_identity":["B","G"]}]}""");
        }

        return Json("""{"object":"list","has_more":true,"next_page":"https://api.scryfall.com/page2","data":[{"name":"Niv-Mizzet, Parun","color_identity":["R","U"]},{"name":"Meren of Clan Nel Toth","color_identity":["B","G"]},{"name":"Kozilek, the Great Distortion","color_identity":[]}]}""");
    }

    [Fact]
    public async Task FirstCall_FetchesEveryPageAndReturnsSortedDistinctNames()
    {
        var catalog = CreateCatalog();

        var commanders = await catalog.GetCommandersAsync();

        Assert.Equal(
            new[] { "Atraxa, Praetors' Voice", "Kozilek, the Great Distortion", "Meren of Clan Nel Toth", "Niv-Mizzet, Parun" },
            commanders);
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task ColorIdentities_MapEachCommanderToWubrgOrderedLettersAndEmptyWhenColorless()
    {
        var catalog = CreateCatalog();

        var identities = await catalog.GetColorIdentitiesAsync();

        Assert.Equal("WUBG", identities["Atraxa, Praetors' Voice"]);
        Assert.Equal("BG", identities["Meren of Clan Nel Toth"]);
        Assert.Equal("UR", identities["Niv-Mizzet, Parun"]);
        Assert.Equal("", identities["Kozilek, the Great Distortion"]);
    }

    [Fact]
    public async Task ColorIdentities_AreServedFromCacheOnSecondCall()
    {
        var catalog = CreateCatalog();
        await catalog.GetColorIdentitiesAsync();
        _requests.Clear();

        var identities = await catalog.GetColorIdentitiesAsync();

        Assert.Empty(_requests);
        Assert.Equal("UR", identities["Niv-Mizzet, Parun"]);
    }

    [Fact]
    public async Task CacheFileWithoutColorIdentities_IsRefreshedEvenWhenFresh()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(
            CachePath,
            """{"FetchedAtUtc":"2026-01-01T11:00:00+00:00","Commanders":["Old Commander"]}""");

        var identities = await CreateCatalog().GetColorIdentitiesAsync();

        Assert.Equal(2, _requests.Count);
        Assert.Equal("UR", identities["Niv-Mizzet, Parun"]);
    }

    [Fact]
    public async Task ApiFailureWithCacheLackingColorIdentities_ReturnsStaleCommandersAndNoColors()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(
            CachePath,
            """{"FetchedAtUtc":"2026-01-01T11:00:00+00:00","Commanders":["Old Commander"]}""");
        _respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var catalog = CreateCatalog();

        Assert.Equal(new[] { "Old Commander" }, await catalog.GetCommandersAsync());
        Assert.Empty(await catalog.GetColorIdentitiesAsync());
    }

    [Fact]
    public async Task FirstCall_UsesExpectedScryfallQueryAndHeaders()
    {
        var catalog = CreateCatalog();

        await catalog.GetCommandersAsync();

        var first = _requests[0];
        Assert.Equal("api.scryfall.com", first.RequestUri!.Host);
        Assert.Equal("/cards/search", first.RequestUri.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(first.RequestUri.Query);
        Assert.Equal("f:edh (is:commander or (t:legendary t:creature))", query["q"]);
        Assert.Equal("cards", query["unique"]);
        Assert.NotEmpty(first.Headers.UserAgent);
        Assert.Contains(first.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task FirstCall_WritesCacheFileToDisk()
    {
        var catalog = CreateCatalog();

        await catalog.GetCommandersAsync();

        Assert.True(File.Exists(CachePath));
        Assert.Contains("Niv-Mizzet, Parun", await File.ReadAllTextAsync(CachePath));
    }

    [Fact]
    public async Task SecondCallWithinADay_DoesNotCallApiAgain()
    {
        var catalog = CreateCatalog();
        await catalog.GetCommandersAsync();
        _requests.Clear();
        _time.Advance(TimeSpan.FromHours(23));

        var commanders = await catalog.GetCommandersAsync();

        Assert.Empty(_requests);
        Assert.Equal(4, commanders.Count);
    }

    [Fact]
    public async Task NewInstanceWithFreshCacheFile_ReadsFromDiskWithoutCallingApi()
    {
        await CreateCatalog().GetCommandersAsync();
        _requests.Clear();

        var commanders = await CreateCatalog().GetCommandersAsync();

        Assert.Empty(_requests);
        Assert.Equal(4, commanders.Count);
    }

    [Fact]
    public async Task CallAfterADay_RefreshesFromApi()
    {
        var catalog = CreateCatalog();
        await catalog.GetCommandersAsync();
        _requests.Clear();
        _time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        _respond = _ => Json("""{"object":"list","has_more":false,"data":[{"name":"Krenko, Mob Boss"}]}""");

        var commanders = await catalog.GetCommandersAsync();

        Assert.Single(_requests);
        Assert.Equal(new[] { "Krenko, Mob Boss" }, commanders);
    }

    [Fact]
    public async Task ApiFailureWithStaleCache_ReturnsStaleCommanders()
    {
        var catalog = CreateCatalog();
        await catalog.GetCommandersAsync();
        _time.Advance(TimeSpan.FromDays(3));
        _respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var commanders = await catalog.GetCommandersAsync();

        Assert.Equal(4, commanders.Count);
    }

    [Fact]
    public async Task ApiFailureWithoutCache_ReturnsEmptyListAndWritesNoCache()
    {
        _respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var catalog = CreateCatalog();

        var commanders = await catalog.GetCommandersAsync();

        Assert.Empty(commanders);
        Assert.False(File.Exists(CachePath));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
