using System.Globalization;
using System.Net.Http.Headers;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Tracks Metron's daily ("sustained") quota from the headers every response carries (<c>X-RateLimit-Sustained-Limit/-Remaining/-Reset</c>; the reset is a Unix timestamp
/// in seconds; the limit differs per account, so nothing here hardcodes 5,000). The minute-long burst limit is already enforced by <see cref="MetronHttp"/>'s handler; this
/// covers the day. Background work stops while the remaining count is at or below a reserve, so an interactive search or a Request is never the one that finds the day used up.
/// </summary>
public static class MetronQuota
{
    /// <summary>The share of the daily limit background work leaves untouched (never less than <see cref="MinimumReserve"/> requests).</summary>
    public const double ReserveFraction = 0.10;

    public const int MinimumReserve = 50;

    private static readonly object Gate = new();
    private static int? _limit;
    private static int? _remaining;
    private static DateTimeOffset _reset;

    /// <summary>Test seam for "now".</summary>
    internal static Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Reads the sustained counters from a response (also present on a 429). Missing or unreadable headers leave the last known values alone.</summary>
    public static void Observe(HttpResponseHeaders headers)
    {
        if (!TryGetLong(headers, "X-RateLimit-Sustained-Remaining", out long remaining) || !TryGetLong(headers, "X-RateLimit-Sustained-Reset", out long reset))
        {
            return;
        }

        lock (Gate)
        {
            _remaining = (int)Math.Clamp(remaining, 0, int.MaxValue);
            _reset = DateTimeOffset.FromUnixTimeSeconds(reset);
            if (TryGetLong(headers, "X-RateLimit-Sustained-Limit", out long limit))
            {
                _limit = (int)Math.Clamp(limit, 0, int.MaxValue);
            }
        }
    }

    /// <summary>
    /// True while background (low priority) requests should wait, with how long until the day's counter resets. Only ever true when the last response said the
    /// counter is at or under the reserve and that window has not yet reset.
    /// </summary>
    public static bool BackgroundShouldWait(out TimeSpan untilReset)
    {
        lock (Gate)
        {
            untilReset = TimeSpan.Zero;
            var now = Clock();
            if (_remaining is not int remaining || _reset <= now)
            {
                return false;                    // nothing known, or the window has rolled over since
            }

            int reserve = Math.Max(MinimumReserve, (int)Math.Ceiling((_limit ?? 5000) * ReserveFraction));
            if (remaining > reserve)
            {
                return false;
            }

            untilReset = _reset - now;
            return true;
        }
    }

    /// <summary>Forgets what was seen (tests, and a changed login).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _limit = null;
            _remaining = null;
            _reset = default;
        }
    }

    private static bool TryGetLong(HttpResponseHeaders headers, string name, out long value)
    {
        value = 0;
        return headers.TryGetValues(name, out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
