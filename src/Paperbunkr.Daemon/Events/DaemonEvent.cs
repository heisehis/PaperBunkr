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

/// <summary>A release was handed to the download client (manually approved or auto-grabbed).</summary>
public sealed record IssueSnatchedEvent(int WantedIssueId, string Label, string ReleaseTitle, bool Automatic) : DaemonEvent;

/// <summary>Live progress of one download; <paramref name="Progress"/> is 0..1.</summary>
public sealed record DownloadProgressEvent(int WantedIssueId, string Label, double Progress, long BytesPerSecond, TimeSpan? Eta) : DaemonEvent;

/// <summary>
/// The set of running downloads changed or advanced. <paramref name="Active"/> is how many are running now; the host shows one aggregate job.
/// </summary>
public sealed record DownloadsChangedEvent(int Active, double AverageProgress, string? Detail) : DaemonEvent;

/// <summary>A downloaded release was imported into the library.</summary>
public sealed record IssueImportedEvent(int WantedIssueId, string Label, string Path) : DaemonEvent;

/// <summary>An attempt failed (download or import); the issue is back to needing the user's decision.</summary>
public sealed record IssueFailedEvent(int WantedIssueId, string Label, string Reason) : DaemonEvent;

/// <summary>ComicVine's details were added to an imported issue.</summary>
public sealed record IssueScrapedEvent(int WantedIssueId, string Label, int FieldsChanged) : DaemonEvent;

/// <summary>
/// Adding ComicVine's details to an imported issue failed. The issue itself is fine and stays in the library; the failure is recorded on its want
/// (<c>ScrapeStatus.Failed</c>), so this event only announces it. <paramref name="WillRetry"/> is true when the sweep will try again on its own.
/// </summary>
public sealed record IssueScrapeFailedEvent(int WantedIssueId, string Label, string Reason, bool WillRetry) : DaemonEvent;
