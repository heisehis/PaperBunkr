// Duplicate Finder - CreateBookList hook (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
// §7, upgraded to the grouped shape in docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-
// scan-alerts-design.md §5). Scans the whole library and groups every set of books that share a
// series+number - the dynamic "Possible Duplicates" Smart List entry, now reviewable in the
// Grouped Review overlay with a suggested keeper instead of just a flat list.
var library = Environment.App.GetLibraryBooks().ToList();
var groups = library
    .GroupBy(b => (b.SeriesId, Number: b.Number ?? string.Empty))
    .Where(g => g.Count() > 1)
    .Select(g =>
    {
        var books = g.ToList();
        // Prefer a copy with a real file over a fileless placeholder; if every copy is a
        // placeholder (or none stands out), fall back to the first one in the group.
        var preferred = books.FirstOrDefault(b => !string.IsNullOrEmpty(b.FilePath) && !b.IsPlaceholder) ?? books[0];
        return new PluginBookGroup($"Series #{g.Key.SeriesId} issue {g.Key.Number}", books, preferred.Id);
    })
    .ToList();

return groups;
