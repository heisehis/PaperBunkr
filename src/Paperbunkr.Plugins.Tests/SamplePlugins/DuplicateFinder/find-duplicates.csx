// Duplicate Finder - Library hook (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5/§7).
// Paperbunkr's Library grid has no multi-selection model (unlike CE), so Books is normally a
// single right-clicked issue here - this compares each book in Books against the WHOLE library
// (not just within Books itself, which would never find anything with a single-item selection).
var library = Environment.App.GetLibraryBooks().ToList();
var duplicateGroups = new System.Collections.Generic.List<string>();

foreach (var book in Books)
{
    var matches = library
        .Where(other => other.Id != book.Id && other.SeriesId == book.SeriesId && (other.Number ?? string.Empty) == (book.Number ?? string.Empty))
        .ToList();

    if (matches.Count > 0)
    {
        duplicateGroups.Add($"Series #{book.SeriesId} issue {book.Number} ({matches.Count + 1} copies)");
    }
}

if (duplicateGroups.Count > 0)
{
    Environment.App.AskQuestion(
        "Found " + duplicateGroups.Count + " duplicate group(s):\n" + string.Join("\n", duplicateGroups),
        "OK",
        string.Empty);
}

return duplicateGroups;
