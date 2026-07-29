using System.Diagnostics;
using System.Text;

namespace ArchiveMediaDrive.Core;

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
        await _rcloneEnvironment.WriteCombineConfigAsync(_sources, _resolver, cancellationToken);
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
        _process.Start();
    }

    public void Stop()
    {
        if (_process is null || _process.HasExited)
            return;

        try
        {
            _process.Kill();
            _process.WaitForExit(5000);
        }
        catch
        {
        }
    }
}
