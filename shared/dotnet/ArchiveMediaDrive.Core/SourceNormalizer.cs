using System.Text.RegularExpressions;

namespace ArchiveMediaDrive.Core;

public static class SourceNormalizer
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "archive.org",
        "www.archive.org",
    };

    public static string NormalizeValue(SourceKind kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SourceContractException("source value must not be empty");

        return kind switch
        {
            SourceKind.Item => NormalizeIdentifier(value),
            SourceKind.Collection => NormalizeIdentifier(value),
            SourceKind.Favorites => NormalizeFavoritesOwner(value),
            SourceKind.Search => value.Trim(),
            _ => throw new SourceContractException($"unsupported source kind: {kind}"),
        };
    }

    public static string NormalizeIdentifier(string value)
    {
        var identifier = IdentifierFromDetailsUrl(value);
        if (!IdentifierPattern.IsMatch(identifier))
            throw new SourceContractException($"invalid Internet Archive identifier: {identifier}");
        return identifier;
    }

    private static string NormalizeFavoritesOwner(string value)
    {
        var owner = IdentifierFromDetailsUrl(value);
        if (owner.StartsWith("fav-", StringComparison.Ordinal))
            owner = owner.Substring(4);
        if (!IdentifierPattern.IsMatch(owner))
            throw new SourceContractException($"invalid favorites owner: {owner}");
        return owner;
    }

    private static string IdentifierFromDetailsUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            if (!AllowedHosts.Contains(uri.Host))
                throw new SourceContractException($"unsupported URL host: {uri.Host}");
            var segments = uri.Segments.Where(s => s != "/").Select(Uri.UnescapeDataString).ToArray();
            if (segments.Length < 2 || segments[0] != "details/")
                throw new SourceContractException("Internet Archive URLs must use /details/<identifier>");
            return segments[1].TrimEnd('/');
        }

        return value.Trim();
    }
}

public sealed class SourceContractException : Exception
{
    public SourceContractException(string message) : base(message) { }
}
