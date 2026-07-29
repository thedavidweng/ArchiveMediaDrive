namespace ArchiveMediaDrive.Core;

public sealed class IaSourceResolver : IIaSourceResolver
{
    private readonly HttpClient _httpClient;

    public IaSourceResolver(HttpClient httpClient) => _httpClient = httpClient;

    public Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken)
        => throw new NotImplementedException("Implement fixture-compatible Internet Archive source resolution as specified.");
}
