using System;
using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One <see cref="LibrarySortField"/>'s comparison logic and display label, extracted verbatim from <c>LibraryScreenViewModel.SortCards</c>'s old switch (docs/superpowers/specs/2026-08-17-metadata-model-phase2c-library-field-descriptors-design.md).</summary>
public sealed record LibrarySortFieldDescriptor(LibrarySortField Field, string DisplayName, Comparison<SeriesCardSample> Compare);

/// <summary>
/// One <see cref="LibraryGroupField"/>'s bucketing/ordering logic and display label, extracted
/// verbatim from <c>LibraryScreenViewModel.GroupCards</c>'s old switch. <see cref="GroupKey"/> also
/// serves as the resulting group's display header; <see cref="GroupOrder"/> orders the groups
/// themselves, which isn't always the same as alphabetical-by-key (e.g. <see cref="ContentType"/>).
/// </summary>
public sealed record LibraryGroupFieldDescriptor(LibraryGroupField Field, string DisplayName, Func<SeriesCardSample, string> GroupKey, Comparison<string> GroupOrder);

/// <summary>
/// Data-driven field catalog for Library's sort/group logic (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase2c-library-field-descriptors-design.md) - same shape this codebase already
/// uses for <c>SmartListCatalog</c> and <c>BulkFieldRegistry</c>. Deliberately two separate small
/// dictionaries, not one unified descriptor type: <see cref="LibrarySortField"/> and
/// <see cref="LibraryGroupField"/> aren't the same set today (<c>Name</c> isn't groupable,
/// <c>ContentType</c>/<c>Alphabetical</c> aren't sortable), so forcing them into one shared shape
/// would invent overlap that doesn't exist. <see cref="Comparison{T}"/>, not a generic
/// <c>IComparable</c>-returning key selector, is the load-bearing choice for both - it's the only
/// way to carry each field's exact existing comparison semantics (ordinal case-insensitive string
/// comparison, not .NET's default culture-aware one; the "blank Publisher -&gt; Unknown" fallback;
/// enum-declaration-order for <see cref="ContentType"/> groups, not alphabetical-by-label) into a
/// named delegate without reinventing them behind a shared, more generic strategy shape.
/// </summary>
public static class LibraryFieldCatalog
{
    public static readonly IReadOnlyDictionary<LibrarySortField, LibrarySortFieldDescriptor> SortFields = new Dictionary<LibrarySortField, LibrarySortFieldDescriptor>
    {
        [LibrarySortField.Name] = new(LibrarySortField.Name, "Name",
            (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)),
        [LibrarySortField.DateAdded] = new(LibrarySortField.DateAdded, "Date Added",
            (a, b) => Nullable.Compare(a.LastAddedTime, b.LastAddedTime)),
        [LibrarySortField.LastRead] = new(LibrarySortField.LastRead, "Last Read",
            (a, b) => Nullable.Compare(a.LastOpenedTime, b.LastOpenedTime)),
        [LibrarySortField.Size] = new(LibrarySortField.Size, "Size",
            (a, b) => a.TotalFileSize.CompareTo(b.TotalFileSize)),
        [LibrarySortField.IssueCount] = new(LibrarySortField.IssueCount, "Issue Count",
            (a, b) => a.IssueCount.CompareTo(b.IssueCount)),
        [LibrarySortField.UnreadCount] = new(LibrarySortField.UnreadCount, "Unread Count",
            (a, b) => a.UnreadCount.CompareTo(b.UnreadCount)),
        [LibrarySortField.Publisher] = new(LibrarySortField.Publisher, "Publisher",
            (a, b) => string.Compare(a.Publisher, b.Publisher, StringComparison.OrdinalIgnoreCase)),
    };

    /// <summary>No entry for <see cref="LibraryGroupField.None"/> - nothing to describe. <c>GroupCards</c> keeps returning an empty result for it, same as the old switch's default branch.</summary>
    public static readonly IReadOnlyDictionary<LibraryGroupField, LibraryGroupFieldDescriptor> GroupFields = new Dictionary<LibraryGroupField, LibraryGroupFieldDescriptor>
    {
        [LibraryGroupField.ContentType] = new(LibraryGroupField.ContentType, "Content Type",
            c => c.ContentTypeLabel,
            (a, b) => Enum.Parse<ContentType>(a).CompareTo(Enum.Parse<ContentType>(b))),
        [LibraryGroupField.Publisher] = new(LibraryGroupField.Publisher, "Publisher",
            c => string.IsNullOrWhiteSpace(c.Publisher) ? "Unknown" : c.Publisher,
            (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
        [LibraryGroupField.Alphabetical] = new(LibraryGroupField.Alphabetical, "Alphabetical",
            c => c.Name.Length > 0 && char.IsAsciiLetter(c.Name[0]) ? char.ToUpperInvariant(c.Name[0]).ToString() : "#",
            (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
    };
}
