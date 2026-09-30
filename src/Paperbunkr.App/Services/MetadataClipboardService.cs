using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// What "Copy Data" copied: the book it came from (for the paste dialog's title) and its field values keyed by
/// <see cref="Models.BulkFieldRegistry"/> label - the same keys and string normalization the undo history's snapshots use, so the Library's
/// Paste Data can apply them through <see cref="Models.BulkFieldDescriptor.Set"/>. The single-book editor also stores its editor-only fields
/// here under <see cref="MetadataClipboardService.StoryArcNumberKey"/> / <see cref="MetadataClipboardService.FinalIssueKey"/>.
/// </summary>
public sealed record MetadataClipboardContent(string SourceCaption, IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// App-wide "Copy Data / Paste Data" clipboard (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §1, Q15) - moved out of
/// <c>IssuePropertiesScreenViewModel</c> so a copy taken in the editor pastes in the Library and the other way round. App-internal on
/// purpose, like CE's own <c>ComicBook</c> clipboard object: the system clipboard keeps whatever text the user copied.
/// </summary>
public sealed class MetadataClipboardService
{
    public static readonly MetadataClipboardService Shared = new();

    /// <summary>Editor-only fields the bulk registry doesn't cover.</summary>
    public const string StoryArcNumberKey = "Story Arc Number";

    public const string FinalIssueKey = "Final Issue";

    public MetadataClipboardContent? Content { get; private set; }

    public bool HasContent => Content is not null;

    /// <summary>Raised after every <see cref="Set"/>, so commands that need content can re-evaluate.</summary>
    public event EventHandler? Changed;

    public void Set(MetadataClipboardContent content)
    {
        Content = content;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
