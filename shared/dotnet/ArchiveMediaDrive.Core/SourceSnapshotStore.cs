using System.Text;
using System.Text.Json;

namespace ArchiveMediaDrive.Core;

public sealed record SourceSnapshot(
    string SourceId,
    string Query,
    IReadOnlyList<string> Identifiers,
    DateTimeOffset RefreshedAt,
    DateTimeOffset LastAttempt,
    int Count,
    string? LastError);

public interface ISourceSnapshotStore
{
    Task<SourceSnapshot?> GetAsync(string sourceId, CancellationToken cancellationToken);
    Task SaveAsync(SourceSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class SourceRefreshService
{
    private const int MaxIdentifiers = 100000;

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
        var now = DateTimeOffset.UtcNow;

        try
        {
            var identifiers = await _resolver.ResolveAsync(source, cancellationToken);
            var limited = SortAndLimit(identifiers);
            var snapshot = new SourceSnapshot(
                source.Id,
                query,
                limited,
                now,
                now,
                limited.Count,
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
                previous?.RefreshedAt ?? now,
                now,
                preserved.Count,
                LastError: ex.Message);

            await _store.SaveAsync(snapshot, cancellationToken);
            return snapshot;
        }
    }

    private static IReadOnlyList<string> SortAndLimit(IReadOnlyList<string> identifiers)
    {
        var sorted = identifiers.OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (sorted.Count > MaxIdentifiers)
            sorted.RemoveRange(MaxIdentifiers, sorted.Count - MaxIdentifiers);
        return sorted;
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

public sealed class FileSystemSourceSnapshotStore : ISourceSnapshotStore
{
    private readonly string _storeDirectory;

    public FileSystemSourceSnapshotStore(string storeDirectory)
    {
        _storeDirectory = storeDirectory;
    }

    public Task<SourceSnapshot?> GetAsync(string sourceId, CancellationToken cancellationToken)
    {
        var path = GetPath(sourceId);
        if (!File.Exists(path))
            return Task.FromResult<SourceSnapshot?>(null);

        var json = File.ReadAllText(path);
        var snapshot = JsonSerializer.Deserialize<SourceSnapshot>(json, ArchiveMediaDriveJson.Options);
        return Task.FromResult(snapshot);
    }

    public async Task SaveAsync(SourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_storeDirectory);

        var path = GetPath(snapshot.SourceId);
        var temp = path + ".new";
        var previous = path + ".previous";

        await Task.Run(() =>
        {
            var json = JsonSerializer.Serialize(snapshot, ArchiveMediaDriveJson.Options);
            File.WriteAllText(temp, json);
            if (File.Exists(path))
            {
                TryDelete(previous);
                File.Replace(temp, path, previous);
            }
            else
            {
                File.Move(temp, path);
            }
        }, cancellationToken);
    }

    private string GetPath(string sourceId)
    {
        var fileName = SanitizeFileName(sourceId) + ".json";
        return Path.Combine(_storeDirectory, fileName);
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                sb.Append(c);
            else
                sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "snapshot" : result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
