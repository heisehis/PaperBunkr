using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.ReadingLists;

/// <summary>
/// Metron's per-minute throttle answers 429 "Request was throttled. Expected available in 19 seconds." - reported 2026-10-29 as the error that
/// stopped "create a reading list from Metron". It is a wait, not a failure.
/// </summary>
[Collection("MetronSourceStatics")]
public sealed class MetronSourceThrottleTests : IDisposable
{
    private readonly List<TimeSpan> _waits = new();

    public MetronSourceThrottleTests()
    {
        MetronSource.Delay = (t, _) => { _waits.Add(t); return Task.CompletedTask; };
    }

    public void Dispose() => MetronSource.Delay = (t, ct) => Task.Delay(t, ct);

    private sealed class Scripted(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private int _next;
        public int Calls => _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = replies[Math.Min(_next++, replies.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private const string Throttled = """{"detail":"Request was throttled. Expected available in 19 seconds."}""";

    [Fact]
    public async Task AThrottledSearch_WaitsAsAskedAndTriesAgain()
    {
        var http = new Scripted((HttpStatusCode.TooManyRequests, Throttled), (HttpStatusCode.OK, """{"results":[{"id":5,"name":"Dark Reign"}]}"""));
        var source = new MetronSource(MetronAuth.Token("t"), new HttpClient(http));

        var found = await source.SearchAsync("Dark reign", CancellationToken.None);

        Assert.Equal("Dark Reign", Assert.Single(found).Name);
        Assert.Equal(2, http.Calls);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(_waits));
    }

    [Fact]
    public async Task ThrottledEveryTime_GivesUpAfterTwoRetries()
    {
        var http = new Scripted((HttpStatusCode.TooManyRequests, Throttled));
        var source = new MetronSource(MetronAuth.Token("t"), new HttpClient(http));

        await Assert.ThrowsAsync<ReadingListSourceException>(() => source.SearchAsync("Dark reign", CancellationToken.None));
        Assert.Equal(3, http.Calls);
    }

    [Fact]
    public async Task ADailyLimitWait_IsReportedInsteadOfWaitedOut()
    {
        var http = new Scripted((HttpStatusCode.TooManyRequests, """{"detail":"Request was throttled. Expected available in 41000 seconds."}"""));
        var source = new MetronSource(MetronAuth.Token("t"), new HttpClient(http));

        var ex = await Assert.ThrowsAsync<ReadingListSourceException>(() => source.SearchAsync("Dark reign", CancellationToken.None));
        Assert.Contains("daily", ex.Message);
        Assert.Empty(_waits);
    }
}
