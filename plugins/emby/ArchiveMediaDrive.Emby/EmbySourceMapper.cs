using System.Text.RegularExpressions;
using ArchiveMediaDrive.Core;

namespace ArchiveMediaDrive.Emby;

public static class EmbySourceMapper
{
    private static readonly Regex SafeId = new("^[a-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<SourceDefinition> Map(PluginOptions options)
    {
        var sources = new List<SourceDefinition>(options.Sources.Count);
        foreach (var obj in options.Sources)
        {
            if (obj is SourceOption s)
                sources.Add(Map(s));
        }
        return sources;
    }

    public static SourceDefinition Map(SourceOption option) => new()
    {
        Id = option.Id,
        Name = option.Name,
        Kind = option.Kind,
        Value = SourceNormalizer.NormalizeValue(option.Kind, option.Value),
        Enabled = option.Enabled,
        RefreshMinutes = option.RefreshMinutes,
    };

    public static bool TryValidate(PluginOptions options, out string error)
    {
        error = string.Empty;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in options.Sources)
        {
            if (obj is not SourceOption s)
                continue;

            if (string.IsNullOrWhiteSpace(s.Id))
            {
                error = "Every source must have an Id.";
                return false;
            }

            if (!SafeId.IsMatch(s.Id))
            {
                error = $"Source Id '{s.Id}' may only contain letters, numbers, dashes, and underscores.";
                return false;
            }

            if (ids.Contains(s.Id))
            {
                error = $"Duplicate source Id '{s.Id}'.";
                return false;
            }

            ids.Add(s.Id);

            if (string.IsNullOrWhiteSpace(s.Name))
            {
                error = $"Source '{s.Id}' must have a Name.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(s.Value))
            {
                error = $"Source '{s.Id}' must have a Value.";
                return false;
            }

            if (s.RefreshMinutes < 1)
            {
                error = $"Source '{s.Id}' refresh interval must be at least 1 minute.";
                return false;
            }
        }

        if (options.ManagedLibraryEnabled && string.IsNullOrWhiteSpace(options.ManagedLibraryName))
        {
            error = "Managed Library name is required when Managed Library is enabled.";
            return false;
        }

        return true;
    }
}
