using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ArchiveMediaDrive.Core;

public sealed class RcloneEnvironment
{
    public const string RemoteName = "archive-media-drive-ia";
    public const string LibraryRemoteName = "archive-media-drive-library";

    private readonly IRcloneRuntimeManager _runtimeManager;
    private readonly string _configDirectory;
    private readonly SemaphoreSlim _readyGate = new(1, 1);
    private Task<string>? _readyTask;
    private string? _rcloneBinary;

    public RcloneEnvironment(IRcloneRuntimeManager runtimeManager, string configDirectory)
    {
        _runtimeManager = runtimeManager;
        _configDirectory = configDirectory;
    }

    public IRcloneRuntimeManager RuntimeManager => _runtimeManager;
    public string ConfigPath => Path.Combine(_configDirectory, "rclone.conf");
    public string RcloneBinary => _rcloneBinary ?? throw new InvalidOperationException("rclone is not ready");

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var task = Volatile.Read(ref _readyTask);
        if (task is not null)
        {
            return await WaitWithCancellationAsync(task, cancellationToken);
        }

        await _readyGate.WaitAsync(cancellationToken);
        try
        {
            if (_readyTask is null)
                _readyTask = EnsureReadyCoreAsync(CancellationToken.None);

            task = _readyTask;
        }
        finally
        {
            _readyGate.Release();
        }

        try
        {
            return await WaitWithCancellationAsync(task, cancellationToken);
        }
        catch
        {
            await _readyGate.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_readyTask, task))
                    _readyTask = null;
            }
            finally
            {
                _readyGate.Release();
            }

            throw;
        }
    }

    private async Task<string> EnsureReadyCoreAsync(CancellationToken cancellationToken)
    {
        var exePath = await _runtimeManager.EnsureInstalledAsync(cancellationToken);
        _rcloneBinary = exePath;
        EnsureConfigFile();
        return exePath;
    }

    public void EnsureConfigFile()
    {
        Directory.CreateDirectory(_configDirectory);
        if (!File.Exists(ConfigPath))
            File.WriteAllText(ConfigPath, $"[{RemoteName}]\ntype = internetarchive\n");
    }

    public async Task WriteCombineConfigAsync(
        IReadOnlyList<SourceDefinition> sources,
        IIaSourceResolver resolver,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        Directory.CreateDirectory(_configDirectory);

        var sb = new StringBuilder();
        sb.AppendLine($"[{RemoteName}]");
        sb.AppendLine("type = internetarchive");
        sb.AppendLine();

        var upstreams = new List<string>();
        var seen = new HashSet<string>();

        foreach (var source in sources.Where(s => s.Enabled))
        {
            IReadOnlyList<string> identifiers;
            try
            {
                identifiers = await resolver.ResolveAsync(source, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            var dirName = SanitizeDirectoryName(source.Name);
            foreach (var identifier in identifiers)
            {
                var virtualPath = $"{dirName}/{identifier}";
                if (!seen.Add(virtualPath))
                    continue;
                upstreams.Add($"\"{virtualPath}={RemoteName}:{identifier}\"");
            }
        }

        if (upstreams.Count > 0)
        {
            sb.AppendLine($"[{LibraryRemoteName}]");
            sb.AppendLine("type = combine");
            sb.AppendLine($"upstreams = {string.Join(" ", upstreams)}");
        }

        await Task.Run(() => File.WriteAllText(ConfigPath, sb.ToString()), cancellationToken);
    }

    private static string SanitizeDirectoryName(string name)
    {
        var sanitized = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ')
                sanitized.Append(c);
            else
                sanitized.Append('-');
        }
        var result = sanitized.ToString().Trim().TrimEnd('.');
        return string.IsNullOrEmpty(result) ? "source" : result;
    }

    public static string DetectRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }

    private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
            return await task;

        var tcs = new TaskCompletionSource<object>();
        using (cancellationToken.Register(state => ((TaskCompletionSource<object>)state!).TrySetCanceled(), tcs))
        {
            if (await Task.WhenAny(task, tcs.Task) == tcs.Task)
                throw new OperationCanceledException(cancellationToken);
        }

        return await task;
    }
}
