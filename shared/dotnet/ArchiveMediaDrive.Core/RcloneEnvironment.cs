using System.Diagnostics;
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

    public async Task<bool> WriteCombineConfigAsync(
        IReadOnlyList<SourceDefinition> sources,
        IIaSourceResolver resolver,
        CancellationToken cancellationToken)
    {
        var rcloneBinary = await EnsureReadyAsync(cancellationToken);
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

        if (upstreams.Count == 0)
            return false;

        sb.AppendLine($"[{LibraryRemoteName}]");
        sb.AppendLine("type = combine");
        sb.AppendLine($"upstreams = {string.Join(" ", upstreams)}");

        var candidate = ConfigPath + ".new";
        var previous = ConfigPath + ".previous";
        var configText = sb.ToString();

        await Task.Run(() => File.WriteAllText(candidate, configText), cancellationToken);

        if (!await ValidateConfigAsync(rcloneBinary, candidate, cancellationToken))
        {
            TryDelete(candidate);
            return false;
        }

        await Task.Run(() =>
        {
            if (File.Exists(ConfigPath))
            {
                TryDelete(previous);
                File.Replace(candidate, ConfigPath, previous);
            }
            else
            {
                File.Move(candidate, ConfigPath);
            }
        }, cancellationToken);

        return true;
    }

    private static async Task<bool> ValidateConfigAsync(string rcloneBinary, string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(rcloneBinary))
            return false;

        return await RunRcloneCheckAsync(rcloneBinary, $"config show --config \"{configPath}\"", cancellationToken) &&
               await RunRcloneCheckAsync(rcloneBinary, $"lsf \"{LibraryRemoteName}:\" --config \"{configPath}\" --max-depth 0", cancellationToken);
    }

    private static async Task<bool> RunRcloneCheckAsync(string rcloneBinary, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(rcloneBinary, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            while (!proc.HasExited)
            {
                cts.Token.ThrowIfCancellationRequested();
                proc.WaitForExit(100);
            }

            await stdoutTask;
            await stderrTask;

            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
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
