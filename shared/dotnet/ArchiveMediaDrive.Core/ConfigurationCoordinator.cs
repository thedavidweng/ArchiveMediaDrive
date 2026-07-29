using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchiveMediaDrive.Core;

public sealed record ConfigurationError(string Field, string Message);

public sealed class ConfigurationResult
{
    public bool Succeeded { get; init; }
    public string Hash { get; init; } = string.Empty;
    public IReadOnlyList<ConfigurationError> Errors { get; init; } = Array.Empty<ConfigurationError>();
    public IReadOnlyList<SourceDefinition> Sources { get; init; } = Array.Empty<SourceDefinition>();
    public bool HashChanged { get; init; }
}

public interface IConfigurationCoordinator
{
    Task<ConfigurationResult> ApplyAsync(IReadOnlyList<SourceDefinition> sources, CancellationToken cancellationToken);
}

public sealed class ConfigurationCoordinator : IConfigurationCoordinator
{
    private static readonly Regex SourceIdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    private readonly RcloneEnvironment _environment;
    private readonly SourceRefreshService _refresh;
    private readonly ISourceSnapshotStore _store;
    private readonly IIaSourceResolver _resolver;
    private readonly ManagedLibraryService? _mount;
    private readonly string _sourcesPath;
    private readonly string _previousSourcesPath;

    public ConfigurationCoordinator(
        RcloneEnvironment environment,
        SourceRefreshService refresh,
        ISourceSnapshotStore store,
        IIaSourceResolver resolver,
        string dataDirectory,
        ManagedLibraryService? mount = null)
    {
        _environment = environment;
        _refresh = refresh;
        _store = store;
        _resolver = resolver;
        _mount = mount;
        _sourcesPath = Path.Combine(dataDirectory, "sources.json");
        _previousSourcesPath = _sourcesPath + ".previous";
    }

    public async Task<ConfigurationResult> ApplyAsync(IReadOnlyList<SourceDefinition> sources, CancellationToken cancellationToken)
    {
        var validation = Validate(sources);
        if (!validation.IsValid)
            return new ConfigurationResult { Succeeded = false, Errors = validation.Errors };

        var normalized = validation.Normalized;
        var newHash = ComputeHash(normalized);
        var previousHash = await ReadPreviousHashAsync(cancellationToken);

        if (newHash == previousHash)
            return new ConfigurationResult { Succeeded = true, Hash = newHash, Sources = normalized, HashChanged = false };

        var previousSources = await ReadSourcesAsync(cancellationToken);
        var previousSnapshots = new Dictionary<string, SourceSnapshot?>();
        var changedIds = new HashSet<string>();

        foreach (var source in normalized)
        {
            var old = previousSources.FirstOrDefault(s => s.Id == source.Id);
            if (old is null || !AreEquivalent(old, source))
            {
                changedIds.Add(source.Id);
                previousSnapshots[source.Id] = await _store.GetAsync(source.Id, cancellationToken);
            }
        }

        var mountWasRunning = _mount?.IsRunning ?? false;
        await SaveSourcesAsync(normalized, cancellationToken);

        try
        {
            foreach (var source in normalized.Where(s => changedIds.Contains(s.Id)))
            {
                await _refresh.RefreshAsync(source, cancellationToken);
            }

            var hasConfig = await _environment.WriteCombineConfigAsync(normalized, _resolver, cancellationToken);

            if (_mount is not null)
            {
                await _mount.StopAsync(cancellationToken);
                await _mount.UpdateSourcesAsync(normalized, cancellationToken);
                if (hasConfig)
                    await _mount.StartAsync(cancellationToken);
            }

            return new ConfigurationResult { Succeeded = true, Hash = newHash, Sources = normalized, HashChanged = true };
        }
        catch (Exception ex)
        {
            await RollbackAsync(previousSources, previousSnapshots, mountWasRunning, cancellationToken);
            return new ConfigurationResult
            {
                Succeeded = false,
                Hash = newHash,
                Sources = normalized,
                HashChanged = true,
                Errors = new[] { new ConfigurationError("", ex.Message) },
            };
        }
    }

