using System.Net;
using System.Text.Json;

namespace ArchiveMediaDrive.Core;

public sealed class IaSourceResolver : IIaSourceResolver
{
    private const string SearchEndpoint = "https://archive.org/advancedsearch.php";
    private const int PageSize = 1000;
    private const int MaxPagesSafety = 1000;

    private readonly HttpClient _httpClient;
    private readonly string _userAgent;

    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public IaSourceResolver(HttpClient httpClient) : this(httpClient, "ArchiveMediaDrive/0.1")
    {
    }

    public IaSourceResolver(HttpClient httpClient, string userAgent)
    {
        _httpClient = httpClient;
        _userAgent = userAgent;
    }

    public async Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken)
    {
        if (source.Kind == SourceKind.Item)
        {
            return new[] { SourceNormalizer.NormalizeValue(SourceKind.Item, source.Value) };
        }

        var query = BuildQuery(source);
        var identifiers = new List<string>();
        var seen = new HashSet<string>();

        for (var page = 1; page <= MaxPagesSafety; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = $"{SearchEndpoint}?q={Uri.EscapeDataString(query)}&fl[]=identifier&output=json&rows={PageSize}&page={page}";

            var doc = await SendWithRetryAsync(uri, cancellationToken);
            var response = doc.RootElement.GetProperty("response");
            var numFound = response.GetProperty("numFound").GetInt32();
            var docs = response.GetProperty("docs");

            foreach (var entry in docs.EnumerateArray())
            {
                var identifier = entry.GetProperty("identifier").GetString()!;
                if (seen.Add(identifier))
                    identifiers.Add(identifier);
            }

            if (identifiers.Count >= numFound || docs.GetArrayLength() == 0)
                break;
        }

        return identifiers;
    }

    private static string BuildQuery(SourceDefinition source)
    {
        return source.Kind switch
        {
            SourceKind.Collection => $"collection:{SourceNormalizer.NormalizeValue(SourceKind.Collection, source.Value)}",
            SourceKind.Favorites => $"collection:fav-{SourceNormalizer.NormalizeValue(SourceKind.Favorites, source.Value)}",
            SourceKind.Search => source.Value,
            _ => throw new SourceContractException($"unsupported source kind for search: {source.Kind}"),
        };
    }

    private async Task<JsonDocument> SendWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(RequestTimeout);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(_userAgent);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= MaxRetries) throw;
                await DelayAsync(RetryDelay, linkedCts.Token);
                continue;
            }

            TimeSpan delay = RetryDelay;
            try
            {
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    return await JsonDocument.ParseAsync(stream, cancellationToken: linkedCts.Token);
                }

                if (!IsTransient(response.StatusCode) || attempt >= MaxRetries)
                    throw new SourceContractException($"Internet Archive search failed: {(int)response.StatusCode} {response.StatusCode}");

                if (response.Headers.TryGetValues("Retry-After", out var retryAfter))
                {
                    var first = retryAfter.FirstOrDefault();
                    if (first != null && int.TryParse(first, out var seconds))
                        delay = TimeSpan.FromSeconds(seconds);
                }
            }
            finally
            {
                response.Dispose();
            }

            await DelayAsync(delay, linkedCts.Token);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout
            || statusCode == (HttpStatusCode)429
            || statusCode == HttpStatusCode.InternalServerError;

    private static readonly Random JitterRandom = new();

    private static async Task DelayAsync(TimeSpan baseDelay, CancellationToken cancellationToken)
    {
        int jitter;
        lock (JitterRandom) jitter = JitterRandom.Next(0, 250);
        var total = baseDelay + TimeSpan.FromMilliseconds(jitter);
        try
        {
            await Task.Delay(total, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
