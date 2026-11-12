using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Common;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Collections;

namespace Paperbunkr.App.Models;

/// <summary>
/// Everything the Library's collection banner shows (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md): name, glyph, accent, description,
/// stat chips, read percentage and the cover. Built when a collection is active; never for "All Series".
/// </summary>
public sealed class CollectionBannerState
{
    public required int CollectionId { get; init; }

    public required string Name { get; init; }

    public string? IconGlyph { get; init; }

    /// <summary>True when the stored icon can be drawn (<see cref="CollectionIconResolver"/>).</summary>
    public bool HasGlyph => CollectionIconResolver.Resolve(IconGlyph) is not null;

    public string? AccentColor { get; init; }

    public bool IsPinned { get; init; }

    public bool IsSmart { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<MdBlock> DescriptionBlocks => MarkdownLite.ParseDescription(Description);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>"3 series", "12 issues", "2 books" - only the non-zero kinds, in that order.</summary>
    public IReadOnlyList<string> StatChips { get; init; } = new List<string>();

    /// <summary>"62% read", or null when the collection has no readable units.</summary>
    public string? ReadText { get; init; }

    public double? ReadFraction { get; init; }

    /// <summary>The cover the banner blurs behind its text: the chosen / first member (null for a manual cover or none).</summary>
    public LibraryTile? CoverTile { get; init; }

    public Bitmap? ManualCover { get; init; }

    public IBrush CoverBrush { get; init; } = Brushes.Gray;

    /// <summary>"Edit collection", etc. - the accessible name of the whole banner.</summary>
    public string AutomationName => $"Collection {Name}";

    public static IReadOnlyList<string> BuildStats(IEnumerable<CollectionMember> members)
    {
        int series = 0, issues = 0, books = 0;
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case CollectionMemberKind.Series: series++; break;
                case CollectionMemberKind.Issue: issues++; break;
                case CollectionMemberKind.Book: books++; break;
            }
        }

        var chips = new List<string>();
        if (series > 0) chips.Add(series == 1 ? "1 series" : $"{series} series");
        if (issues > 0) chips.Add(issues == 1 ? "1 issue" : $"{issues} issues");
        if (books > 0) chips.Add(books == 1 ? "1 book" : $"{books} books");
        return chips;
    }
}
