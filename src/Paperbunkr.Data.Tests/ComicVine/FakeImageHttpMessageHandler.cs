using System.Net;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Returns the same canned image bytes for every request, regardless of URL - stands in for
/// <see cref="Paperbunkr.Data.ComicVine.Scraping.ScrapeOrchestrator.CoverHttp"/> (a static test seam)
/// so the cover-hash auto-match gate never needs a live network call.</summary>
internal sealed class FakeImageHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<string, byte[]> _bytesFor;

    public FakeImageHttpMessageHandler(byte[] bytes)
    {
        _bytesFor = _ => bytes;
    }

    /// <summary>Different bytes per requested URL - for a test where a volume's series art and one issue's own cover must differ.</summary>
    public FakeImageHttpMessageHandler(Func<string, byte[]> bytesFor)
    {
        _bytesFor = bytesFor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_bytesFor(request.RequestUri!.ToString())),
        };
        return Task.FromResult(response);
    }
}
