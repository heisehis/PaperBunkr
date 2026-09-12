namespace ClusterLibraryManager.ComicVine;

/// <summary>Thrown when a ComicVine API call fails or returns a non-success `status_code` (CE's own
/// gate, verified: <c>dom.status_code == 1</c> means success, anything else is an error).</summary>
public sealed class ComicVineException : Exception
{
    public int StatusCode { get; }

    public ComicVineException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
