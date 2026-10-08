using System;
using System.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Shared settings for the whole-library sweeps (Verify, metadata sync, series resync) that read the library a page at a time
/// (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.3/§4.4). Pages are read by id cursor
/// (<c>Where(Id &gt; last).OrderBy(Id).Take(n)</c>), never <c>Skip/Take</c>, which makes SQLite walk and discard every earlier row.
/// </summary>
internal static class SweepPaging
{
    public const int DefaultPageSize = 250;

    /// <summary>
    /// Breather after each full page so a long sweep leaves the two cores to the UI now and then. The sweeps are synchronous
    /// inside <c>Task.Run</c>, so this waits on the token (it returns at once on cancel) instead of awaiting a delay. Windows
    /// rounds a short wait up to its timer tick, about 15 ms.
    /// </summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(10);

    public static void PauseBetweenPages(CancellationToken ct)
    {
        ct.WaitHandle.WaitOne(Pause);
        ct.ThrowIfCancellationRequested();
    }
}
