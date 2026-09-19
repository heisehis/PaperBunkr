namespace Paperbunkr.Daemon.Events;

public enum DaemonAlertSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Everything the daemon tells the outside world. The UI never calls into the daemon's internals; it only
/// consumes these (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §3).
/// </summary>
public abstract record DaemonEvent
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A polling cycle began (used to open an Activity Center job).</summary>
public sealed record CycleStartedEvent(bool Manual) : DaemonEvent;

/// <summary>Progress inside a cycle, e.g. "Searching 3 of 12".</summary>
public sealed record CycleProgressEvent(int Done, int Total, string? Detail) : DaemonEvent;

/// <summary>A cycle finished; <paramref name="Summary"/> becomes the job's success text.</summary>
public sealed record CycleCompletedEvent(string Summary, int IssuesSearched, int CandidatesFound) : DaemonEvent;

/// <summary>A cycle ended early because of a failure; <paramref name="Message"/> becomes the job's failure text.</summary>
public sealed record CycleFailedEvent(string Message) : DaemonEvent;

/// <summary>New release candidates were stored for a wanted issue.</summary>
public sealed record CandidatesFoundEvent(int WantedIssueId, string Label, int Count) : DaemonEvent;

/// <summary>
/// Something needs the user's attention (indexer unreachable, ComicVine paused...). <paramref name="Key"/>
/// deduplicates repeats so the same outage doesn't stack alerts.
/// </summary>
public sealed record DaemonAlertEvent(string Key, DaemonAlertSeverity Severity, string Title, string? Detail) : DaemonEvent;

/// <summary>The condition behind an earlier alert with this key has cleared.</summary>
public sealed record DaemonAlertClearedEvent(string Key) : DaemonEvent;
