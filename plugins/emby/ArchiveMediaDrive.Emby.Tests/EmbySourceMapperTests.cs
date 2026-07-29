using ArchiveMediaDrive.Core;
using Emby.Web.GenericEdit;
using Xunit;

namespace ArchiveMediaDrive.Emby.Tests;

public sealed class EmbySourceMapperTests
{
    [Fact]
    public void Map_CollectionSource_UsesNormalizedValue()
    {
        var options = new PluginOptions
        {
            Sources = new EditableObjectCollection
            {
                new SourceOption
                {
                    Id = "prelinger",
                    Name = "Prelinger",
                    Kind = SourceKind.Collection,
                    Value = "prelinger",
                    Enabled = true,
                    RefreshMinutes = 720,
                },
            },
        };

        var sources = EmbySourceMapper.Map(options);

        Assert.Single(sources);
        Assert.Equal("prelinger", sources[0].Id);
        Assert.Equal("Prelinger", sources[0].Name);
        Assert.Equal(SourceKind.Collection, sources[0].Kind);
        Assert.Equal("prelinger", sources[0].Value);
        Assert.True(sources[0].Enabled);
        Assert.Equal(720, sources[0].RefreshMinutes);
    }

    [Fact]
    public void TryValidate_EmptyId_ReturnsFalse()
    {
        var options = new PluginOptions
        {
            Sources = new EditableObjectCollection
            {
                new SourceOption
                {
                    Id = "",
                    Name = "No id",
                    Kind = SourceKind.Item,
                    Value = "foo",
                },
            },
        };

        var result = EmbySourceMapper.TryValidate(options, out var error);

        Assert.False(result);
        Assert.Contains("Id", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_InvalidId_ReturnsFalse()
    {
        var options = new PluginOptions
        {
            Sources = new EditableObjectCollection
            {
                new SourceOption
                {
                    Id = "bad id!",
                    Name = "Bad",
                    Kind = SourceKind.Item,
                    Value = "foo",
                },
            },
        };

        var result = EmbySourceMapper.TryValidate(options, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryValidate_DuplicateIds_ReturnsFalse()
    {
        var options = new PluginOptions
        {
            Sources = new EditableObjectCollection
            {
                new SourceOption { Id = "same", Name = "A", Kind = SourceKind.Item, Value = "a" },
                new SourceOption { Id = "same", Name = "B", Kind = SourceKind.Item, Value = "b" },
            },
        };

        var result = EmbySourceMapper.TryValidate(options, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryValidate_ManagedLibraryWithEmptyName_ReturnsFalse()
    {
        var options = new PluginOptions
        {
            ManagedLibraryEnabled = true,
            ManagedLibraryName = "",
        };

        var result = EmbySourceMapper.TryValidate(options, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryValidate_ValidSources_ReturnsTrue()
    {
        var options = new PluginOptions
        {
            ManagedLibraryEnabled = true,
            ManagedLibraryName = "Internet Archive",
            Sources = new EditableObjectCollection
            {
                new SourceOption
                {
                    Id = "prelinger",
                    Name = "Prelinger",
                    Kind = SourceKind.Collection,
                    Value = "prelinger",
                    Enabled = true,
                    RefreshMinutes = 720,
                },
            },
        };

        var result = EmbySourceMapper.TryValidate(options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
    }
}
