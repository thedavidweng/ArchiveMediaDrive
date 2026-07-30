using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchiveMediaDrive.Core;

public sealed class DiagnosticsPackageContext
{
    public IReadOnlyList<SourceDefinition> Sources { get; init; } = Array.Empty<SourceDefinition>();
    public RcloneReceipt? Receipt { get; init; }
    public RcloneProbe? Probe { get; init; }
    public bool MountRunning { get; init; }
    public string? MountPath { get; init; }
    public long CacheUsageBytes { get; init; }
    public DateTimeOffset? LastRefresh { get; init; }
    public int SourceCount { get; init; }
    public int ItemCount { get; init; }
    public string? LastError { get; init; }
    public IReadOnlyList<SourceSnapshot> SourceSummaries { get; init; } = Array.Empty<SourceSnapshot>();
    public string PluginVersion { get; init; } = string.Empty;
    public string HostVersion { get; init; } = string.Empty;
    public IReadOnlyList<string>? RecentLogs { get; init; }
}

public interface IDiagnosticsPackageBuilder
{
    Task BuildAsync(DiagnosticsPackageContext context, Stream output, CancellationToken cancellationToken);
}

public sealed class DiagnosticsPackageBuilder : IDiagnosticsPackageBuilder
{
    private static readonly Regex SecretPropertyPattern = new("(api.?key|token|password|secret|auth|authenticationRef)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecretLogPattern = new("(token|api.?key|password|secret)=[^&\\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task BuildAsync(DiagnosticsPackageContext context, Stream output, CancellationToken cancellationToken)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (output is null)
            throw new ArgumentNullException(nameof(output));

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var configJson = JsonSerializer.Serialize(context.Sources, ArchiveMediaDriveJson.Options);
        await AddJsonEntryAsync(zip, "config.json", configJson, cancellationToken);

        var receiptJson = context.Receipt is not null
            ? JsonSerializer.Serialize(context.Receipt, ArchiveMediaDriveJson.Options)
            : "{}";
        await AddJsonEntryAsync(zip, "receipt.json", receiptJson, cancellationToken);

        var rcloneInfo = new
        {
            version = context.Probe?.Version ?? string.Empty,
            platform = context.Probe?.Platform ?? string.Empty,
            architecture = context.Probe?.Architecture ?? string.Empty,
            executableHash = context.Receipt?.ExecutableSha256 ?? string.Empty,
        };
        await AddJsonEntryAsync(zip, "rclone.json", JsonSerializer.Serialize(rcloneInfo, ArchiveMediaDriveJson.Options), cancellationToken);

        var status = new
        {
            pluginVersion = context.PluginVersion,
            hostVersion = context.HostVersion,
            runtimeStatus = GetRuntimeStatus(context),
            rcloneVersion = context.Probe?.Version ?? string.Empty,
            rcloneHash = context.Receipt?.ExecutableSha256 ?? string.Empty,
            mountStatus = context.MountRunning ? "running" : "stopped",
            mountPath = context.MountPath ?? string.Empty,
            cacheUsageBytes = context.CacheUsageBytes,
            lastRefresh = context.LastRefresh,
            sourceCount = context.SourceCount,
            itemCount = context.ItemCount,
            lastError = context.LastError ?? string.Empty,
        };
        await AddJsonEntryAsync(zip, "status.json", JsonSerializer.Serialize(status, ArchiveMediaDriveJson.Options), cancellationToken);

        var summary = JsonSerializer.Serialize(context.SourceSummaries, ArchiveMediaDriveJson.Options);
        await AddJsonEntryAsync(zip, "source-summary.json", summary, cancellationToken);

        if (context.RecentLogs is not null)
            await AddTextEntryAsync(zip, "logs.txt", RedactLogs(context.RecentLogs), cancellationToken);
    }

    private static string GetRuntimeStatus(DiagnosticsPackageContext context)
    {
        if (context.Probe is null || string.IsNullOrWhiteSpace(context.Probe.Version))
            return "not available";

        return "ok";
    }

    private static async Task AddJsonEntryAsync(ZipArchive zip, string name, string json, CancellationToken cancellationToken)
    {
        var redacted = RedactJson(json);
        await AddTextEntryAsync(zip, name, redacted, cancellationToken);
    }

    private static async Task AddTextEntryAsync(ZipArchive zip, string name, string content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }

    private static string RedactJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        WriteRedacted(doc.RootElement, writer, false, null);
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer, bool isSecretValue, string? propertyName)
    {
        if (isSecretValue)
        {
            writer.WriteStringValue("<redacted>");
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    var secret = IsSecretProperty(property.Name);
                    writer.WritePropertyName(property.Name);
                    WriteRedacted(property.Value, writer, secret, property.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedacted(item, writer, false, propertyName);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                    writer.WriteNumberValue(longValue);
                else if (element.TryGetDouble(out var doubleValue))
                    writer.WriteNumberValue(doubleValue);
                else
                    writer.WriteStringValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteStringValue(element.GetRawText());
                break;
        }
    }

    private static bool IsSecretProperty(string name)
    {
        return SecretPropertyPattern.IsMatch(name);
    }

    private static string RedactLogs(IReadOnlyList<string> logs)
    {
        if (logs.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var line in logs)
        {
            var redacted = SecretLogPattern.Replace(line, "$1=<redacted>");
            sb.AppendLine(redacted);
        }
        return sb.ToString();
    }
}
