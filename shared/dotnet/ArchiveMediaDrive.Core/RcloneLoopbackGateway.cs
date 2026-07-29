namespace ArchiveMediaDrive.Core;

public sealed class RcloneLoopbackGateway : IRcloneGateway
{
    private readonly IRcloneRuntimeManager _runtime;

    public RcloneLoopbackGateway(IRcloneRuntimeManager runtime) => _runtime = runtime;

    public Task<IReadOnlyList<RawNode>> ListAsync(string identifier, string relativePath, CancellationToken cancellationToken)
        => throw new NotImplementedException("Implement operations/list via rclone rc --loopback as specified.");

    public Task<Uri> GetPublicLinkAsync(string identifier, string relativePath, CancellationToken cancellationToken)
        => throw new NotImplementedException("Implement operations/publiclink via rclone rc --loopback as specified.");

    public Task<RcloneProbe> ProbeAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException("Implement core/version probe as specified.");
}
