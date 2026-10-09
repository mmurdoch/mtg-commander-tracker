using mtg_game_tracker.Services;

namespace mtg_game_tracker.Tests;

public sealed class FakeCommanderCatalog(params string[] commanders) : ICommanderCatalog
{
    public Task<IReadOnlyList<string>> GetCommandersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(commanders);
}
