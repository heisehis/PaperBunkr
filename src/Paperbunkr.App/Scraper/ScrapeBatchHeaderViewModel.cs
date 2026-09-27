using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine.Scraping;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Backs <see cref="ScrapeBatchHeaderView"/> - the persistent batch-progress header (docs/superpowers/
/// specs/2026-09-13-cluster-scraper-ui-redesign-design.md §4) shown above the per-book review dialogs
/// for the whole scrape run, via <c>INativePluginUiEnvironment.BeginModalBatch</c>. CE's own scraper
/// shows the same thing: the current book's own cover, its filename, a progress bar, and Cancel.
/// </summary>
public sealed partial class ScrapeBatchHeaderViewModel : ObservableObject
{
    private readonly Action _onCancel;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private int _currentIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private ScrapePhase _phase = ScrapePhase.Single;

    [ObservableProperty]
    private string _currentBookLabel = string.Empty;

    [ObservableProperty]
    private Bitmap? _currentCover;

    /// <summary>Two labeled phases (docs/superpowers/specs, 2026-09-25 grilling round - the
    /// Recommended option): a two-phase auto-choose run gets its own distinct label per phase instead
    /// of one continuous counter, since CE's own single growing "Book X of N" (its own worklist
    /// literally lengthens as books get deferred) would look like a glitch here - the total visibly
    /// shrinking back down when the Reviewing phase starts its own separate count.</summary>
    public string ProgressLabel => Phase switch
    {
        ScrapePhase.AutoMatching => $"Auto-matching {CurrentIndex} of {Total}",
        ScrapePhase.Reviewing => $"Reviewing {CurrentIndex} of {Total}",
        _ => $"Book {CurrentIndex} of {Total}",
    };

    public ScrapeBatchHeaderViewModel(int total, Action onCancel)
    {
        _total = total;
        _onCancel = onCancel;
    }

    /// <summary>Called once per book (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-
    /// design.md §4 - the orchestrator's own <c>onProgress</c> fires once per book, not once per
    /// dialog step) with that book's own local cover bytes from <c>IApplication.GetComicThumbnail</c>.
    /// A decode failure (or null bytes - no thumbnail cached yet) just leaves <see cref="CurrentCover"/>
    /// null, same resilience as everywhere else this plugin touches ComicVine's own remote covers.</summary>
    public void ReportProgress(ScrapePhase phase, int total, int index, string bookLabel, byte[]? coverBytes)
    {
        Phase = phase;
        Total = total;
        CurrentIndex = index;
        CurrentBookLabel = bookLabel;
        CurrentCover = DecodeOrNull(coverBytes);
    }

    private static Bitmap? DecodeOrNull(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
