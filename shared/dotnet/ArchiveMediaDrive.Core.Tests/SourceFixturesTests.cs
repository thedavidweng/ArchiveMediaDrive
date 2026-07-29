using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class SourceFixturesTests
{
    private static readonly string FixturesPath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "sources.json");

    private sealed record FixtureSource(
        string Id,
        string Name,
        string Kind,
        string Value,
        bool Enabled,
        int RefreshMinutes,
        string? AuthenticationRef);

    private sealed record FixtureFile(int SchemaVersion, List<FixtureSource> Sources);

    private static FixtureFile Load()
    {
        var json = File.ReadAllText(FixturesPath);
        return JsonSerializer.Deserialize<FixtureFile>(json, ArchiveMediaDriveJson.Options)!;
    }

    [Fact]
    public void All_fixture_sources_normalize_to_stable_ids_and_lowercase_kinds()
    {
        var fixtures = Load();
        Assert.NotEmpty(fixtures.Sources);

        foreach (var fixture in fixtures.Sources)
        {
            var kind = Enum.Parse<SourceKind>(fixture.Kind, ignoreCase: true);
            var normalizedValue = SourceNormalizer.NormalizeValue(kind, fixture.Value);

            var source = new SourceDefinition
            {
                Id = fixture.Id,
                Name = fixture.Name,
                Kind = kind,
                Value = normalizedValue,
                Enabled = fixture.Enabled,
                RefreshMinutes = fixture.RefreshMinutes,
                AuthenticationRef = fixture.AuthenticationRef,
            };

            var json = JsonSerializer.Serialize(source, ArchiveMediaDriveJson.Options);
            var roundTripped = JsonSerializer.Deserialize<SourceDefinition>(json, ArchiveMediaDriveJson.Options)!;

            Assert.Equal(fixture.Id, roundTripped.Id);
            Assert.Equal(normalizedValue, roundTripped.Value);
            Assert.Equal(fixture.Kind.ToLowerInvariant(), json.Split("\"kind\":\"")[1].Split('"')[0]);
        }
    }

    [Fact]
    public void Item_and_collection_url_fixtures_collapse_to_identifiers()
    {
        var fixtures = Load();
        var byId = fixtures.Sources.ToDictionary(s => s.Id);

        Assert.Equal("TripDown1905", SourceNormalizer.NormalizeValue(SourceKind.Item, byId["tripdown"].Value));
        Assert.Equal("prelinger", SourceNormalizer.NormalizeValue(SourceKind.Collection, byId["prelinger-url"].Value));
        Assert.Equal("david", SourceNormalizer.NormalizeValue(SourceKind.Favorites, byId["david-favs"].Value));
    }
}
