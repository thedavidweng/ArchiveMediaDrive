using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class SourceSerializationTests
{
    private static readonly string ExpectedItemJson =
        "{\"schemaVersion\":1,\"id\":\"tripdown\",\"name\":\"Trip Down\",\"kind\":\"item\",\"value\":\"TripDown1905\",\"enabled\":true,\"refreshMinutes\":360,\"authenticationRef\":null}";

    [Fact]
    public void Item_source_serializes_to_schema_camel_case_with_lowercase_kind()
    {
        var source = new SourceDefinition
        {
            SchemaVersion = 1,
            Id = "tripdown",
            Name = "Trip Down",
            Kind = SourceKind.Item,
            Value = "TripDown1905",
            Enabled = true,
            RefreshMinutes = 360,
            AuthenticationRef = null,
        };

        var json = JsonSerializer.Serialize(source, ArchiveMediaDriveJson.Options);

        Assert.Equal(ExpectedItemJson, json);
    }

    [Fact]
    public void Collection_favorites_and_search_kinds_serialize_as_lowercase_strings()
    {
        foreach (var (kind, expected) in new[]
        {
            (SourceKind.Collection, "collection"),
            (SourceKind.Favorites, "favorites"),
            (SourceKind.Search, "search"),
            (SourceKind.Item, "item"),
        })
        {
            var source = new SourceDefinition { Id = "s", Name = "S", Kind = kind, Value = "v" };
            var json = JsonSerializer.Serialize(source, ArchiveMediaDriveJson.Options);
            Assert.Contains($"\"kind\":\"{expected}\"", json);
        }
    }

    [Fact]
    public void Source_round_trips_through_json_without_loss()
    {
        var original = new SourceDefinition
        {
            SchemaVersion = 1,
            Id = "prelinger",
            Name = "Prelinger",
            Kind = SourceKind.Collection,
            Value = "prelinger",
            Enabled = false,
            RefreshMinutes = 720,
            AuthenticationRef = "ia-creds",
        };

        var json = JsonSerializer.Serialize(original, ArchiveMediaDriveJson.Options);
        var roundTripped = JsonSerializer.Deserialize<SourceDefinition>(json, ArchiveMediaDriveJson.Options);

        Assert.Equal(original.Id, roundTripped!.Id);
        Assert.Equal(original.Kind, roundTripped.Kind);
        Assert.Equal(original.Enabled, roundTripped.Enabled);
        Assert.Equal(original.RefreshMinutes, roundTripped.RefreshMinutes);
        Assert.Equal(original.AuthenticationRef, roundTripped.AuthenticationRef);
    }

    [Fact]
    public void Deserializing_invalid_json_throws()
    {
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<SourceDefinition>("not json", ArchiveMediaDriveJson.Options));
    }

    [Theory]
    [InlineData(1)]
    public void Source_schema_version_round_trips(int version)
    {
        var source = new SourceDefinition
        {
            SchemaVersion = version,
            Id = "s",
            Name = "S",
            Kind = SourceKind.Item,
            Value = "x",
        };

        var json = JsonSerializer.Serialize(source, ArchiveMediaDriveJson.Options);

        Assert.Contains($"\"schemaVersion\":{version}", json);
    }
}
