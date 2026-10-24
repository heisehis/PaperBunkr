namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Paces image downloads from ComicVine's own site (reported 2026-10-21: ComicVine scraping "hangs"). Its covers live on comicvine.gamespot.com, the same
/// site as the API, and ComicVine answers a burst from one address with HTTP 420 "slow down" - after which the API limiter
/// (<see cref="ComicVineRateLimitHandler"/>) pauses every ComicVine call for 1, 5, 15, then 60 minutes. Cover checks fetched several candidate covers
/// at once with no pause between comics, outside that limiter. Here they go one at a time, at the API's own spacing, and not at all while the API is
/// paused. They are not counted against the API's hourly budget: covers are not API calls.
/// </summary>
public static class ComicVineImagePacer
{
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(1100);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>Test seam for the wait.</summary>
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    /// <summary>True for an image ComicVine serves itself (and so counts toward its "slow down" protection).</summary>
    public static bool IsComicVineImage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.EndsWith("comicvine.gamespot.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs <paramref name="download"/> when it is this image's turn: after any API pause, and at least <see cref="Spacing"/> after the last one.</summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> download, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ComicVineHttp.Handler.PausedUntil is { } pausedUntil && pausedUntil > DateTimeOffset.UtcNow)
            {
                await Delay(pausedUntil - DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            }

            var wait = _last + Spacing - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await download().ConfigureAwait(false);
            }
            finally
            {
                _last = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
