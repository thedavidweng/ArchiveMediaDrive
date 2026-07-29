using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ArchiveMediaDrive.Core;

public interface IRcloneProcess
{
    Task<string> ExecuteAsync(string command, string jsonInput, CancellationToken cancellationToken);
}

public sealed class RcloneProcessException : Exception
{
    public RcloneProcessException(string message) : base(message) { }
}

public sealed class RcloneGatewayException : Exception
{
    public RcloneGatewayException(string message) : base(message) { }
    public RcloneGatewayException(string message, Exception inner) : base(message, inner) { }
}

public sealed class RcloneProcess : IRcloneProcess
{
    private readonly string _rcloneBinary;
    private readonly string _configPath;
    private readonly string _remoteName;
    private readonly string? _user;
    private readonly string? _password;

    public RcloneProcess(string rcloneBinary, string configPath, string remoteName, string? user = null, string? password = null)
    {
        _rcloneBinary = rcloneBinary;
        _configPath = configPath;
        _remoteName = remoteName;
        _user = user;
        _password = password;
    }

    public async Task<string> ExecuteAsync(string command, string jsonInput, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _rcloneBinary,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var args = new List<string>
        {
            "rc",
            "--loopback",
            "--config",
            _configPath,
            command,
            "--json",
            jsonInput,
        };
        psi.Arguments = BuildArguments(args);

        using var proc = Process.Start(psi)
            ?? throw new RcloneProcessException($"failed to start rclone: {_rcloneBinary}");

        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            while (!proc.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                proc.WaitForExit(100);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                var detail = stderr.Trim();
                if (string.IsNullOrEmpty(detail)) detail = stdout.Trim();
                throw new RcloneProcessException($"rclone exited with code {proc.ExitCode}: {detail}");
            }

            return stdout;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch { }
            throw;
        }
    }

    private static string BuildArguments(IList<string> args)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(QuoteArg(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg)
    {
        if (arg.Length == 0) return "''";
        if (!NeedsQuoting(arg)) return arg;
        var escaped = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private static bool NeedsQuoting(string arg)
    {
        foreach (var c in arg)
        {
            if (c == ' ' || c == '\t' || c == '"' || c == '\\' || c == '\'') return true;
        }
        return false;
    }
}

public sealed class RcloneLoopbackGateway : IRcloneGateway
{
    private const string RemotePrefix = "archive-media-drive-ia";
    private readonly IRcloneProcess _process;

    public RcloneLoopbackGateway(IRcloneProcess process) => _process = process;

    public RcloneLoopbackGateway(IRcloneRuntimeManager runtime, string configPath)
        : this(new RcloneProcess(runtime.ExecutablePath, configPath, RemotePrefix))
    {
    }

    public RcloneLoopbackGateway(IRcloneRuntimeManager runtime, string configPath, string? user, string? password)
        : this(new RcloneProcess(runtime.ExecutablePath, configPath, RemotePrefix, user, password))
    {
    }

    public async Task<IReadOnlyList<RawNode>> ListAsync(string identifier, string relativePath, CancellationToken cancellationToken)
    {
        ValidateIdentifier(identifier);
        ValidateRelativePath(relativePath);

        var input = JsonSerializer.Serialize(new
        {
            fs = $"{RemotePrefix}:{identifier}",
            remote = relativePath,
        });

        string output;
        try
        {
            output = await _process.ExecuteAsync("operations/list", input, cancellationToken);
        }
        catch (RcloneProcessException ex)
        {
            throw new RcloneGatewayException(ex.Message, ex);
        }

        var doc = JsonDocument.Parse(output);
        if (!doc.RootElement.TryGetProperty("list", out var list))
            return Array.Empty<RawNode>();

        var nodes = new List<RawNode>();
        foreach (var entry in list.EnumerateArray())
        {
            var name = entry.GetProperty("Name").GetString()!;
            var isDir = entry.GetProperty("IsDir").GetBoolean();
            var size = entry.TryGetProperty("Size", out var sizeEl) && sizeEl.TryGetInt64(out var s) ? s : 0;
            var format = entry.TryGetProperty("Formatted", out var fmtEl) ? fmtEl.GetString() : null;

            nodes.Add(new RawNode
            {
                Kind = isDir ? RawNodeKind.Directory : RawNodeKind.File,
                Name = name,
                Path = string.IsNullOrEmpty(relativePath) ? name : $"{relativePath}/{name}",
                Identifier = identifier,
                Size = isDir ? null : size,
                Format = format,
            });
        }

        return nodes;
    }

    public async Task<Uri> GetPublicLinkAsync(string identifier, string relativePath, CancellationToken cancellationToken)
    {
        ValidateIdentifier(identifier);
        ValidateRelativePath(relativePath);

        var input = JsonSerializer.Serialize(new
        {
            fs = $"{RemotePrefix}:{identifier}",
            remote = relativePath,
        });

        string output;
        try
        {
            output = await _process.ExecuteAsync("operations/publiclink", input, cancellationToken);
        }
        catch (RcloneProcessException ex)
        {
            throw new RcloneGatewayException(ex.Message, ex);
        }

        var doc = JsonDocument.Parse(output);
        string? link = null;
        if (doc.RootElement.TryGetProperty("url", out var urlEl)) link = urlEl.GetString();
        else if (doc.RootElement.TryGetProperty("link", out var linkEl)) link = linkEl.GetString();

        if (string.IsNullOrEmpty(link))
            throw new RcloneGatewayException("rclone publiclink response missing 'url' or 'link' field");

        link = NormalizeUrl(link!);
        return new Uri(link);
    }

    public async Task<RcloneProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        string output;
        try
        {
            output = await _process.ExecuteAsync("core/version", "{}", cancellationToken);
        }
        catch (RcloneProcessException ex)
        {
            throw new RcloneGatewayException(ex.Message, ex);
        }

        var doc = JsonDocument.Parse(output);
        return new RcloneProbe
        {
            Version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            Platform = doc.RootElement.TryGetProperty("os", out var o) ? o.GetString() ?? "" : "",
            Architecture = doc.RootElement.TryGetProperty("arch", out var a) ? a.GetString() ?? "" : "",
        };
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new RcloneGatewayException("identifier must not be empty");
        if (identifier.Contains("..", StringComparison.Ordinal))
            throw new RcloneGatewayException($"identifier contains path traversal: {identifier}");
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        if (relativePath.Contains("..", StringComparison.Ordinal))
            throw new RcloneGatewayException($"relative path contains traversal: {relativePath}");
        if (Path.IsPathRooted(relativePath))
            throw new RcloneGatewayException($"relative path must not be absolute: {relativePath}");
    }

    private static string NormalizeUrl(string url)
    {
        if (url.StartsWith("https:/", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
            return "https://" + url.Substring("https:/".Length);
        if (url.StartsWith("http:/", StringComparison.Ordinal) && !url.StartsWith("http://", StringComparison.Ordinal))
            return "http://" + url.Substring("http:/".Length);
        return url;
    }
}
