using ClusterLibraryManager.ComicVine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClusterLibraryManager.Dialogs;

/// <summary>One ranked candidate row (design doc §4's modal-per-book review flow).</summary>
public sealed partial class ComicVineMatchCandidateViewModel : ObservableObject
{
    private readonly Action<ComicVineVolumeSearchResult> _choose;

    public ComicVineVolumeSearchResult Volume { get; }
    public double Score { get; }

    public string DisplayLabel =>
        $"{Volume.Name} ({Volume.StartYear ?? "?"}) - {Volume.Publisher ?? "Unknown publisher"}";

    public ComicVineMatchCandidateViewModel(ComicVineVolumeSearchResult volume, double score, Action<ComicVineVolumeSearchResult> choose)
    {
        Volume = volume;
        Score = score;
        _choose = choose;
    }

    [RelayCommand]
    private void Choose() => _choose(Volume);
}

/// <summary>
/// Backs <see cref="ComicVineMatchReviewDialogView"/> (design doc §4, grilling Q14 - modal-per-book,
/// same mechanism as <see cref="FileConflictDialogViewModel"/>). Shown only when auto-choose is off
/// and the run is interactive (design doc §4/§9's headless-automation gate; a non-interactive run
/// never constructs this at all, it uses the skip-and-log path directly in the plugin's own
/// scrape-and-apply orchestration instead).
/// </summary>
public sealed partial class ComicVineMatchReviewDialogViewModel : ObservableObject
{
    private readonly Action<ComicVineVolumeSearchResult?> _resolve;

    public string BookLabel { get; }
    public IReadOnlyList<ComicVineMatchCandidateViewModel> Candidates { get; }

    public ComicVineMatchReviewDialogViewModel(
        string bookLabel,
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> rankedCandidates,
        Action<ComicVineVolumeSearchResult?> resolve)
    {
        BookLabel = bookLabel;
        _resolve = resolve;
        Candidates = rankedCandidates
            .OrderByDescending(c => c.Score)
            .Select(c => new ComicVineMatchCandidateViewModel(c.Volume, c.Score, Choose))
            .ToList();
    }

    private void Choose(ComicVineVolumeSearchResult volume) => _resolve(volume);

    /// <summary>Skipping resolves with null - the book is left unmatched, no ComicVine fields applied.</summary>
    [RelayCommand]
    private void Skip() => _resolve(null);
}
