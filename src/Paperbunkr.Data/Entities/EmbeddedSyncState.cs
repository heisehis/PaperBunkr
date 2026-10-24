namespace Paperbunkr.Data.Entities;

/// <summary>Outcome of the last attempt to write a chapter's metadata into its file (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10).</summary>
public enum EmbeddedSyncState
{
    /// <summary>Never attempted (or not applicable).</summary>
    None,

    /// <summary>The file holds the metadata the library currently expects.</summary>
    Synced,

    /// <summary>A write failed and is waiting for another attempt.</summary>
    RetryScheduled,

    /// <summary>Retries ran out; shown in the sync status until the user retries or the next launch tries again.</summary>
    NeedsAttention,

    /// <summary>The file is read-only.</summary>
    SkippedReadOnly,

    /// <summary>A format with no free writer (CBR/CB7/CBT/PDF/DjVu/EPUB).</summary>
    UnsupportedFormat,

    SkippedMissingFile,

    /// <summary>The file changed outside Paperbunkr since our last write; automatic writes leave it alone until the user says "Write anyway".</summary>
    ConflictExternalModification,
}
