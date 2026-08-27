using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>
/// One undoable/redoable metadata edit (docs/ce-feature-inventory.md §A "Undo/Redo for metadata
/// edits") - one entry per Save from either the single-book or bulk editor, covering every issue
/// that Save touched. <see cref="Before"/>/<see cref="After"/> are keyed by Issue.Id, each value a
/// snapshot of every <see cref="BulkFieldDescriptor"/> field's value on that issue (see <see
/// cref="Paperbunkr.App.Services.MetadataEditHistoryService.CaptureSnapshot"/>) - reusing the same
/// registry both editors already use to read/write fields means Undo/Redo needs no separate
/// field-by-field mapping of its own.
/// </summary>
public sealed class MetadataEditHistoryEntry
{
    public required string Description { get; init; }

    public required Dictionary<int, Dictionary<string, string?>> Before { get; init; }

    public required Dictionary<int, Dictionary<string, string?>> After { get; init; }
}
