using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// One collapsible group in the TOC drawer (docs/superpowers/specs/2026-09-03-books-reader-hud-
/// redesign-design.md) - a run of consecutive chapters sharing the same <see cref="BookChapterSummary.PartTitle"/>.
/// A chapter with no <c>PartTitle</c> still gets wrapped in one of these (a single-chapter group with
/// <see cref="PartTitle"/> null), so the TOC drawer's XAML only needs one template rather than two
/// parallel grouped/ungrouped code paths - it just hides the header row when <c>PartTitle</c> is null.
/// </summary>
public sealed partial class BookTocGroup : ObservableObject
{
    public string? PartTitle { get; init; }

    public ObservableCollection<BookChapterSummary> Chapters { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
