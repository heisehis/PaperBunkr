using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>Displays one candidate's main and associated ComicVine cover images, remembering an
/// explicitly selected URL only for the lifetime of the current scrape run.</summary>
public sealed partial class ComicVineCoverCarouselViewModel : ObservableObject
{
    private readonly IDictionary<string, string> _sessionSelections;
    private IReadOnlyList<string> _urls = Array.Empty<string>();
    private string _selectionKey = string.Empty;

    [ObservableProperty]
    private string? _currentCoverUrl;

    public bool HasAlternates => _urls.Count > 1;

    public string PositionLabel => _urls.Count == 0
        ? "No cover"
        : $"Cover {_urls.IndexOf(CurrentCoverUrl) + 1} of {_urls.Count}";

    public ComicVineCoverCarouselViewModel(IDictionary<string, string>? sessionSelections = null)
    {
        _sessionSelections = sessionSelections ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public void SetCovers(IEnumerable<string?> urls, string selectionKey, string? preferredUrl = null)
    {
        _urls = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _selectionKey = selectionKey;

        string? selectedUrl = _sessionSelections.TryGetValue(selectionKey, out string? remembered)
            ? _urls.FirstOrDefault(url => string.Equals(url, remembered, StringComparison.Ordinal))
            : null;
        CurrentCoverUrl = selectedUrl ?? _urls.FirstOrDefault(url => string.Equals(url, preferredUrl, StringComparison.Ordinal)) ?? _urls.FirstOrDefault();
        OnPropertyChanged(nameof(HasAlternates));
        OnPropertyChanged(nameof(PositionLabel));
        PreviousCoverCommand.NotifyCanExecuteChanged();
        NextCoverCommand.NotifyCanExecuteChanged();
    }

    private bool CanCycle() => _urls.Count > 1;

    [RelayCommand(CanExecute = nameof(CanCycle))]
    private void PreviousCover() => SelectOffset(-1);

    [RelayCommand(CanExecute = nameof(CanCycle))]
    private void NextCover() => SelectOffset(1);

    private void SelectOffset(int offset)
    {
        int current = _urls.IndexOf(CurrentCoverUrl);
        int next = (current + offset + _urls.Count) % _urls.Count;
        CurrentCoverUrl = _urls[next];
        _sessionSelections[_selectionKey] = _urls[next];
        OnPropertyChanged(nameof(PositionLabel));
    }
}

internal static class CoverUrlListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string? value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
