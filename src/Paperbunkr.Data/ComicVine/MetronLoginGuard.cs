namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Keeps Paperbunkr from getting the user's address blocked by Metron. Metron's published firewall rules
/// (<c>fail2ban/jail.d/metron.conf</c> in Metron-Project/metron, read 2026-10-05) drop every connection from an
/// address for <b>24 hours</b> after three HTTP 401s within five minutes, and after ten 403s. A wrong or expired
/// login makes every Metron request a 401, and the app makes them from several places at once - a scrape, the
/// hourly account sync, the weekly list, the followed-series refresh, a reading-list search - so one bad password
/// was enough to trip it.
///
/// The rule here is stricter than Metron's so it can't be reached: a login that was just rejected is not sent again
/// for <see cref="SameLoginPause"/>, and once two rejections fall inside <see cref="Window"/> nothing is sent at
/// all, with any login, until the older one has aged out. That is at most two rejections in any six minutes.
/// Process-wide on purpose: the acquisition services run in this same process.
/// </summary>
public static class MetronLoginGuard
{
    public static readonly TimeSpan SameLoginPause = TimeSpan.FromMinutes(10);

    /// <summary>A minute wider than Metron's own five, for clocks and for requests already on their way.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(6);

    private static readonly object Gate = new();
    private static readonly List<(DateTime At, string Login)> Rejections = new();

    /// <summary>Test seam for "now".</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>True when a request with this login must not be sent, with how long until one may be.</summary>
    public static bool ShouldHold(string login, out TimeSpan wait)
    {
        lock (Gate)
        {
            var now = Clock();
            Rejections.RemoveAll(r => now - r.At >= SameLoginPause);
            wait = TimeSpan.Zero;

            foreach (var rejection in Rejections.Where(r => r.Login == login))
            {
                wait = Max(wait, rejection.At + SameLoginPause - now);
            }

            var recent = Rejections.Where(r => now - r.At < Window).OrderBy(r => r.At).ToList();
            if (recent.Count >= 2)
            {
                // Room for another attempt opens when only one rejection is left inside the window.
                wait = Max(wait, recent[^2].At + Window - now);
            }

            return wait > TimeSpan.Zero;
        }
    }

    public static void RecordRejected(string login)
    {
        lock (Gate)
        {
            Rejections.Add((Clock(), login));
        }
    }

    /// <summary>The message for a held request: it names the reason, because "try again in 8 minutes" alone reads like a bug.</summary>
    public static string HoldMessage(TimeSpan wait) =>
        $"Metron rejected this login a moment ago, so Paperbunkr is not sending it again for about {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minute(s). " +
        "Metron blocks an address for 24 hours after three failed logins in five minutes. Check the login under Preferences → Connections.";

    /// <summary>Forgets what was seen (tests).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Rejections.Clear();
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
