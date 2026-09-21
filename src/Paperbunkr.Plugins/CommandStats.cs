using System.Globalization;

namespace Paperbunkr.Plugins;

/// <summary>A point-in-time copy of a command's <see cref="CommandStats"/>.</summary>
public sealed record CommandStatsSnapshot(
    long Runs,
    long Failures,
    long TimedOut,
    long Dropped,
    TimeSpan Total,
    TimeSpan Max,
    TimeSpan Last)
{
    public TimeSpan Average => Runs == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Total.Ticks / Runs);

    /// <summary>True when there is anything worth showing.</summary>
    public bool HasData => Runs > 0 || TimedOut > 0 || Dropped > 0;

    /// <summary>
    /// One muted line for the Plugin screen, e.g. <c>12 runs · avg 45 ms · max 210 ms · 1 failed · 1 timed out · 2 dropped</c>;
    /// only the parts that apply, or null when there's nothing to say.
    /// </summary>
    public string? Describe()
    {
        if (!HasData)
        {
            return null;
        }

        var parts = new List<string>();
        if (Runs > 0)
        {
            parts.Add($"{Runs} {(Runs == 1 ? "run" : "runs")}");
            parts.Add($"avg {Format(Average)}");
            parts.Add($"max {Format(Max)}");
        }

        if (Failures > 0)
        {
            parts.Add($"{Failures} failed");
        }

        if (TimedOut > 0)
        {
            parts.Add($"{TimedOut} timed out");
        }

        if (Dropped > 0)
        {
            parts.Add($"{Dropped} dropped");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>A duration the way the Plugin screen writes it: whole milliseconds under a second, otherwise seconds to one decimal.</summary>
    public static string Format(TimeSpan duration) =>
        duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s"
            : $"{Math.Round(duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)} ms";
}

/// <summary>
/// How a command has behaved since the app started (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md
/// §2): runs, failures, durations, and - for the domain hooks - how often it timed out or had events dropped.
/// Recorded by <see cref="Command.InvokeAsync"/>, so every hook and every caller is covered. <b>In memory only</b>:
/// diagnostic data for whoever is wondering why a plugin feels slow, not history worth a table. Thread-safe -
/// domain hooks run on background threads.
/// </summary>
public sealed class CommandStats
{
    private readonly object _gate = new();
    private long _runs;
    private long _failures;
    private long _timedOut;
    private long _dropped;
    private TimeSpan _total;
    private TimeSpan _max;
    private TimeSpan _last;

    public void RecordRun(TimeSpan duration, bool failed)
    {
        lock (_gate)
        {
            _runs++;
            if (failed)
            {
                _failures++;
            }

            _total += duration;
            _last = duration;
            if (duration > _max)
            {
                _max = duration;
            }
        }
    }

    /// <summary>The command was still running when its timeout expired.</summary>
    public void RecordTimeout()
    {
        lock (_gate)
        {
            _timedOut++;
        }
    }

    /// <summary>An event queued for the command was dropped because its queue was full. Counted per event, not once per session.</summary>
    public void RecordDropped()
    {
        lock (_gate)
        {
            _dropped++;
        }
    }

    public CommandStatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new CommandStatsSnapshot(_runs, _failures, _timedOut, _dropped, _total, _max, _last);
        }
    }
}
