using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class CrossPlatformAcceptanceTests
{
    [Fact]
    public void Dotnet_and_kodi_produce_byte_identical_json_for_item_source()
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
        var expected = "{\"schemaVersion\":1,\"id\":\"tripdown\",\"name\":\"Trip Down\",\"kind\":\"item\",\"value\":\"TripDown1905\",\"enabled\":true,\"refreshMinutes\":360,\"authenticationRef\":null}";
        Assert.Equal(expected, json);
    }
}
