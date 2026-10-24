using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists.CeImport;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What <see cref="CblImport.ImportAny"/> made of a <c>.cbl</c> file. Exactly one of <see cref="ReadingList"/>, <see cref="SmartList"/> is set unless the import was refused.</summary>
public sealed record CblImportResult(
    CblFileKind Kind,
    ReadingList? ReadingList,
    SmartList? SmartList,
    IReadOnlyList<CeImportWarning> Warnings,
    string? RefusedReason)
{
    public bool Refused => RefusedReason is not null;

    /// <summary>For a reading list: what became of every entry (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.7).</summary>
    public Matching.ImportReport? Report { get; init; }
}

/// <summary>
/// One entry point for importing any ComicRack <c>.cbl</c>: a reading list goes through the existing <see cref="CblReadingListIO"/>, a smart list through
/// <see cref="SmartListFileImporter"/> (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md).
/// </summary>
public static class CblImport
{
    /// <exception cref="InvalidDataException">The file is not a ComicRack list.</exception>
    public static CblImportResult ImportAny(PaperbunkrDbContext context, string path)
    {
        switch (CblFile.Detect(path))
        {
            case CblFileKind.SmartList:
                SmartListImportResult smart = SmartListFileImporter.Import(context, path);
                return new CblImportResult(CblFileKind.SmartList, null, smart.List, smart.Warnings, smart.RefusedReason);

            case CblFileKind.ReadingList:
                var (list, report) = CblReadingListIO.ImportWithReport(context, path);
                return new CblImportResult(CblFileKind.ReadingList, list, null, Array.Empty<CeImportWarning>(), null) { Report = report };

            default:
                throw new InvalidDataException($"'{path}' is not a valid CBL list.");
        }
    }
}
