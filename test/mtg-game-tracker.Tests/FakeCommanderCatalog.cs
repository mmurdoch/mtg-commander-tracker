using mtg_game_tracker.Services;

namespace mtg_game_tracker.Tests;

public sealed class FakeCommanderCatalog(params string[] commanders) : ICommanderCatalog
{
    // Commander name -> color letters (e.g. "UR"); an empty string means colorless.
    public Dictionary<string, string> ColorIdentities { get; } = new();

    public Task<IReadOnlyList<string>> GetCommandersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(commanders);

    public Task<IReadOnlyDictionary<string, string>> GetColorIdentitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(ColorIdentities);
}
