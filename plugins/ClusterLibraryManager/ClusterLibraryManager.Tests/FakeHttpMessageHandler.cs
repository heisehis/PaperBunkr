using System.Net;

namespace ClusterLibraryManager.Tests;

/// <summary>Records every requested URL and returns canned responses in order (or repeats the last
/// one once exhausted) - no live network call, per this project's own testing conventions.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses;

    public List<string> RequestedUrls { get; } = new();

    public FakeHttpMessageHandler(params string[] jsonResponses)
    {
        _responses = new Queue<string>(jsonResponses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        string body = _responses.Count > 0 ? _responses.Dequeue() : _responses.Count == 0 ? "{}" : _responses.Peek();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };
        return Task.FromResult(response);
    }
}
