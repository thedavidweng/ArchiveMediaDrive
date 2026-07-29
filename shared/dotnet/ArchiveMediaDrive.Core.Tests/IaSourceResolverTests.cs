using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class IaSourceResolverTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<Uri> Calls { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request.RequestUri!);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = JsonContent.Create(body) };

    private static object SearchResponse(IEnumerable<string> identifiers, int numFound, int start) => new
    {
        responseHeader = new { status = 0 },
        response = new
        {
            numFound,
            start,
            docs = identifiers.Select(id => new { identifier = id }).ToArray(),
        },
    };

    [Fact]
    public async Task Item_source_returns_single_normalized_identifier_without_network()
    {
        var handler = new FakeHandler(_ => Json(SearchResponse(Array.Empty<string>(), 0, 0)));
        var resolver = new IaSourceResolver(new HttpClient(handler));

        var result = await resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Item, Value = "https://archive.org/details/TripDown1905" },
            CancellationToken.None);

        Assert.Equal(new[] { "TripDown1905" }, result);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Collection_pages_until_complete_preserving_order_and_deduplicating()
    {
        var page = 0;
        var handler = new FakeHandler(req =>
        {
            page++;
            var ids = page switch
            {
                1 => new[] { "alpha", "beta" },
                2 => new[] { "beta", "gamma" },
                _ => Array.Empty<string>(),
            };
            var start = (page - 1) * 2;
            return Json(SearchResponse(ids, numFound: 3, start));
        });
        var resolver = new IaSourceResolver(new HttpClient(handler));

        var result = await resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, result);
        Assert.True(handler.Calls.Count >= 2);
    }

    [Fact]
    public async Task Search_query_is_passed_through_verbatim()
    {
        var captured = string.Empty;
        var handler = new FakeHandler(req =>
        {
            captured = req.RequestUri!.Query;
            return Json(SearchResponse(new[] { "zeta" }, numFound: 1, start: 0));
        });
        var resolver = new IaSourceResolver(new HttpClient(handler));

        await resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Search, Value = "mediatype:movies AND collection:prelinger" },
            CancellationToken.None);

        Assert.Contains("mediatype%3Amovies", captured);
        Assert.Contains("collection%3Aprelinger", captured);
    }

    [Fact]
    public async Task Favorites_query_uses_fav_prefix()
    {
        var captured = string.Empty;
        var handler = new FakeHandler(req =>
        {
            captured = req.RequestUri!.Query;
            return Json(SearchResponse(new[] { "zeta" }, numFound: 1, start: 0));
        });
        var resolver = new IaSourceResolver(new HttpClient(handler));

        await resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Favorites, Value = "fav-david" },
            CancellationToken.None);

        Assert.Contains("collection%3Afav-david", captured);
    }

    [Fact]
    public async Task Cancellation_stops_pagination()
    {
        var page = 0;
        var handler = new FakeHandler(req =>
        {
            page++;
            if (page >= 2) throw new OperationCanceledException();
            return Json(SearchResponse(new[] { "alpha" }, numFound: 100, start: 0));
        });
        var resolver = new IaSourceResolver(new HttpClient(handler));

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Collection, Value = "prelinger" },
            CancellationToken.None));
    }

    [Fact]
    public async Task Retries_transient_503_then_succeeds()
    {
        var attempts = 0;
        var handler = new FakeHandler(req =>
        {
            attempts++;
            return attempts < 2
                ? Json(SearchResponse(Array.Empty<string>(), 0, 0), HttpStatusCode.ServiceUnavailable)
                : Json(SearchResponse(new[] { "alpha" }, numFound: 1, start: 0));
        });
        var resolver = new IaSourceResolver(new HttpClient(handler))
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(1),
        };

        var result = await resolver.ResolveAsync(
            new SourceDefinition { Id = "s", Name = "S", Kind = SourceKind.Search, Value = "x" },
            CancellationToken.None);

        Assert.Equal(new[] { "alpha" }, result);
        Assert.True(attempts >= 2);
    }
}
