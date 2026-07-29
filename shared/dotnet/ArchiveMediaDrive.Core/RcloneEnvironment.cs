using System.Runtime.InteropServices;

namespace ArchiveMediaDrive.Core;

public sealed class RcloneEnvironment
{
    public const string RemoteName = "archive-media-drive-ia";

    private readonly IRcloneRuntimeManager _runtimeManager;
    private readonly string _configDirectory;

    public RcloneEnvironment(IRcloneRuntimeManager runtimeManager, string configDirectory)
    {
        _runtimeManager = runtimeManager;
        _configDirectory = configDirectory;
    }

    public IRcloneRuntimeManager RuntimeManager => _runtimeManager;
    public string ConfigPath => Path.Combine(_configDirectory, "rclone.conf");

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var exePath = await _runtimeManager.EnsureInstalledAsync(cancellationToken);
        EnsureConfigFile();
        return exePath;
    }

    public void EnsureConfigFile()
    {
        Directory.CreateDirectory(_configDirectory);
        if (!File.Exists(ConfigPath))
            File.WriteAllText(ConfigPath, $"[{RemoteName}]\ntype = internetarchive\n");
    }

    public static string DetectRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }
}
