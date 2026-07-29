using System.Diagnostics;

namespace ArchiveMediaDrive.Core;

public interface IMountProcess
{
    void Start();
    void Stop();
    bool IsRunning { get; }
    event EventHandler? Exited;
}

public interface IMountProcessFactory
{
    IMountProcess Create(string binary, string[] args, string mountPoint);
}

public sealed class RcloneMountException : Exception
{
    public RcloneMountException(string message) : base(message) { }
}

public sealed class RcloneMountSupervisor : IDisposable
{
    private readonly IMountProcessFactory _factory;
    private readonly string _mountPoint;
    private readonly string _rcloneBinary;
    private readonly string _remoteName;
    private readonly string _configPath;
    private readonly CancellationTokenSource _stopCts = new();
    private IMountProcess? _process;
    private int _restartCount;
    private bool _disposed;

    public int MaxRestarts { get; set; } = 5;
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(5);

    public bool IsRunning => _process?.IsRunning ?? false;
    public string MountPoint => _mountPoint;

    public RcloneMountSupervisor(
        IMountProcessFactory factory,
        string mountPoint,
        string rcloneBinary,
        string remoteName,
        string configPath = "")
    {
        _factory = factory;
        _mountPoint = mountPoint;
        _rcloneBinary = rcloneBinary;
        _remoteName = remoteName;
        _configPath = configPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_process is { IsRunning: true })
            return Task.CompletedTask;

        Directory.CreateDirectory(_mountPoint);

        _restartCount = 0;
        while (_restartCount <= MaxRestarts)
        {
            StartProcess();
            if (_process is { IsRunning: true })
                return Task.CompletedTask;
            _restartCount++;
        }

        throw new RcloneMountException($"mount restart budget exhausted after {_restartCount} attempts");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _stopCts.Cancel();
        if (_process is null)
            return Task.CompletedTask;

        _process.Exited -= OnProcessExited;
        _process.Stop();
        _process = null;
        return Task.CompletedTask;
    }

    public bool CheckHealth()
    {
        return _process is { IsRunning: true };
    }

    private void StartProcess()
    {
        if (_restartCount >= MaxRestarts)
            throw new RcloneMountException($"mount restart budget exhausted after {_restartCount} attempts");

        var args = new List<string>
        {
            "mount",
            $"{_remoteName}:",
            _mountPoint,
            "--read-only",
            "--daemon-timeout",
            "30s",
        };
        if (!string.IsNullOrEmpty(_configPath))
        {
            args.Add("--config");
            args.Add(_configPath);
        }

        _process = _factory.Create(_rcloneBinary, args.ToArray(), _mountPoint);
        _process.Exited += OnProcessExited;
        _process.Start();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed || _stopCts.IsCancellationRequested || _process is null)
            return;

        _restartCount++;
        if (_restartCount > MaxRestarts)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RestartDelay, _stopCts.Token);
                if (_disposed || _stopCts.IsCancellationRequested)
                    return;
                StartProcess();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"rclone mount restart failed: {ex.Message}");
            }
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RcloneMountSupervisor));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopCts.Cancel();
        _process?.Stop();
        _process = null;
        _stopCts.Dispose();
    }
}
