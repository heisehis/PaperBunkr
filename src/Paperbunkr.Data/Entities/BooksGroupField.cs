namespace Paperbunkr.Data.Entities;

/// <summary>
/// Grouping dimensions for the Books screen (docs/superpowers/specs/2026-08-27-books-screen-chrome-
/// and-home-strip-design.md). <see cref="Series"/> buckets no-series books into "Standalone";
/// <see cref="Author"/> buckets blank authors into "Unknown author".
/// </summary>
public enum BooksGroupField
{
    None,
    Series,
    Author,
}
