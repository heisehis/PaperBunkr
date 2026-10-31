using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// A batch of metadata writes shown to the user before anything is saved (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-
/// 4-5-pitch-completion-design.md §D2). The setters only <em>record</em> a change (each returns false for an issue that no longer exists);
/// <see cref="PreviewAndApplyAsync"/> shows every book's "field: old → new" and, on Apply, writes them all in one transaction.
/// <code>
/// var batch = Environment.Writer.BeginBatch();
/// foreach (var book in Books) batch.SetFormat(book, "Trade Paperback");
/// bool applied = await batch.PreviewAndApplyAsync("Mark as trade paperbacks");
/// </code>
/// </summary>
public interface IMetadataWriteBatch
{
    bool SetFormat(Issue issue, string? value);

    bool SetBookAge(Issue issue, string? value);

    bool SetCustomValue(Issue issue, string name, string? value);

    bool AddTag(Issue issue, string tag);

    bool RemoveTag(Issue issue, string tag);

    /// <summary>How many recorded writes would actually change something.</summary>
    int PendingChanges { get; }

    /// <summary>
    /// Shows the changes and applies them if the user chooses Apply. False when there was nothing to change (no dialog is shown),
    /// the user cancelled, or the save failed (nothing is written then). A batch is used once.
    /// </summary>
    Task<bool> PreviewAndApplyAsync(string title);
}
