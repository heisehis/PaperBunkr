using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>Open/closed state of one collapsible Library Health section (a one-line header row that expands to its list).</summary>
public partial class LibraryHealthSectionState : ObservableObject
{
    public LibraryHealthSectionState(string key, LibraryHealthTab tab, string anchor)
    {
        Key = key;
        Tab = tab;
        Anchor = anchor;
    }

    public string Key { get; }

    /// <summary>The sub-tab this section lives on.</summary>
    public LibraryHealthTab Tab { get; }

    /// <summary>The scroll-anchor <c>Tag</c> of the section in <c>LibrarySection.axaml</c> (also its search-index anchor).</summary>
    public string Anchor { get; }

    [ObservableProperty]
    private bool _isOpen;

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;
}

/// <summary>
/// The ten collapsible sections of the Library Health card, each with its open state, sub-tab and scroll anchor
/// (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md). All start closed; a deep link or search hit opens the
/// one it targets. A closed section is never measured, so its list builds no rows.
/// </summary>
public sealed class LibraryHealthSections
{
    public LibraryHealthSections()
    {
        Duplicates = new("duplicates", LibraryHealthTab.Review, "library.healthDuplicates");
        SeriesConflicts = new("seriesConflicts", LibraryHealthTab.Review, "library.healthSeriesConflicts");
        ContentType = new("contentType", LibraryHealthTab.Review, "library.healthContentType");
        Proposals = new("proposals", LibraryHealthTab.Review, "library.healthProposals");
        AdPages = new("adPages", LibraryHealthTab.Review, "library.healthAdPages");
        ReportedPages = new("reportedPages", LibraryHealthTab.Review, "library.healthReportedPages");
        SimilarSeries = new("similarSeries", LibraryHealthTab.Review, "library.healthSimilarSeries");
        Missing = new("missing", LibraryHealthTab.Files, "library.healthMissing");
        EmptyRows = new("emptyRows", LibraryHealthTab.Files, "library.healthEmptyRows");
        RecentlyRemoved = new("recentlyRemoved", LibraryHealthTab.Files, "library.healthRecentlyRemoved");
        All = new[] { Duplicates, SeriesConflicts, ContentType, Proposals, AdPages, ReportedPages, SimilarSeries, Missing, EmptyRows, RecentlyRemoved };
    }

    public LibraryHealthSectionState Duplicates { get; }

    public LibraryHealthSectionState SeriesConflicts { get; }

    public LibraryHealthSectionState ContentType { get; }

    public LibraryHealthSectionState Proposals { get; }

    public LibraryHealthSectionState AdPages { get; }

    public LibraryHealthSectionState ReportedPages { get; }

    public LibraryHealthSectionState SimilarSeries { get; }

    public LibraryHealthSectionState Missing { get; }

    public LibraryHealthSectionState EmptyRows { get; }

    public LibraryHealthSectionState RecentlyRemoved { get; }

    public IReadOnlyList<LibraryHealthSectionState> All { get; }

    /// <summary>
    /// The section a scroll anchor (<c>library.healthDuplicates</c> ...) or a short key (<c>duplicates</c>) belongs to, or null.
    /// Case-insensitive, because deep-link payloads are written by hand (<c>LibraryHealth/Review/Duplicates</c>).
    /// </summary>
    public LibraryHealthSectionState? Find(string? anchorOrKey)
    {
        if (string.IsNullOrEmpty(anchorOrKey))
        {
            return null;
        }

        foreach (var section in All)
        {
            if (string.Equals(section.Anchor, anchorOrKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(section.Key, anchorOrKey, StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }
}
