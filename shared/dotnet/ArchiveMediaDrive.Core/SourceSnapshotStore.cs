namespace ArchiveMediaDrive.Core;

public sealed record SourceSnapshot(
    string SourceId,
    string Query,
    IReadOnlyList<string> Identifiers,
    DateTimeOffset RefreshedAt,
    int Count,
    string? LastError);

public interface ISourceSnapshotStore
{
    Task<SourceSnapshot?> GetAsync(string sourceId, CancellationToken cancellationToken);
    Task SaveAsync(SourceSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class SourceRefreshService
{
    private readonly IIaSourceResolver _resolver;
    private readonly ISourceSnapshotStore _store;

    public SourceRefreshService(IIaSourceResolver resolver, ISourceSnapshotStore store)
    {
        _resolver = resolver;
        _store = store;
    }

    public async Task<SourceSnapshot> RefreshAsync(SourceDefinition source, CancellationToken cancellationToken)
    {
        var query = SourceQuery.For(source);
        var previous = await _store.GetAsync(source.Id, cancellationToken);

        try
        {
            var identifiers = await _resolver.ResolveAsync(source, cancellationToken);
            var snapshot = new SourceSnapshot(
                source.Id,
                query,
                identifiers,
                DateTimeOffset.UtcNow,
                identifiers.Count,
                LastError: null);

            await _store.SaveAsync(snapshot, cancellationToken);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var preserved = previous is { } prior ? prior.Identifiers : Array.Empty<string>();
            var snapshot = new SourceSnapshot(
                source.Id,
                query,
                preserved,
                previous?.RefreshedAt ?? DateTimeOffset.UtcNow,
                preserved.Count,
                LastError: ex.Message);

            await _store.SaveAsync(snapshot, cancellationToken);
            return snapshot;
        }
    }
}

public static class SourceQuery
{
    public static string For(SourceDefinition source)
    {
        return source.Kind switch
        {
            SourceKind.Item => SourceNormalizer.NormalizeValue(SourceKind.Item, source.Value),
            SourceKind.Collection => $"collection:{SourceNormalizer.NormalizeValue(SourceKind.Collection, source.Value)}",
            SourceKind.Favorites => $"collection:fav-{SourceNormalizer.NormalizeValue(SourceKind.Favorites, source.Value)}",
            SourceKind.Search => source.Value,
            _ => source.Value,
        };
    }
}
