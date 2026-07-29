using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class SourceRefreshServiceTests
{
    private sealed class FakeResolver : IIaSourceResolver
    {
        public Func<SourceDefinition, CancellationToken, Task<IReadOnlyList<string>>> Behavior { get; set; } = (_, _) =>
            throw new SourceContractException("not configured");

        public Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken)
            => Behavior(source, cancellationToken);
    }

    private sealed class FakeSnapshotStore : ISourceSnapshotStore
    {
        public SourceSnapshot? Stored { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<SourceSnapshot?> GetAsync(string sourceId, CancellationToken cancellationToken)
            => Task.FromResult(Stored is { } s && s.SourceId == sourceId ? s : null);

        public Task SaveAsync(SourceSnapshot snapshot, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Stored = snapshot;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Successful_refresh_replaces_snapshot_with_results_and_timestamp()
    {
        var resolver = new FakeResolver
        {
            Behavior = (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "alpha", "beta" }),
        };
        var store = new FakeSnapshotStore();
        var service = new SourceRefreshService(resolver, store);

        var snapshot = await service.RefreshAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None);

        Assert.Equal(new[] { "alpha", "beta" }, snapshot.Identifiers);
        Assert.Equal("s", snapshot.SourceId);
        Assert.Equal(2, snapshot.Count);
        Assert.Null(snapshot.LastError);
        Assert.True(snapshot.RefreshedAt > DateTimeOffset.MinValue);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task Failed_refresh_preserves_previous_snapshot_and_records_error()
    {
        var resolver = new FakeResolver
        {
            Behavior = (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "alpha" }),
        };
        var store = new FakeSnapshotStore();
        var service = new SourceRefreshService(resolver, store);

        await service.RefreshAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None);

        resolver.Behavior = (_, _) => throw new SourceContractException("archive down");

        var snapshot = await service.RefreshAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None);

        Assert.Equal(new[] { "alpha" }, snapshot.Identifiers);
        Assert.Contains("archive down", snapshot.LastError);
        Assert.Equal(2, store.SaveCalls);
    }

    [Fact]
    public async Task Failed_refresh_with_no_prior_snapshot_returns_empty_results_with_error()
    {
        var resolver = new FakeResolver
        {
            Behavior = (_, _) => throw new SourceContractException("archive down"),
        };
        var store = new FakeSnapshotStore();
        var service = new SourceRefreshService(resolver, store);

        var snapshot = await service.RefreshAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None);

        Assert.Empty(snapshot.Identifiers);
        Assert.Contains("archive down", snapshot.LastError);
    }
}
