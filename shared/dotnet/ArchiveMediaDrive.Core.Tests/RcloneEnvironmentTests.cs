using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneEnvironmentTests
{
    private sealed class FakeRuntimeManager : IRcloneRuntimeManager
    {
        public string ExecutablePath => "/tmp/amd-fake-rclone";
        public string RuntimeDirectory => "/tmp/amd-fake-rclone-dir";
        public string ReceiptPath => "/tmp/amd-fake-rclone-dir/receipt.json";
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

    [Fact]
    public async Task WriteCombineConfigAsync_writes_combine_remote_with_resolved_identifiers()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(), tmp);
            var sources = new[]
            {
                new SourceDefinition { Id = "prelinger", Name = "Prelinger", Kind = SourceKind.Collection, Value = "prelinger", Enabled = true },
                new SourceDefinition { Id = "disabled", Name = "Disabled", Kind = SourceKind.Collection, Value = "disabled", Enabled = false },
            };
            var resolver = new FakeResolver(s => s.Id == "prelinger" ? new[] { "itemA", "itemB" } : Array.Empty<string>());

            await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

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
        }
    }

    [Fact]
    public async Task WriteCombineConfigAsync_skips_combine_section_when_no_identifiers_resolved()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(), tmp);
            var sources = Array.Empty<SourceDefinition>();
            var resolver = new FakeResolver(_ => Array.Empty<string>());

            await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

            var config = File.ReadAllText(env.ConfigPath);
            Assert.Contains("[archive-media-drive-ia]", config);
            Assert.DoesNotContain("[archive-media-drive-library]", config);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task WriteCombineConfigAsync_sanitizes_source_names()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var env = new RcloneEnvironment(new FakeRuntimeManager(), tmp);
            var sources = new[]
            {
                new SourceDefinition { Id = "s1", Name = "My/Special:Source", Kind = SourceKind.Collection, Value = "x", Enabled = true },
            };
            var resolver = new FakeResolver(_ => new[] { "item1" });

            await env.WriteCombineConfigAsync(sources, resolver, CancellationToken.None);

            var config = File.ReadAllText(env.ConfigPath);
            Assert.Contains("\"My-Special-Source/item1=archive-media-drive-ia:item1\"", config);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}
