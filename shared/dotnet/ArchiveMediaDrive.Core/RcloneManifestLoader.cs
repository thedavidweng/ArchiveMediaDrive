using System.Text.Json;

namespace ArchiveMediaDrive.Core;

public static class RcloneManifestLoader
{
    public static RcloneManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<RcloneManifest>(json, ArchiveMediaDriveJson.Options)
            ?? throw new RcloneRuntimeException($"invalid rclone manifest: {path}");
        if (manifest.Assets.Count == 0)
            throw new RcloneRuntimeException("rclone manifest declares no assets");
        return manifest;
    }
}

public sealed class HttpAssetDownloader : IAssetDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpAssetDownloader(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken)
    {
        var uri = $"{_baseUrl}/{filename}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new RcloneRuntimeException($"download failed for {filename}: {(int)response.StatusCode} {response.StatusCode}");
        return await response.Content.ReadAsStreamAsync();
    }
}
