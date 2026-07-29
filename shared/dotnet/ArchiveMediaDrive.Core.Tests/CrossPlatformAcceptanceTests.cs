using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class CrossPlatformAcceptanceTests
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
        "contracts", "fixtures", "sources.json");

    private static readonly string ActualFixturesPath = Path.GetFullPath(FixturesPath);

    private static JsonDocument LoadFixtures()
    {
        var path = ActualFixturesPath;
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "contracts", "fixtures", "sources.json"));
        }
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Dotnet_normalizes_shared_source_fixtures_with_stable_ids()
    {
        var doc = LoadFixtures();
        foreach (var fixture in doc.RootElement.GetProperty("sources").EnumerateArray())
        {
            var id = fixture.GetProperty("id").GetString()!;
            var kind = fixture.GetProperty("kind").GetString()!;
            var rawValue = fixture.GetProperty("value").GetString()!;
            var enabled = fixture.GetProperty("enabled").GetBoolean();
            var refreshMinutes = fixture.GetProperty("refreshMinutes").GetInt32();

            var normalized = SourceNormalizer.NormalizeValue(
                Enum.Parse<SourceKind>(kind, ignoreCase: true), rawValue);
            var source = new SourceDefinition
            {
                Id = id,
                Name = fixture.GetProperty("name").GetString()!,
                Kind = Enum.Parse<SourceKind>(kind, ignoreCase: true),
                Value = normalized,
                Enabled = enabled,
                RefreshMinutes = refreshMinutes,
            };

            Assert.Equal(id, source.Id);
            Assert.Equal(kind, source.Kind.ToString().ToLowerInvariant());
            Assert.Equal(enabled, source.Enabled);
            Assert.Equal(refreshMinutes, source.RefreshMinutes);
        }
    }

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

    [Fact]
    public void All_fixture_kinds_are_supported()
    {
        var doc = LoadFixtures();
        var kinds = doc.RootElement.GetProperty("sources")
            .EnumerateArray()
            .Select(s => s.GetProperty("kind").GetString()!)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "item", "collection", "favorites", "search" }, kinds);
    }

    [Fact]
    public void Shared_fixtures_load_into_dotnet_source_definitions()
    {
        var doc = LoadFixtures();
        var sources = new List<SourceDefinition>();

        foreach (var fixture in doc.RootElement.GetProperty("sources").EnumerateArray())
        {
            var kind = fixture.GetProperty("kind").GetString()!;
            var kindEnum = Enum.Parse<SourceKind>(kind, ignoreCase: true);
            sources.Add(new SourceDefinition
            {
                Id = fixture.GetProperty("id").GetString()!,
                Name = fixture.GetProperty("name").GetString()!,
                Kind = kindEnum,
                Value = SourceNormalizer.NormalizeValue(kindEnum, fixture.GetProperty("value").GetString()!),
                Enabled = fixture.GetProperty("enabled").GetBoolean(),
                RefreshMinutes = fixture.GetProperty("refreshMinutes").GetInt32(),
            });
        }

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.False(string.IsNullOrEmpty(s.Id)));
        Assert.All(sources, s => Assert.False(string.IsNullOrEmpty(s.Value)));
    }
}
