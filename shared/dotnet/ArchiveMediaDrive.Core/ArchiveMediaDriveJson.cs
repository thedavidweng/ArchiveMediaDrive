using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchiveMediaDrive.Core;

public static class ArchiveMediaDriveJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        Converters =
        {
            new LowercaseEnumConverter<SourceKind>(),
            new LowercaseEnumConverter<RawNodeKind>(),
        },
    };
}

internal sealed class LowercaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is null
            ? default
            : (T)Enum.Parse(typeof(T), value, ignoreCase: true);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}
