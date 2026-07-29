using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class SourceNormalizationTests
{
    [Theory]
    [InlineData(SourceKind.Item, "https://archive.org/details/TripDown1905", "TripDown1905")]
    [InlineData(SourceKind.Item, "TripDown1905", "TripDown1905")]
    [InlineData(SourceKind.Collection, "https://archive.org/details/prelinger", "prelinger")]
    [InlineData(SourceKind.Collection, "prelinger", "prelinger")]
    [InlineData(SourceKind.Favorites, "fav-david", "david")]
    [InlineData(SourceKind.Favorites, "david", "david")]
    [InlineData(SourceKind.Search, "mediatype:movies AND collection:prelinger", "mediatype:movies AND collection:prelinger")]
    public void Normalizes_values_per_kind(SourceKind kind, string input, string expected)
    {
        Assert.Equal(expected, SourceNormalizer.NormalizeValue(kind, input));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("space inside")]
    public void Rejects_invalid_identifiers(string value)
    {
        Assert.Throws<SourceContractException>(() => SourceNormalizer.NormalizeValue(SourceKind.Item, value));
    }

    [Fact]
    public void Rejects_unsupported_url_host()
    {
        Assert.Throws<SourceContractException>(
            () => SourceNormalizer.NormalizeValue(SourceKind.Item, "https://example.com/details/x"));
    }
}
