using System.Net.Http;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// The one process-wide ComicVine <see cref="HttpClient"/>. Everything that talks to ComicVine
/// (<c>ComicVineSource</c>, the acquisition daemon's <c>ComicVineClient</c>) shares it, so the rate limit
/// in <see cref="ComicVineRateLimitHandler"/> is genuinely global rather than per-instance.
/// </summary>
public static class ComicVineHttp
{
    public static ComicVineRateLimitHandler Handler { get; } = new(new HttpClientHandler());

    /// <summary>
    /// Timeout is infinite on purpose: the handler enforces its own per-request timeout on the actual
    /// send, so time spent queued behind the rate limit never counts against a request.
    /// </summary>
    public static HttpClient Client { get; } = CreateClient();

    /// <summary>Marks a request as background (daemon) work so it yields to foreground requests.</summary>
    public static HttpRequestMessage Get(string url, ComicVineRequestPriority priority = ComicVineRequestPriority.High)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(ComicVineRateLimitHandler.PriorityKey, priority);
        return request;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(Handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        // ComicVine rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/0.1 (comic library manager)");
        return client;
    }
}
