namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-defined, flat (non-nested — closed as an open item, docs/onboarding.md §15) collapsible
/// grouping, Mihon-style — surfaced in the UI as a "Collection" (renamed from the original
/// <c>Category</c>, docs/superpowers/specs/2026-08-27-collections-design.md). Membership is a mix
/// of <see cref="Series"/>, standalone <see cref="Issue"/>, and <see cref="Book"/> rows, held in a
/// user-defined order via the polymorphic <see cref="CollectionItem"/> join entity (chosen over
/// three separate M:M joins so one ordered query renders a mixed collection).
/// </summary>
public class Collection
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>Free-text blurb shown in the collection header. Optional.</summary>
    public string? Description { get; set; }

    /// <summary>Hex colour (<c>#RRGGBB</c>) for the sidebar dot and Detail chips. Null → the generic accent.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Manual cover image path. Honoured only when <see cref="IsAutoCover"/> is <see langword="false"/>.</summary>
    public string? CoverImagePath { get; set; }

    /// <summary>When true (default), the cover is derived on read from the first member; see <c>CollectionResolver.ResolveCover</c>.</summary>
    public bool IsAutoCover { get; set; } = true;

    public List<CollectionItem> Items { get; set; } = new();
}
