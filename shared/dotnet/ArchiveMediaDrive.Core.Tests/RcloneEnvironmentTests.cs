using System.Diagnostics;
using System.Runtime.InteropServices;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneEnvironmentTests
{
    private sealed class FakeRuntimeManager : IRcloneRuntimeManager
    {
        public FakeRuntimeManager(string? executablePath = null)
        {
            ExecutablePath = executablePath ?? "/tmp/amd-fake-rclone";
            RuntimeDirectory = Path.Combine(Path.GetTempPath(), $"amd-fake-rclone-dir-{Guid.NewGuid():N}");
            ReceiptPath = Path.Combine(RuntimeDirectory, "receipt.json");
        }

        public string ExecutablePath { get; }
        public string RuntimeDirectory { get; }
        public string ReceiptPath { get; }
        public Task<string> EnsureInstalledAsync(CancellationToken cancellationToken) => Task.FromResult(ExecutablePath);
        public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RepairAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeResolver : IIaSourceResolver
    {
        private readonly Func<SourceDefinition, IReadOnlyList<string>> _resolve;
        public FakeResolver(Func<SourceDefinition, IReadOnlyList<string>> resolve) => _resolve = resolve;
        public Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken)
            => Task.FromResult(_resolve(source));
    }

    private static string CreateFakeRclone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"amd-fake-rclone-{Guid.NewGuid():N}");
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var proc = Process.Start(new ProcessStartInfo("chmod", $"u+x \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            proc?.WaitForExit();
        }
        return path;
    }

    [Fact]
    public async Task WriteCombineConfigAsync_writes_combine_remote_with_resolved_identifiers()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        var rclone = CreateFakeRclone();
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(rclone), tmp);
            var sources = new[]
            {
                new SourceDefinition { Id = "prelinger", Name = "Prelinger", Kind = SourceKind.Collection, Value = "prelinger", Enabled = true },
                new SourceDefinition { Id = "disabled", Name = "Disabled", Kind = SourceKind.Collection, Value = "disabled", Enabled = false },
            };
            var resolver = new FakeResolver(s => s.Id == "prelinger" ? new[] { "itemA", "itemB" } : Array.Empty<string>());

            var ok = await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

            Assert.True(ok);
            var config = File.ReadAllText(env.ConfigPath);
            Assert.Contains("[archive-media-drive-ia]", config);
            Assert.Contains("type = internetarchive", config);
            Assert.Contains("[archive-media-drive-library]", config);
            Assert.Contains("type = combine", config);
            Assert.Contains("\"Prelinger/itemA=archive-media-drive-ia:itemA\"", config);
            Assert.Contains("\"Prelinger/itemB=archive-media-drive-ia:itemB\"", config);
            Assert.DoesNotContain("Disabled", config);
        }
        finally
        {
            Directory.Delete(tmp, true);
            File.Delete(rclone);
        }
    }

    [Fact]
    public async Task WriteCombineConfigAsync_skips_combine_section_when_no_identifiers_resolved()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        var rclone = CreateFakeRclone();
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(rclone), tmp);
            var sources = Array.Empty<SourceDefinition>();
            var resolver = new FakeResolver(_ => Array.Empty<string>());

            var ok = await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

            Assert.False(ok);
            var config = File.ReadAllText(env.ConfigPath);
            Assert.Contains("[archive-media-drive-ia]", config);
            Assert.DoesNotContain("[archive-media-drive-library]", config);
        }
        finally
        {
            Directory.Delete(tmp, true);
            File.Delete(rclone);
        }
    }

    [Fact]
    public async Task WriteCombineConfigAsync_sanitizes_source_names()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        var rclone = CreateFakeRclone();
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(rclone), tmp);
            var sources = new[]
            {
                new SourceDefinition { Id = "s1", Name = "My/Special:Source", Kind = SourceKind.Collection, Value = "x", Enabled = true },
            };
            var resolver = new FakeResolver(_ => new[] { "item1" });

            var ok = await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

            Assert.True(ok);
            var config = File.ReadAllText(env.ConfigPath);
            Assert.Contains("\"My-Special-Source/item1=archive-media-drive-ia:item1\"", config);
        }
        finally
        {
            Directory.Delete(tmp, true);
            File.Delete(rclone);
        }
    }
}
