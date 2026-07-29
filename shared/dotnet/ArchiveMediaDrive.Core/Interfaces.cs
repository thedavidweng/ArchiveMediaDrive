namespace ArchiveMediaDrive.Core;

public interface IIaSourceResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken);
}

public interface IRcloneGateway
{
    Task<IReadOnlyList<RawNode>> ListAsync(string identifier, string relativePath, CancellationToken cancellationToken);
    Task<Uri> GetPublicLinkAsync(string identifier, string relativePath, CancellationToken cancellationToken);
    Task<RcloneProbe> ProbeAsync(CancellationToken cancellationToken);
}

public interface IRcloneRuntimeManager
{
    Task<string> EnsureInstalledAsync(CancellationToken cancellationToken);
    Task VerifyAsync(CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}
