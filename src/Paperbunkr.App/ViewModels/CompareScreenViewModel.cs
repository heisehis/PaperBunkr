using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.AdDetection;
using Paperbunkr.App.Services.Compare;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>How the two editions are shown.</summary>
public enum CompareMode
{
    SideBySide,
    Flicker,
}

/// <summary>One of the two files being compared: its own pipeline (decode, cache), page hashes and the facts the strip shows.</summary>
public sealed class EditionSide : IDisposable
{
    public EditionSide(int issueId, string filePath, string label, ReaderImagePipeline pipeline, PageHashCache hashes, string format, long fileSize)
    {
        IssueId = issueId;
        FilePath = filePath;
        Label = label;
        Pipeline = pipeline;
        Hashes = hashes;
        Format = format;
        FileSize = fileSize;
    }

    public int IssueId { get; }

    public string FilePath { get; }

    /// <summary>The file name, for headings.</summary>
    public string Label { get; }

    public ReaderImagePipeline Pipeline { get; }

    public PageHashCache Hashes { get; }

    public string Format { get; }

    public long FileSize { get; }

    public int PageCount => Pipeline.PageCount;

    public void Dispose() => Pipeline.Dispose();
}

/// <summary>
/// The Compare screen (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11): two editions of one book side by side or as a flicker toggle, with pages paired by content (dHash) so a different page count
/// does not misalign them, linked zoom and pan, a facts strip, and Keep A / Keep B / Not a duplicate. Nothing is deleted without a confirmation naming both files, and closing the screen changes nothing.
/// </summary>
public sealed partial class CompareScreenViewModel : ObservableObject, IDisposable
{
    private readonly Action _goBack;
    private readonly Action<string, string> _showToast;
    private readonly IDialogService? _dialogs;
    private readonly Dictionary<(int A, int B), int> _offsets = new();

    private EditionSide? _sideA;
    private EditionSide? _sideB;
    private List<int> _candidateIds = new();
    private int _candidateIndex;
    private int _alignRequest;

    public CompareScreenViewModel(Action goBack, Action<string, string>? showToast = null, IDialogService? dialogs = null)
    {
        _goBack = goBack;
        _showToast = showToast ?? ((_, _) => { });
        _dialogs = dialogs;
    }

    /// <summary>Hashes a page of a file (a test seam: the default decodes the page and takes its dHash).</summary>
    internal Func<string, int, long?> PageHashProvider { get; set; } = DefaultHash;

    private static long? DefaultHash(string path, int page)
    {
        using var bitmap = PageDecodeCore.DecodeSinglePage(path, page);
        return bitmap is null ? null : PageHasher.TryCompute(bitmap);
    }

