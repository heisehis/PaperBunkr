namespace Paperbunkr.App.Models;

/// <summary>When a settled <see cref="ActivityJob"/> should raise a transient completion toast.</summary>
public enum ActivityToastPolicy
{
    /// <summary>Toast on success and failure (the normal case).</summary>
    Always,

    /// <summary>Toast only when the job failed.</summary>
    FailuresOnly,

    /// <summary>Never toast for this job.</summary>
    Never,
}
