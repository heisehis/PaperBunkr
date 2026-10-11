using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One source's answer behind a suggestion: a short chip ("MangaBaka: manhwa", "AniList: KR") and, as its tooltip, what was matched and how well.</summary>
public sealed record ContentTypeEvidenceChip(string Text, string ToolTip);

/// <summary>
/// One row in Library Health's Content Type queue (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md): a series whose type is Unknown or has
/// a tracker-suggested type waiting for a person. The row is a dense table line; <see cref="IsExpanded"/> opens it in place into the card (cover, evidence chips, type picker).
/// The same type backs the "Recently auto-classified" rows (<see cref="IsAutoClassified"/>), where the action is Undo.
/// </summary>
public partial class SeriesReviewItem : ObservableObject
{
    public int SeriesId { get; init; }

    public string SeriesName { get; init; } = string.Empty;

    /// <summary>The issue whose cover the expanded card shows.</summary>
    public int? CoverIssueId { get; init; }

    /// <summary>The series' type today ("Unknown", "Manga", ...).</summary>
    public string CurrentLabel { get; init; } = "Unknown";

    /// <summary>The queued candidate type, or null when the row is just an unknown series with nothing to confirm.</summary>
    public ContentType? Suggestion { get; init; }

    public string SuggestionLabel => Suggestion?.ToString() ?? string.Empty;

    public bool HasSuggestion => Suggestion is not null;

    public double? Confidence { get; init; }

    /// <summary>"97%", or empty with no suggestion.</summary>
    public string ConfidenceLabel => Confidence is double c ? $"{c:P0}" : string.Empty;

    /// <summary>The sources disagree on the type: never bulk-accepted, shown with a warning.</summary>
    public bool IsConflict { get; init; }

    /// <summary>At the auto-match tier with an unambiguous type and no conflict: what "Accept all high confidence" takes.</summary>
    public bool IsHighConfidence { get; init; }

    /// <summary>For a row with no suggestion: why (not looked up yet, no match, not looked up because it looks Western).</summary>
    public string StatusLabel { get; init; } = string.Empty;

    public IReadOnlyList<ContentTypeEvidenceChip> Chips { get; init; } = System.Array.Empty<ContentTypeEvidenceChip>();

    public bool HasChips => Chips.Count > 0;

    /// <summary>All the chips on one line for the table row.</summary>
    public string ChipSummary => string.Join(" · ", System.Linq.Enumerable.Select(Chips, c => c.Text));

    /// <summary>True for a row in "Recently auto-classified": the type was applied on its own and can be undone.</summary>
    public bool IsAutoClassified { get; init; }

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
