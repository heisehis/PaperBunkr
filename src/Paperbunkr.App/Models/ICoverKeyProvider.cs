namespace Paperbunkr.App.Models;

/// <summary>
/// An item that has a cover thumbnail the Library grid can prefetch (docs/superpowers/specs/2026-09-19-library-scroll-
/// smoothness-design.md §3.3). <see cref="CoverKey"/> is the same stem <c>AsyncCoverImage.SourceId</c> binds, or
/// <see langword="null"/> when the item has no cover to load.
/// </summary>
public interface ICoverKeyProvider
{
    string? CoverKey { get; }
}