    /// <summary>The last alignment started; tests await it.</summary>
    internal Task AlignmentTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPage), nameof(PageLabel))]
    private int _pageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffsetText))]
    private int _offset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPage), nameof(PageLabel))]
    private int _pageIndexB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPage))]
    private Bitmap? _pageA;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPage))]
    private Bitmap? _pageB;

    [ObservableProperty]
    private string _matchText = string.Empty;

    [ObservableProperty]
    private bool _isMatched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlicker), nameof(IsSideBySide), nameof(LeftPage), nameof(LeftColumnSpan), nameof(ModeLabel))]
    private CompareMode _mode = CompareMode.SideBySide;

    /// <summary>Flicker mode: B is showing in the single canvas (else A).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPage), nameof(FlickerLabel))]
    private bool _showingB;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private double _panOffsetX;

    [ObservableProperty]
    private double _panOffsetY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecondCandidate))]
    private string _titleA = string.Empty;

    [ObservableProperty]
    private string _titleB = string.Empty;

    [ObservableProperty]
    private EditionFacts? _factsA;

    [ObservableProperty]
    private EditionFacts? _factsB;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public bool IsFlicker => Mode == CompareMode.Flicker;

    public bool IsSideBySide => Mode == CompareMode.SideBySide;

    public string ModeLabel => IsFlicker ? "Flicker" : "Side by side";

    /// <summary>The bitmap of the left (or only) canvas: A, or B while flickering to B.</summary>
    public Bitmap? LeftPage => IsFlicker && ShowingB ? PageB : PageA;

    /// <summary>Flicker mode gives the one canvas the whole width.</summary>
    public int LeftColumnSpan => IsFlicker ? 2 : 1;

    public string FlickerLabel => ShowingB ? "B" : "A";

    public string OffsetText => Offset == 0 ? "Offset 0" : $"Offset {Offset:+#;-#;0}";

    public bool HasSecondCandidate => _candidateIds.Count > 1;

    public string PageLabel => _sideA is null ? string.Empty : $"A: page {PageIndex + 1} / {_sideA.PageCount}  ·  B: page {PageIndexB + 1} / {_sideB?.PageCount ?? 0}";

    // --- facts strip badges ---

    public FactsWinner ResolutionWinner => FactsA is { } a && FactsB is { } b ? EditionComparison.HigherResolution(a, b) : FactsWinner.None;

    public FactsWinner FileSizeWinner => FactsA is { } a && FactsB is { } b ? EditionComparison.LargerFile(a, b) : FactsWinner.None;

    public FactsWinner PageCountWinner => FactsA is { } a && FactsB is { } b ? EditionComparison.MorePages(a, b) : FactsWinner.None;

    public string BadgesA => Badges(FactsWinner.A);

    public string BadgesB => Badges(FactsWinner.B);

    private string Badges(FactsWinner side)
    {
        var badges = new List<string>();
        if (ResolutionWinner == side) badges.Add("Higher resolution");
        if (FileSizeWinner == side) badges.Add("Larger file");
        if (PageCountWinner == side) badges.Add("More pages");
        return string.Join(" · ", badges);
    }

    partial void OnFactsAChanged(EditionFacts? value) => OnFactsChanged();

    partial void OnFactsBChanged(EditionFacts? value) => OnFactsChanged();

    private void OnFactsChanged()
    {
        OnPropertyChanged(nameof(ResolutionWinner));
        OnPropertyChanged(nameof(FileSizeWinner));
        OnPropertyChanged(nameof(PageCountWinner));
        OnPropertyChanged(nameof(BadgesA));
        OnPropertyChanged(nameof(BadgesB));
    }

    // ===================== Opening =====================

    /// <summary>
    /// Opens the screen for <paramref name="issueAId"/> against the first of <paramref name="otherIds"/> (a duplicate group with more than two members offers the others with <see cref="NextCandidateCommand"/>). Returns
    /// false (with a toast) when either issue is remote, has no file, or the file cannot be opened.
    /// </summary>
    public bool Open(int issueAId, IReadOnlyList<int> otherIds)
    {
        CloseSides();
        ErrorMessage = string.Empty;
        _candidateIds = otherIds.Where(id => id != issueAId).Distinct().ToList();
        _candidateIndex = 0;
        if (_candidateIds.Count == 0)
        {
            ErrorMessage = "Pick two different comics to compare.";
            return false;
        }

        var a = OpenSide(issueAId);
        if (a is null)
        {
            return false;
        }

        _sideA = a;
        TitleA = a.Label;
        if (!OpenCandidate(0))
        {
            CloseSides();
            return false;
        }

        IsOpen = true;
        PageIndex = 0;
        ShowingB = false;
        Mode = CompareMode.SideBySide;
        ResetView();
        ShowCurrent();
        return true;
    }

    /// <summary>Pairs A with candidate <paramref name="index"/> of the others (used by Open and by the "next candidate" button).</summary>
    private bool OpenCandidate(int index)
    {
        _sideB?.Dispose();
        _sideB = null;
        var b = OpenSide(_candidateIds[index]);
        if (b is null)
        {
            return false;
        }

        _sideB = b;
        _candidateIndex = index;
        TitleB = b.Label;
        Offset = _offsets.TryGetValue((_sideA!.IssueId, b.IssueId), out int remembered) ? remembered : 0;
        OnPropertyChanged(nameof(HasSecondCandidate));
        RefreshFacts();
        return true;
    }

    private EditionSide? OpenSide(int issueId)
    {
        Issue? issue;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        }

        if (issue is null || issue.RemoteSourceId is not null || string.IsNullOrEmpty(issue.FilePath) || !File.Exists(issue.FilePath))
        {
            _showToast("Can't compare this one", "Compare works on two comics that are files on this computer.");
            ErrorMessage = "Compare works on two comics that are files on this computer.";
            return null;
        }

        var pipeline = ReaderImagePipeline.TryOpen(issue.FilePath);
        if (pipeline is null)
        {
            _showToast("Can't open the file", Path.GetFileName(issue.FilePath));
            ErrorMessage = $"Couldn't open {Path.GetFileName(issue.FilePath)}.";
            return null;
        }

        pipeline.SetViewportWidth(2000);
        string path = issue.FilePath;
        var hashes = new PageHashCache(pipeline.PageCount, page => PageHashProvider(path, page));
        long size = new FileInfo(path).Length;
        string format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return new EditionSide(issue.Id, path, Path.GetFileName(path), pipeline, hashes, format, size);
    }

    private void CloseSides()
    {
        _alignRequest++;
        PageA = null;
        PageB = null;
        _sideA?.Dispose();
        _sideB?.Dispose();
        _sideA = null;
        _sideB = null;
        FactsA = null;
        FactsB = null;
        IsOpen = false;
    }

    private void RefreshFacts()
    {
        if (_sideA is null || _sideB is null)
        {
            return;
        }

        FactsA = FactsFor(_sideA, PageIndex);
        FactsB = FactsFor(_sideB, PageIndexB);
    }

    private static EditionFacts FactsFor(EditionSide side, int page)
    {
        var size = side.Pipeline.PeekPageSize(Math.Clamp(page, 0, Math.Max(0, side.PageCount - 1)));
        return new EditionFacts(side.Format, side.FileSize, side.PageCount, size?.Width ?? 0, size?.Height ?? 0);
    }

    // ===================== Pages and alignment =====================

    /// <summary>Shows page <see cref="PageIndex"/> of A and (at once, at its default pairing) the matching page of B, then refines the pairing by content in the background.</summary>
    private void ShowCurrent()
    {
        if (_sideA is null || _sideB is null)
        {
            return;
        }

        PageIndex = Math.Clamp(PageIndex, 0, Math.Max(0, _sideA.PageCount - 1));
        PageA = TryGetPage(_sideA, PageIndex);
        ShowB(PageAligner.Expected(PageIndex, Offset, _sideB.PageCount));
        MatchText = "Matching…";
        IsMatched = false;
        StartAlignment();
    }

    private void ShowB(int pageB)
    {
        if (_sideB is null)
        {
            return;
        }

        PageIndexB = Math.Clamp(pageB, 0, Math.Max(0, _sideB.PageCount - 1));
        PageB = TryGetPage(_sideB, PageIndexB);
        RefreshFacts();
    }

    private static Bitmap? TryGetPage(EditionSide side, int page)
    {
        try
        {
            return side.Pipeline.GetPage(page);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private void StartAlignment()
    {
        if (_sideA is not { } a || _sideB is not { } b)
        {
            return;
        }

        int request = ++_alignRequest;
        int pageA = PageIndex;
        int offset = Offset;
        AlignmentTask = Task.Run(() =>
        {
            long? hashA = a.Hashes.Get(pageA);
            if (hashA is not { } hash)
            {
                return new PageMatch(PageAligner.Expected(pageA, offset, b.PageCount), null);
            }

            int expected = PageAligner.Expected(pageA, offset, b.PageCount);
            for (int page = Math.Max(0, expected - PageAligner.Window); page <= Math.Min(b.PageCount - 1, expected + PageAligner.Window); page++)
            {
                if (request != _alignRequest)
                {
                    return new PageMatch(expected, null);   // the user moved on: stop hashing pages nobody needs
                }

                b.Hashes.Get(page);
            }

            return PageAligner.FindMatch(hash, pageA, offset, b.PageCount, b.Hashes.TryGet);
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                return;
            }

            var match = task.Result;
            Dispatcher.UIThread.Post(() =>
            {
                if (request != _alignRequest)
                {
                    return;
                }

                ShowB(match.PageB);
                IsMatched = match.IsMatched;
                MatchText = match.IsMatched
                    ? (match.Distance == 0 ? "Matched: identical picture" : $"Matched by picture (difference {match.Distance} of 64)")
                    : $"No matching page found: showing B's page {match.PageB + 1} by number";
            });
        });
    }

    [RelayCommand]
    private void NextPage()
    {
        if (_sideA is not null && PageIndex < _sideA.PageCount - 1)
        {
            PageIndex++;
            ShowCurrent();
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (PageIndex > 0)
        {
            PageIndex--;
            ShowCurrent();
        }
    }

    [RelayCommand]
    private void GoToFirstPage()
    {
        PageIndex = 0;
        ShowCurrent();
    }

    /// <summary>Shifts B's default pairing by one page for the rest of the visit (when the automatic match keeps failing).</summary>
    [RelayCommand]
    private void OffsetPlus() => SetOffset(Offset + 1);

    [RelayCommand]
    private void OffsetMinus() => SetOffset(Offset - 1);

    [RelayCommand]
    private void OffsetReset() => SetOffset(0);

    private void SetOffset(int value)
    {
        if (_sideA is null || _sideB is null)
        {
            return;
        }

        Offset = Math.Clamp(value, -_sideB.PageCount, _sideB.PageCount);
        _offsets[(_sideA.IssueId, _sideB.IssueId)] = Offset;
        ShowCurrent();
    }

    // ===================== Modes and view =====================

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = IsFlicker ? CompareMode.SideBySide : CompareMode.Flicker;
        ShowingB = false;
    }

    /// <summary>Flicker mode: show the other edition in the same place.</summary>
    [RelayCommand]
    private void Flip()
    {
        if (IsFlicker)
        {
            ShowingB = !ShowingB;
        }
    }

    [RelayCommand]
    private void ResetView()
    {
        ZoomLevel = 1.0;
        PanOffsetX = 0;
        PanOffsetY = 0;
    }

    /// <summary>A duplicate group with more than two members: compare A against the next of the others.</summary>
    [RelayCommand]
    private void NextCandidate()
    {
        if (_candidateIds.Count < 2 || _sideA is null)
        {
            return;
        }

        int next = (_candidateIndex + 1) % _candidateIds.Count;
        if (OpenCandidate(next))
        {
            ShowCurrent();
        }
    }

    // ===================== Deciding =====================

    [RelayCommand]
    private Task KeepAAsync() => KeepAsync(keepA: true);

    [RelayCommand]
    private Task KeepBAsync() => KeepAsync(keepA: false);

    private async Task KeepAsync(bool keepA)
    {
        if (_sideA is not { } a || _sideB is not { } b)
        {
            return;
        }

        var keep = keepA ? a : b;
        var remove = keepA ? b : a;
        string message = $"Keep {Describe(keep)} and move {Describe(remove)} to the Recycle Bin?";
        if (_dialogs is null || !await _dialogs.ConfirmAsync(message, "Delete the other copy", "Move to Recycle Bin", "Cancel", isDestructive: true))
        {
            return;
        }

        int removeId = remove.IssueId;
        string removedName = remove.Label;
        CloseSides();   // release both files before one of them goes to the Recycle Bin
        DuplicateGroupResolver.RemoveIssues([removeId], deleteFile: true);
        _showToast("Kept " + keep.Label, $"{removedName} was moved to the Recycle Bin.");
        _goBack();
    }

    private static string Describe(EditionSide side)
    {
        var size = EditionFacts.FormatBytes(side.FileSize);
        return $"{side.Label} ({size})";
    }

    /// <summary>"Not a duplicate": hides the group (both files stay) and returns.</summary>
    [RelayCommand]
    private void NotADuplicate()
    {
        if (_sideA is not { } a || _sideB is not { } b)
        {
            return;
        }

        int[] ids = [a.IssueId, b.IssueId];
        DuplicateGroupResolver.Acknowledge(ids);
        _showToast("Marked as not a duplicate", "Both files stay in the library.");
        Close();
    }

    /// <summary>Leaves the screen without changing anything.</summary>
    [RelayCommand]
    public void Close()
    {
        CloseSides();
        _goBack();
    }

    /// <summary>Releases both files (the shell calls this when the screen is left by any route).</summary>
    public void Dispose() => CloseSides();
}
