using System.Reflection;
using System.Text.Json;

namespace ArchiveMediaDrive.Core;

public static class RcloneManifestLoader
{
    private const string ManifestResourceName = "ArchiveMediaDrive.Core.runtime.rclone.manifest.json";
    private const string LicenseResourceName = "ArchiveMediaDrive.Core.LICENSE";
    private const string ManifestRelativePath = "runtime/rclone/manifest.json";
    private const string LicenseRelativePath = "LICENSE";

    public static RcloneManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<RcloneManifest>(json, ArchiveMediaDriveJson.Options)
            ?? throw new RcloneRuntimeException($"invalid rclone manifest: {path}");
        manifest.Validate();
        return manifest;
    }

    public static RcloneManifest LoadFromPluginData(string pluginDataDirectory)
    {
        var manifestPath = Path.Combine(pluginDataDirectory, ManifestRelativePath);
        if (File.Exists(manifestPath))
        {
            try
            {
                return Load(manifestPath);
            }
            catch (RcloneRuntimeException)
            {
                // A corrupted on-disk copy is replaced by the embedded manifest.
            }
        }

        ExtractToPluginData(pluginDataDirectory);
        return Load(manifestPath);
    }

    private static void ExtractToPluginData(string pluginDataDirectory)
    {
        var manifestPath = Path.Combine(pluginDataDirectory, ManifestRelativePath);
        var licensePath = Path.Combine(pluginDataDirectory, LicenseRelativePath);
        var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(manifestDirectory);

        var assembly = typeof(RcloneManifestLoader).GetTypeInfo().Assembly;

        using var manifestStream = assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new RcloneRuntimeException("embedded rclone manifest is missing");
        var manifestJson = ReadAllText(manifestStream);
        var manifest = JsonSerializer.Deserialize<RcloneManifest>(manifestJson, ArchiveMediaDriveJson.Options)
            ?? throw new RcloneRuntimeException("embedded rclone manifest is invalid");
        manifest.Validate();

        using var licenseStream = assembly.GetManifestResourceStream(LicenseResourceName)
            ?? throw new RcloneRuntimeException("embedded license is missing");
        var licenseText = ReadAllText(licenseStream);

        var tempManifest = Path.Combine(manifestDirectory, $"manifest.json.new-{Guid.NewGuid():N}");
        var tempLicense = Path.Combine(manifestDirectory, $"LICENSE.new-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(tempManifest, manifestJson);
            File.WriteAllText(tempLicense, licenseText);

            ReplaceOrMove(tempManifest, manifestPath);
            ReplaceOrMove(tempLicense, licensePath);
        }
        finally
        {
            TryDelete(tempManifest);
            TryDelete(tempLicense);
        }
    }

    private static void ReplaceOrMove(string source, string destination)
    {
        if (File.Exists(destination))
        {
            var backup = destination + ".previous";
            TryDelete(backup);
            File.Replace(source, destination, backup);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static string ReadAllText(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