    private static (bool IsValid, IReadOnlyList<ConfigurationError> Errors, IReadOnlyList<SourceDefinition> Normalized) Validate(IReadOnlyList<SourceDefinition> sources)
    {
        var errors = new List<ConfigurationError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SourceDefinition>();

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var fieldPrefix = string.IsNullOrEmpty(source.Id) ? $"source[{i}]" : source.Id;

            if (string.IsNullOrWhiteSpace(source.Id))
            {
                errors.Add(new ConfigurationError(fieldPrefix, "id is required"));
                continue;
            }

            if (!SourceIdPattern.IsMatch(source.Id))
                errors.Add(new ConfigurationError($"{fieldPrefix}.id", "id must be 1-64 lowercase letters, numbers, or dashes"));

            if (seen.Contains(source.Id))
                errors.Add(new ConfigurationError($"{fieldPrefix}.id", $"duplicate id '{source.Id}'"));
            else
                seen.Add(source.Id);

            if (string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 120)
                errors.Add(new ConfigurationError($"{fieldPrefix}.name", "name is required and must be 120 characters or fewer"));

            if (string.IsNullOrWhiteSpace(source.Value) || source.Value.Length > 4096)
            {
                errors.Add(new ConfigurationError($"{fieldPrefix}.value", "value is required and must be 4096 characters or fewer"));
            }
            else
            {
                try
                {
                    var _ = SourceNormalizer.NormalizeValue(source.Kind, source.Value);
                }
                catch (SourceContractException ex)
                {
                    errors.Add(new ConfigurationError($"{fieldPrefix}.value", ex.Message));
                }
            }

            if (source.RefreshMinutes < 1 || source.RefreshMinutes > 10080)
                errors.Add(new ConfigurationError($"{fieldPrefix}.refreshMinutes", "refresh interval must be between 1 and 10080 minutes"));

            if (!Enum.IsDefined(typeof(SourceKind), source.Kind))
                errors.Add(new ConfigurationError($"{fieldPrefix}.kind", "kind is not supported"));

            if (source.AuthenticationRef is not null && source.AuthenticationRef.Length > 128)
                errors.Add(new ConfigurationError($"{fieldPrefix}.authenticationRef", "authentication reference must be 128 characters or fewer"));

            normalized.Add(new SourceDefinition
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                Name = source.Name,
                Kind = source.Kind,
                Value = source.Value,
                Enabled = source.Enabled,
                RefreshMinutes = source.RefreshMinutes,
                AuthenticationRef = source.AuthenticationRef,
            });
        }

        return (errors.Count == 0, errors, normalized);
    }

    private static bool AreEquivalent(SourceDefinition a, SourceDefinition b)
    {
        return a.Id == b.Id
            && a.Name == b.Name
            && a.Kind == b.Kind
            && a.Value == b.Value
            && a.Enabled == b.Enabled
            && a.RefreshMinutes == b.RefreshMinutes
            && a.AuthenticationRef == b.AuthenticationRef;
    }

    private static string ComputeHash(IReadOnlyList<SourceDefinition> sources)
    {
        var ordered = sources.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(ordered, ArchiveMediaDriveJson.Options);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(json);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private async Task<string> ReadPreviousHashAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sourcesPath))
            return string.Empty;

        return await Task.Run(() =>
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(_sourcesPath);
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<SourceDefinition>> ReadSourcesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sourcesPath))
            return Array.Empty<SourceDefinition>();

        try
        {
            var json = await Task.Run(() => File.ReadAllText(_sourcesPath), cancellationToken);
            var list = JsonSerializer.Deserialize<List<SourceDefinition>>(json, ArchiveMediaDriveJson.Options);
            return (IReadOnlyList<SourceDefinition>?)list ?? Array.Empty<SourceDefinition>();
        }
        catch
        {
            return Array.Empty<SourceDefinition>();
        }
    }

    private async Task SaveSourcesAsync(IReadOnlyList<SourceDefinition> sources, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_sourcesPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        var candidate = _sourcesPath + ".new";
        var json = JsonSerializer.Serialize(sources, ArchiveMediaDriveJson.Options);

        await Task.Run(() =>
        {
            File.WriteAllText(candidate, json, Encoding.UTF8);
            if (File.Exists(_sourcesPath))
            {
                TryDelete(_previousSourcesPath);
                File.Replace(candidate, _sourcesPath, _previousSourcesPath);
            }
            else
            {
                File.Move(candidate, _sourcesPath);
            }
        }, cancellationToken);
    }

    private async Task RollbackAsync(
        IReadOnlyList<SourceDefinition> previousSources,
        IReadOnlyDictionary<string, SourceSnapshot?> previousSnapshots,
        bool restartMount,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_mount is not null)
                await _mount.StopAsync(cancellationToken);
        }
        catch
        {
        }

        await Task.Run(() =>
        {
            if (File.Exists(_previousSourcesPath))
            {
                TryDelete(_sourcesPath);
                File.Move(_previousSourcesPath, _sourcesPath);
            }
        }, cancellationToken);

        foreach (var pair in previousSnapshots)
        {
            if (pair.Value is null)
                continue;
            try
            {
                await _store.SaveAsync(pair.Value, cancellationToken);
            }
            catch
            {
            }
        }

        var rclonePrevious = _environment.ConfigPath + ".previous";
        await Task.Run(() =>
        {
            if (File.Exists(rclonePrevious))
            {
                TryDelete(_environment.ConfigPath);
                File.Move(rclonePrevious, _environment.ConfigPath);
            }
        }, cancellationToken);

        if (_mount is not null && restartMount && previousSources.Any(s => s.Enabled))
        {
            try
            {
                await _mount.UpdateSourcesAsync(previousSources, cancellationToken);
                await _mount.StartAsync(cancellationToken);
            }
            catch
            {
            }
        }
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
