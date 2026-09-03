using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Ctrl+P command palette (docs/superpowers/specs/2026-09-03-quick-open-command-palette-
/// design.md). Owns the query / result list / selection and the fuzzy re-rank; hands the chosen
/// entry to <c>MainViewModel.ActivateQuickOpenEntry</c> via the <c>activate</c> delegate.
/// </summary>
public partial class QuickOpenViewModel : ViewModelBase
{
    private readonly Action<QuickOpenEntry> _activate;
    private readonly Action _close;
    private readonly QuickOpenService _service;

    private IReadOnlyList<QuickOpenEntry> _index = Array.Empty<QuickOpenEntry>();

    public QuickOpenViewModel(Action<QuickOpenEntry> activate, Action close, QuickOpenService? service = null)
    {
        _activate = activate;
        _close = close;
        _service = service ?? new QuickOpenService();
    }

    public ObservableCollection<QuickOpenEntry> Results { get; } = new();

    /// <summary>Raised by <see cref="Open"/> - the overlay code-behind uses this to focus the search box.</summary>
    public event Action? Opened;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    public QuickOpenEntry? SelectedEntry =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public bool HasNoMatches => Query.Length > 0 && Results.Count == 0;

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(SelectedEntry));

    // Re-ranked synchronously on each keystroke - QuickOpenMatcher.Rank is an O(n) in-memory pass
    // over a few thousand short strings (microseconds), so a debounce would add latency for nothing.
    partial void OnQueryChanged(string value) => Rerank();

    /// <summary>Called every time the overlay is opened - rebuilds the index and starts blank on the recency list.</summary>
    public void Open()
    {
        _index = _service.BuildIndex();
        Query = string.Empty;
        Rerank();
        Opened?.Invoke();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    public void ActivateSelected()
    {
        if (SelectedEntry is { } entry)
        {
            _activate(entry);
            _close();
        }
    }

    private void Rerank()
    {
        Results.Clear();
        foreach (var entry in QuickOpenMatcher.Rank(Query, _index))
        {
            Results.Add(entry);
        }

        SelectedIndex = 0;
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasNoMatches));
    }
}
