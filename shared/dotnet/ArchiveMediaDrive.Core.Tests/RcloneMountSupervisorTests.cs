using System.Diagnostics;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneMountSupervisorTests
{
    private sealed class FakeProcessFactory : IMountProcessFactory
    {
        public FakeMountProcess? LastProcess { get; private set; }
        public Func<FakeMountProcess> CreateBehavior { get; set; } = () => new FakeMountProcess();

        public IMountProcess Create(string binary, string[] args, string mountPoint)
        {
            var proc = CreateBehavior();
            proc.Binary = binary;
            proc.Args = args;
            proc.MountPoint = mountPoint;
            LastProcess = proc;
            return proc;
        }
    }

    private sealed class FakeMountProcess : IMountProcess
    {
        public string Binary { get; set; } = "";
        public string[] Args { get; set; } = Array.Empty<string>();
        public string MountPoint { get; set; } = "";
        public bool IsRunning { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool ExitAfterStart { get; set; }
        public int ExitCode { get; set; }
        public string StdError { get; set; } = "";

        public void Start()
        {
            StartCalls++;
            IsRunning = true;
            if (ExitAfterStart)
            {
                IsRunning = false;
                Exited?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            StopCalls++;
            IsRunning = false;
        }

        public void SimulateExit(int code, string stderr = "")
        {
            IsRunning = false;
            ExitCode = code;
            StdError = stderr;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? Exited;
#pragma warning disable CS0067
        public event EventHandler<string>? ErrorDataReceived;
#pragma warning restore CS0067
    }

    [Fact]
    public async Task Start_launches_rclone_mount_process()
    {
        var factory = new FakeProcessFactory();
        var supervisor = new RcloneMountSupervisor(factory, "/tmp/amd-mount", "/tmp/amd-rclone", "amd-library");

        await supervisor.StartAsync(CancellationToken.None);

        Assert.NotNull(factory.LastProcess);
        Assert.Equal("/tmp/amd-rclone", factory.LastProcess!.Binary);
        Assert.Contains("mount", factory.LastProcess.Args);
        Assert.Equal(1, factory.LastProcess.StartCalls);
        Assert.True(supervisor.IsRunning);
    }

    [Fact]
    public async Task Stop_terminates_process()
    {
        var factory = new FakeProcessFactory();
        var supervisor = new RcloneMountSupervisor(factory, "/tmp/amd-mount", "/tmp/amd-rclone", "amd-library");

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(1, factory.LastProcess!.StopCalls);
        Assert.False(supervisor.IsRunning);
    }

    [Fact]
    public async Task Restart_budget_exhausted_raises_supervisor_exception()
    {
        var factory = new FakeProcessFactory
        {
            CreateBehavior = () => new FakeMountProcess { ExitAfterStart = true },
        };
        var supervisor = new RcloneMountSupervisor(factory, "/tmp/amd-mount", "/tmp/amd-rclone", "amd-library")
        {
            MaxRestarts = 3,
            RestartDelay = TimeSpan.FromMilliseconds(1),
        };

        await Assert.ThrowsAsync<RcloneMountException>(() => supervisor.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Health_check_returns_false_when_not_running()
    {
        var factory = new FakeProcessFactory();
        var supervisor = new RcloneMountSupervisor(factory, "/tmp/amd-mount", "/tmp/amd-rclone", "amd-library");

        var healthy = supervisor.CheckHealth();

        Assert.False(healthy);
    }

    [Fact]
    public async Task Health_check_returns_true_when_running()
    {
        var factory = new FakeProcessFactory();
        var supervisor = new RcloneMountSupervisor(factory, "/tmp/amd-mount", "/tmp/amd-rclone", "amd-library");

        await supervisor.StartAsync(CancellationToken.None);

        var healthy = supervisor.CheckHealth();

        Assert.True(healthy);
    }

    [Fact]
    public async Task Mount_point_directory_is_created_on_start()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-mount-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new FakeProcessFactory();
            var supervisor = new RcloneMountSupervisor(factory, tmp, "/tmp/amd-rclone", "amd-library");

            await supervisor.StartAsync(CancellationToken.None);

            Assert.True(Directory.Exists(tmp));
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }
}
