using System.Text.Json;
using System.Text.Json.Serialization;

namespace mtg_game_tracker.Services
{
    public interface ICommanderCatalog
    {
        Task<IReadOnlyList<string>> GetCommandersAsync(CancellationToken cancellationToken = default);
    }

    public class ScryfallCommanderCatalog : ICommanderCatalog
    {
        public const string HttpClientName = "scryfall";

        private static readonly Uri SearchUri = new("https://api.scryfall.com/cards/search?q=f%3Aedh+t%3Alegendary+t%3Acreature&unique=cards");
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan PageDelay = TimeSpan.FromMilliseconds(100);

        private readonly HttpClient _http;
        private readonly string _cachePath;
        private readonly TimeProvider _time;
        private readonly ILogger<ScryfallCommanderCatalog> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public ScryfallCommanderCatalog(HttpClient http, string cachePath, TimeProvider time, ILogger<ScryfallCommanderCatalog> logger)
        {
            _http = http;
            _cachePath = cachePath;
            _time = time;
            _logger = logger;
        }

        public async Task<IReadOnlyList<string>> GetCommandersAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                var cached = await ReadCacheAsync(cancellationToken);
                if (cached is not null && _time.GetUtcNow() - cached.FetchedAtUtc < MaxAge)
                {
                    return cached.Commanders;
                }

                try
                {
                    var commanders = await FetchAsync(cancellationToken);
                    await WriteCacheAsync(new CacheFile(_time.GetUtcNow(), commanders), cancellationToken);
                    return commanders;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not refresh the commander list from Scryfall.");
                    return cached?.Commanders ?? (IReadOnlyList<string>)Array.Empty<string>();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<List<string>> FetchAsync(CancellationToken cancellationToken)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Uri? next = SearchUri;

            while (next is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, next);
                request.Headers.UserAgent.ParseAdd("mtg-commander-tracker/1.0");
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await _http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var page = await response.Content.ReadFromJsonAsync<SearchPage>(cancellationToken)
                    ?? throw new JsonException("Empty response from Scryfall.");

                foreach (var card in page.Data)
                {
                    if (!string.IsNullOrWhiteSpace(card.Name))
                    {
                        names.Add(card.Name);
                    }
                }

                next = page.HasMore && page.NextPage is not null ? new Uri(page.NextPage) : null;
                if (next is not null)
                {
                    await Task.Delay(PageDelay, cancellationToken);
                }
            }

            return names.ToList();
        }

        private async Task<CacheFile?> ReadCacheAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            try
            {
                await using var stream = File.OpenRead(_cachePath);
                return await JsonSerializer.DeserializeAsync<CacheFile>(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogWarning(ex, "Ignoring unreadable commander cache at {Path}.", _cachePath);
                return null;
            }
        }

        private async Task WriteCacheAsync(CacheFile cache, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            var tempPath = _cachePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, cache, cancellationToken: cancellationToken);
            }

            File.Move(tempPath, _cachePath, overwrite: true);
        }

        private sealed record CacheFile(DateTimeOffset FetchedAtUtc, List<string> Commanders);

        private sealed class SearchPage
        {
            [JsonPropertyName("has_more")]
            public bool HasMore { get; set; }

            [JsonPropertyName("next_page")]
            public string? NextPage { get; set; }

            [JsonPropertyName("data")]
            public List<Card> Data { get; set; } = new();
        }

        private sealed class Card
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }
}
