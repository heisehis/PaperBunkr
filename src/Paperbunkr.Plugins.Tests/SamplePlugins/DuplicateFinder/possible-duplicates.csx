// Duplicate Finder - CreateBookList hook (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
// §7). Scans the whole library and returns every book that shares its series+number with another
// book - the dynamic "Possible Duplicates" Smart List entry.
var library = Environment.App.GetLibraryBooks().ToList();
var duplicates = library
    .GroupBy(b => (b.SeriesId, Number: b.Number ?? string.Empty))
    .Where(g => g.Count() > 1)
    .SelectMany(g => g)
    .ToList();

return duplicates;
