using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RawNodeSerializationTests
{
    [Fact]
    public void File_node_serializes_to_schema_camel_case_with_all_fields()
    {
        var node = new RawNode
        {
            SchemaVersion = 1,
            Id = "prelinger/item/file.mkv",
            ParentId = "prelinger/item",
            Kind = RawNodeKind.File,
            Name = "file.mkv",
            Path = "item/file.mkv",
            SourceId = "prelinger",
            Identifier = "item",
            Size = 12345,
            Format = "mkv",
            IaSource = "original",
            Revision = "rev1",
            PublicUrl = new Uri("https://archive.org/download/item/file.mkv"),
        };

        var expected =
            "{\"schemaVersion\":1,\"id\":\"prelinger/item/file.mkv\",\"parentId\":\"prelinger/item\","
            + "\"kind\":\"file\",\"name\":\"file.mkv\",\"path\":\"item/file.mkv\",\"sourceId\":\"prelinger\","
            + "\"identifier\":\"item\",\"size\":12345,\"format\":\"mkv\",\"iaSource\":\"original\","
            + "\"revision\":\"rev1\",\"publicUrl\":\"https://archive.org/download/item/file.mkv\"}";

        var json = JsonSerializer.Serialize(node, ArchiveMediaDriveJson.Options);

        Assert.Equal(expected, json);
    }

    [Fact]
    public void Root_node_serializes_with_null_optionals_and_directory_kind()
    {
        var node = new RawNode
        {
            SchemaVersion = 1,
            Id = "prelinger",
            ParentId = null,
            Kind = RawNodeKind.Source,
            Name = "Prelinger",
            Path = "",
            SourceId = "prelinger",
            Identifier = null,
            Size = null,
            Format = null,
            IaSource = null,
            Revision = null,
            PublicUrl = null,
        };

        var json = JsonSerializer.Serialize(node, ArchiveMediaDriveJson.Options);

        Assert.Contains("\"kind\":\"source\"", json);
        Assert.Contains("\"parentId\":null", json);
        Assert.Contains("\"publicUrl\":null", json);
        Assert.Contains("\"size\":null", json);
    }
}
