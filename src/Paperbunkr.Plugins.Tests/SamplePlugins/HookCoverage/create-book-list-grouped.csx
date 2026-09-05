// CreateBookList hook, grouped return shape (docs/superpowers/specs/2026-09-05-plugin-grouped-
// review-and-scan-alerts-design.md §1) - proves an IEnumerable<PluginBookGroup> round-trips through
// PluginEngine.InvokeAsync, alongside the plain IEnumerable<Issue> shape CreateBookList already
// supported (covered by the Duplicate Finder fixture itself).
var library = Environment.App.GetLibraryBooks().ToList();
return new[] { new PluginBookGroup("all books", library, library.Count > 0 ? library[0].Id : (int?)null) };
