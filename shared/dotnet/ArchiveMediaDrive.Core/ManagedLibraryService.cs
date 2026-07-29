using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ArchiveMediaDrive.Core;

public interface IMountProcess
{
    void Start();
    void Stop();
    bool IsRunning { get; }
    event EventHandler? Exited;
    event EventHandler<string>? ErrorDataReceived;
}

public interface IMountProcessFactory
{
    IMountProcess Create(string binary, string[] args, string mountPoint);
}

public sealed class ManagedLibraryService : IDisposable
{
    private readonly RcloneMountSupervisor _supervisor;
    private readonly RcloneEnvironment _rcloneEnvironment;
    private readonly IReadOnlyList<SourceDefinition> _sources;
    private readonly IIaSourceResolver _resolver;
    private readonly string _libraryName;
    private bool _started;
    private bool _disposed;

    public string MountPoint => _supervisor.MountPoint;
    public bool IsRunning => _supervisor.IsRunning;

    public ManagedLibraryService(
        IMountProcessFactory factory,
        RcloneEnvironment rcloneEnvironment,
        IReadOnlyList<SourceDefinition> sources,
        IIaSourceResolver resolver,
        string mountPoint,
        string libraryName)
    {
        _rcloneEnvironment = rcloneEnvironment;
        _sources = sources;
        _resolver = resolver;
        _libraryName = libraryName;
        _supervisor = new RcloneMountSupervisor(
            factory,
            mountPoint,
            rcloneEnvironment.RuntimeManager.ExecutablePath,
            RcloneEnvironment.LibraryRemoteName,
            rcloneEnvironment.ConfigPath);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_started)
            return;

        await _rcloneEnvironment.EnsureReadyAsync(cancellationToken);
        var hasSources = await _rcloneEnvironment.WriteCombineConfigAsync(_sources, _resolver, cancellationToken);
        if (!hasSources)
            return;

        await _supervisor.StartAsync(cancellationToken);
        _started = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return;

        await _supervisor.StopAsync(cancellationToken);
        _started = false;
    }

    public bool CheckHealth() => _supervisor.CheckHealth();

    public string LibraryName => _libraryName;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ManagedLibraryService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _supervisor.Dispose();
    }
}

public sealed class ProcessMountProcessFactory : IMountProcessFactory
{
    public IMountProcess Create(string binary, string[] args, string mountPoint)
        => new ProcessMountProcess(binary, args, mountPoint);
}

public sealed class ProcessMountProcess : IMountProcess
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private readonly string _binary;
    private readonly string[] _args;
    private readonly string _mountPoint;
    private Process? _process;

    public ProcessMountProcess(string binary, string[] args, string mountPoint)
    {
        _binary = binary;
        _args = args;
        _mountPoint = mountPoint;
    }

    public bool IsRunning
    {
        get
        {
            try { return _process is not null && !_process.HasExited; }
            catch { return false; }
        }
    }

    public event EventHandler? Exited;
    public event EventHandler<string>? ErrorDataReceived;

    public void Start()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        var sb = new StringBuilder();
        foreach (var arg in _args)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append('"', 1).Append(arg.Replace("\"", "\\\"")).Append('"', 1);
        }
        psi.Arguments = sb.ToString();

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (s, e) => Exited?.Invoke(s, e);
        _process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                ErrorDataReceived?.Invoke(this, e.Data);
        };
        _process.Start();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (_process is null || _process.HasExited)
            return;

        try
        {
            TryUnmount();
            WaitForProcessExit(TimeSpan.FromSeconds(2));

            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch
        {
        }
    }

    private void TryUnmount()
    {
        if (IsWindows) return;

        var commands = new[]
        {
            (tool: "fusermount", args: $"-u \"{_mountPoint}\""),
            (tool: "umount", args: $"\"{_mountPoint}\""),
        };

        foreach (var (tool, args) in commands)
        {
            try
            {
                var psi = new ProcessStartInfo(tool, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                if (proc?.ExitCode == 0)
                    return;
            }
            catch
            {
            }
        }
    }

    private void WaitForProcessExit(TimeSpan timeout)
    {
        if (_process is null) return;

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout && !_process.HasExited)
        {
            _process.WaitForExit(100);
        }
    }
}
