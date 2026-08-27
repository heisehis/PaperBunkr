using System;
using System.IO;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace Paperbunkr.App.Services;

public enum ComicInfoWriteBackResult
{
    Success,

    /// <summary>Not a CBZ - RAR (CBR) has no free write library, so this is a deliberate, visible
    /// skip rather than a silent failure (docs/superpowers/specs/2026-08-23-weighted-categorized-
    /// tags-design.md). Other read-only formats (PDF, folder, etc.) skip the same way.</summary>
    SkippedNotCbz,

    Failed,
}

public readonly record struct ComicInfoWriteBackOutcome(ComicInfoWriteBackResult Result, string? ErrorMessage);

/// <summary>
/// Writes updated Genre/Tags back into a CBZ file's embedded ComicInfo.xml (docs/superpowers/specs/
/// 2026-08-23-weighted-categorized-tags-design.md "Build a minimal export path now" follow-up) -
/// the first real caller of the ported ComicRackCE export pipeline (<see cref="ComicExporter"/>/
/// <see cref="PackedStorageProvider"/>), which had zero callers anywhere in Paperbunkr.App before
/// this. Deliberately narrow: this decodes and re-encodes every page just to change two text
/// fields (no lighter in-place zip-entry patch exists in this codebase), so it's used sparingly -
/// only when the Genre/Tags *value set* actually changed, never for a Category/Weight-only edit,
/// which doesn't touch the file's own flat CSV content at all.
///
/// Loads the file's own current <see cref="ComicBook"/> (not a hand-built <see cref="ComicInfo"/>)
/// so every other embedded field (Summary, Writer, credits, ...) survives untouched - only
/// <see cref="ComicInfo.Genre"/>/<see cref="ComicInfo.Tags"/> are overwritten before re-export.
/// <see cref="ExportTarget.ReplaceSource"/> writes through <see cref="ComicExporter"/>'s existing
/// write-to-temp-then-swap safety net (ComicExporter.cs) - a crash mid-write leaves the original
/// file untouched.
/// </summary>
public static class ComicInfoWriteBackService
{
    public static ComicInfoWriteBackOutcome WriteGenreTags(string filePath, string? genre, string? tags)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".cbz", StringComparison.OrdinalIgnoreCase))
        {
            return new ComicInfoWriteBackOutcome(ComicInfoWriteBackResult.SkippedNotCbz, null);
        }

        try
        {
            var book = ComicBook.Create(filePath, RefreshInfoOptions.None);
            book.Genre = genre;
            book.Tags = tags;

            var setting = new ExportSetting
            {
                Target = ExportTarget.ReplaceSource,
                FormatId = KnownFileFormats.CBZ,
            };
            var exporter = new ComicExporter(new[] { book }, setting, sequence: 0);
            exporter.Export(null);
            return new ComicInfoWriteBackOutcome(ComicInfoWriteBackResult.Success, null);
        }
        catch (Exception ex)
        {
            return new ComicInfoWriteBackOutcome(ComicInfoWriteBackResult.Failed, ex.Message);
        }
    }
}
