namespace Paperbunkr.App.Models;

/// <summary>
/// What <c>PlaceholderCoverText</c> needs from a Library tile's row model (docs/superpowers/specs/2026-09-21-cosmetics-
/// pitch-2-design.md #18): the text to set on a generated cover when the real one is missing.
/// </summary>
public interface IPlaceholderCoverSource
{
    string? CoverTitle { get; }

    /// <summary>True only when the library scan verified that this issue's linked file is missing.</summary>
    bool IsCoverFileMissing => false;
}
