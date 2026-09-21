using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Recording <see cref="IPluginActivity"/> for tests: every job started and alert raised is kept so a
/// real plugin script can be asserted against. Shared by reference across environment clones (the
/// fake environments clone with <c>MemberwiseClone</c>), which is what lets a test read back what a
/// command did through its own per-command clone.
/// </summary>
internal sealed class FakePluginActivity : IPluginActivity
{
    public List<FakeJob> Jobs { get; } = new();

    public List<FakeAlert> Alerts { get; } = new();

    // Domain hooks run on background threads, so two commands can report at once - adds are locked.
    // Tests read after the work has finished (or when the adding thread is known to be parked).
    private readonly object _gate = new();

    public IPluginActivityHandle StartJob(string title, bool cancellable = true)
    {
        var job = new FakeJob(title, cancellable);
        lock (_gate)
        {
            Jobs.Add(job);
        }

        return job;
    }

    public void RaiseAlert(PluginAlertSeverity severity, string title, string? detail = null, string? dedupeKey = null)
    {
        lock (_gate)
        {
            Alerts.Add(new FakeAlert(severity, title, detail, dedupeKey));
        }
    }

    internal sealed record FakeAlert(PluginAlertSeverity Severity, string Title, string? Detail, string? DedupeKey);

    internal sealed class FakeJob : IPluginActivityHandle
    {
        public FakeJob(string title, bool cancellable)
        {
            Title = title;
            Cancellable = cancellable;
        }

        public string Title { get; }

        public bool Cancellable { get; }

        /// <summary>Every <c>Report</c> call, as <c>"done/total detail"</c> or just the detail string.</summary>
        public List<string> Reports { get; } = new();

        /// <summary><c>"Succeeded: …"</c> / <c>"Failed: …"</c>, or null while neither was called.</summary>
        public string? Outcome { get; private set; }

        public bool Disposed { get; private set; }

        public CancellationToken CancellationToken => default;

        public void Report(int done, int total, string? detail = null) => Reports.Add($"{done}/{total} {detail}".TrimEnd());

        public void Report(string detail) => Reports.Add(detail);

        public void Succeed(string summary) => Outcome = $"Succeeded: {summary}";

        public void Fail(string summary) => Outcome = $"Failed: {summary}";

        public void Dispose() => Disposed = true;
    }
}
