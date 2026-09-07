using System;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// A permanent record that a file path was deliberately removed from the library (docs/superpowers/
/// specs/2026-09-06-scan-missing-file-handling-design.md) - CE parity for <c>Settings
/// .DontAddRemoveFiles</c>'s blacklist. Written unconditionally by <c>LibraryDeletionHelper
/// .RemoveIssue</c>/<c>RemoveSeries</c> on every removal, regardless of whether
/// <see cref="AppSettings.DontReimportRemovedFiles"/> is on - the setting only gates whether
/// <c>LibraryFolderScanner</c> consults this table during import, not whether it's populated.
/// Deliberately separate from <see cref="RemovedLibraryEntry"/>, which is a 30-day undo log - this
/// table never expires, matching CE's own permanent, database-saved blacklist.
/// </summary>
public class RemovedFilePath
{
    public int Id { get; set; }

    /// <summary>Unique (case-insensitive comparison at query time - Windows path semantics). Upserted: removing the same path again just bumps <see cref="RemovedAtUtc"/> rather than duplicating the row.</summary>
    public string FilePath { get; set; } = "";

    public DateTime RemovedAtUtc { get; set; }
}
