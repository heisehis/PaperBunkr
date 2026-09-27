using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Compare;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Two-edition compare (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11): the pure aligner and badges, the screen's view model over two real files, its entry points and the decisions.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CompareTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_compare_test_{Guid.NewGuid():N}.db");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"paperbunkr_compare_files_{Guid.NewGuid():N}");
    private readonly int _seriesId;
    private readonly int _a;
    private readonly int _b;
    private readonly int _c;

    /// <summary>A pseudo-random 64-bit picture hash for "picture number k": different pictures differ in about half their bits, the same picture not at all.</summary>
    private static long Picture(int k) => unchecked((long)(0x9E3779B97F4A7C15UL * (ulong)(k + 1)));

    public CompareTests()
    {
        Directory.CreateDirectory(_folder);
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Twins" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        string pathA = Path.Combine(_folder, "Twins 01 (low).cbz");
        string pathB = Path.Combine(_folder, "Twins 01 (high).cbz");
        string pathC = Path.Combine(_folder, "Twins 01 (third).cbz");
        CbzFixture.Create(pathA, pageCount: 4, pageSize: _ => new System.Drawing.Size(200, 300));
        CbzFixture.Create(pathB, pageCount: 5, pageSize: _ => new System.Drawing.Size(600, 900));   // a bigger scan with an extra page at the front
        CbzFixture.Create(pathC, pageCount: 4, pageSize: _ => new System.Drawing.Size(300, 450));
        var a = new Issue { SeriesId = series.Id, Number = "1", FilePath = pathA };
        var b = new Issue { SeriesId = series.Id, Number = "1", FilePath = pathB };
        var c = new Issue { SeriesId = series.Id, Number = "1", FilePath = pathC };
        context.Issues.AddRange(a, b, c);
        context.SaveChanges();
        _a = a.Id;
        _b = b.Id;
        _c = c.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; } = true;

        public string? LastMessage { get; private set; }

        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(Answer ? 0 : 1);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool isDestructive = false)
        {
            LastMessage = message;
            return Task.FromResult(Answer);
        }
    }

    /// <summary>A view model whose page hashes come from a table: A's page i is picture i, B's page 0 is an extra cover and B's page j is picture j - 1.</summary>
    private CompareScreenViewModel NewVm(Action? goBack = null, List<(string, string)>? toasts = null, FakeDialogs? dialogs = null) =>
        new(goBack ?? (() => { }), (t, m) => toasts?.Add((t, m)), dialogs)
        {
            PageHashProvider = (path, page) => path.Contains("(high)") ? (page == 0 ? Picture(99) : Picture(page - 1)) : Picture(page),
        };

    /// <summary>Waits for the background pairing and runs what it posted to the UI thread (the test bodies run on the pinned UI thread, so they wait rather than await).</summary>
    private static void Settle(CompareScreenViewModel vm)
    {
        vm.AlignmentTask.GetAwaiter().GetResult();
        TestDispatcher.Drain();
    }

    private static void Run(Task task) => task.GetAwaiter().GetResult();

    // ===== The aligner and the badges =====

    [Fact]
    public void FindMatch_FindsThePartnerAcrossAnExtraPage_WithinTheWindow()
    {
        long?[] b = [Picture(99), Picture(0), Picture(1), Picture(2)];        // B has an extra front cover

        var match = PageAligner.FindMatch(Picture(0), pageA: 0, offset: 0, pageCountB: 4, page => b[page]);

        Assert.Equal(1, match.PageB);
        Assert.Equal(0, match.Distance);
        Assert.True(match.IsMatched);
    }

    [Fact]
    public void FindMatch_NothingClose_FallsBackToTheExpectedPage_Unmatched()
    {
        long?[] b = [Picture(50), Picture(51), Picture(52)];

        var match = PageAligner.FindMatch(Picture(0), pageA: 1, offset: 0, pageCountB: 3, page => b[page]);

        Assert.False(match.IsMatched);
        Assert.Equal(1, match.PageB);                                           // the same number
    }

    [Fact]
    public void FindMatch_HonoursTheOffset_TheWindow_AndUnknownHashes()
    {
        long?[] b = new long?[30];
        b[20] = Picture(0);                                                     // the partner is far outside the window of page 0
        Assert.False(PageAligner.FindMatch(Picture(0), 0, 0, 30, page => b[page]).IsMatched);
        Assert.True(PageAligner.FindMatch(Picture(0), 0, offset: 18, 30, page => b[page]).IsMatched);   // an offset moves the window there
        Assert.Equal(20, PageAligner.FindMatch(Picture(0), 0, offset: 18, 30, page => b[page]).PageB);

        Assert.False(PageAligner.FindMatch(Picture(0), 0, 0, 3, _ => null).IsMatched);                  // hashes not known yet
    }

    [Fact]
    public void FindMatch_ATieGoesToThePageNearestTheExpectedOne()
    {
        long?[] b = [Picture(0), Picture(5), Picture(0)];

        var match = PageAligner.FindMatch(Picture(0), pageA: 2, offset: 0, pageCountB: 3, page => b[page]);

        Assert.Equal(2, match.PageB);
    }

    [Fact]
    public void ExpectedPage_IsKeptInsideB()
    {
        Assert.Equal(0, PageAligner.Expected(0, -5, 10));
        Assert.Equal(9, PageAligner.Expected(8, 5, 10));
        Assert.Equal(0, PageAligner.Expected(3, 3, 0));
    }

    [Fact]
    public void Badges_NeedAClearMargin_AndNameTheWinner()
    {
        var small = new EditionFacts("CBZ", 10_000_000, 20, 800, 1200);
        var big = new EditionFacts("CBZ", 30_000_000, 22, 1600, 2400);
        var same = new EditionFacts("CBZ", 10_100_000, 20, 810, 1215);

        Assert.Equal(FactsWinner.B, EditionComparison.HigherResolution(small, big));
        Assert.Equal(FactsWinner.A, EditionComparison.LargerFile(big, small));
        Assert.Equal(FactsWinner.B, EditionComparison.MorePages(small, big));
        Assert.Equal(FactsWinner.None, EditionComparison.HigherResolution(small, same));                // within 5%
        Assert.Equal(FactsWinner.None, EditionComparison.LargerFile(small, same));
        Assert.Equal(FactsWinner.None, EditionComparison.MorePages(small, same));
        Assert.Equal("28.6 MB", big.SizeLabel);                              // binary megabytes, like Explorer
        Assert.Equal("1600 × 2400", big.ResolutionLabel);
        Assert.Equal(1_363_636, big.BytesPerPage);
    }

    [Fact]
    public void PageHashCache_ComputesEachPageOnce_AndReportsWhatIsKnown()
    {
        int computed = 0;
        var cache = new PageHashCache(3, page => { computed++; return page == 1 ? null : Picture(page); });

        Assert.Null(cache.TryGet(0));
        Assert.False(cache.IsKnown(0));
        Assert.Equal(Picture(0), cache.Get(0));
        Assert.Equal(Picture(0), cache.Get(0));
        Assert.Null(cache.Get(1));
        Assert.True(cache.IsKnown(1));                                          // "could not be hashed" is remembered too
        Assert.Null(cache.Get(9));                                              // out of range
        Assert.Equal(2, computed);
    }

    // ===== The view model =====

    [Fact]
    public void Open_ShowsBothEditions_PairsAByContent_AndBuildsTheFacts()
    {
        using var vm = NewVm();

        Assert.True(vm.Open(_a, [_b]));
        Settle(vm);

        Assert.True(vm.IsOpen);
        Assert.Equal("Twins 01 (low).cbz", vm.TitleA);
        Assert.Equal("Twins 01 (high).cbz", vm.TitleB);
        Assert.NotNull(vm.PageA);
        Assert.NotNull(vm.PageB);
        Assert.Equal(1, vm.PageIndexB);                                        // A's cover pairs with B's second page: B has an extra front page
        Assert.True(vm.IsMatched);
        Assert.StartsWith("Matched", vm.MatchText);
        Assert.Equal(4, vm.FactsA!.PageCount);
        Assert.Equal(5, vm.FactsB!.PageCount);
        Assert.Equal(200, vm.FactsA.PageWidth);
        Assert.Equal(600, vm.FactsB.PageWidth);
        Assert.Equal("Higher resolution", vm.BadgesB.Split(" · ")[0]);
        Assert.Contains("More pages", vm.BadgesB);
        Assert.Equal(string.Empty, vm.BadgesA);
        Assert.Contains("A: page 1 / 4", vm.PageLabel);
        Assert.Contains("B: page 2 / 5", vm.PageLabel);
    }

    [Fact]
    public void TurningThePage_MovesBothSides_AndTheMatchKeepsUp()
    {
        using var vm = NewVm();
        vm.Open(_a, [_b]);
        Settle(vm);

        vm.NextPageCommand.Execute(null);
        Settle(vm);
        Assert.Equal(1, vm.PageIndex);
        Assert.Equal(2, vm.PageIndexB);

        vm.PreviousPageCommand.Execute(null);
        vm.PreviousPageCommand.Execute(null);                                   // already on the first page: stays
        Settle(vm);
        Assert.Equal(0, vm.PageIndex);

        for (int i = 0; i < 6; i++)
        {
            vm.NextPageCommand.Execute(null);
        }

        Settle(vm);
        Assert.Equal(3, vm.PageIndex);                                          // the last page of A
    }

    [Fact]
    public void WhenNothingMatches_TheExpectedPageIsShown_AndTheOffsetMovesThePairing_ForTheVisit()
    {
        using var vm = NewVm();
        vm.PageHashProvider = (path, page) => path.Contains("(high)") ? Picture(1000 + page) : Picture(page);   // nothing in B looks like A
        vm.Open(_a, [_b]);
        Settle(vm);
        Assert.False(vm.IsMatched);
        Assert.Equal(0, vm.PageIndexB);
        Assert.Contains("No matching page", vm.MatchText);

        vm.OffsetPlusCommand.Execute(null);
        Settle(vm);
        Assert.Equal(1, vm.Offset);
        Assert.Equal(1, vm.PageIndexB);
        Assert.Equal("Offset +1", vm.OffsetText);

        vm.Open(_a, [_b]);                                                      // reopening the same pair remembers the offset
        Settle(vm);
        Assert.Equal(1, vm.Offset);

        vm.OffsetResetCommand.Execute(null);
        Settle(vm);
        Assert.Equal(0, vm.PageIndexB);
    }

    [Fact]
    public void Flicker_GivesTheOneCanvasTheWholeWidth_AndFlipsBetweenTheEditions()
    {
        using var vm = NewVm();
        vm.Open(_a, [_b]);
        Settle(vm);
        Assert.Equal(1, vm.LeftColumnSpan);
        Assert.Same(vm.PageA, vm.LeftPage);

        vm.ToggleModeCommand.Execute(null);
        Assert.True(vm.IsFlicker);
        Assert.Equal(2, vm.LeftColumnSpan);
        Assert.Same(vm.PageA, vm.LeftPage);

        vm.FlipCommand.Execute(null);
        Assert.Same(vm.PageB, vm.LeftPage);
        Assert.Equal("B", vm.FlickerLabel);
        vm.FlipCommand.Execute(null);
        Assert.Same(vm.PageA, vm.LeftPage);

        vm.ToggleModeCommand.Execute(null);
        vm.FlipCommand.Execute(null);                                           // flipping means nothing side by side
        Assert.Same(vm.PageA, vm.LeftPage);
    }

    [Fact]
    public void AGroupOfThree_OffersTheOtherCopiesInTurn()
    {
        using var vm = NewVm();
        vm.Open(_a, [_b, _c]);
        Settle(vm);
        Assert.True(vm.HasSecondCandidate);
        Assert.Equal("Twins 01 (high).cbz", vm.TitleB);

        vm.NextCandidateCommand.Execute(null);
        Settle(vm);
        Assert.Equal("Twins 01 (third).cbz", vm.TitleB);
        Assert.Equal(4, vm.FactsB!.PageCount);

        vm.NextCandidateCommand.Execute(null);
        Assert.Equal("Twins 01 (high).cbz", vm.TitleB);                         // round again
    }

    [Fact]
    public void Open_RefusesTheSameComicTwice_AMissingFile_AndARemoteOne()
    {
        var toasts = new List<(string, string)>();
        using var vm = NewVm(toasts: toasts);

        Assert.False(vm.Open(_a, [_a]));
        Assert.True(vm.HasError);

        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Single(i => i.Id == _c);
            File.Delete(issue.FilePath!);
        }

        Assert.False(vm.Open(_a, [_c]));
        Assert.Contains(toasts, t => t.Item1 == "Can't compare this one");
        Assert.False(vm.IsOpen);
    }

    // ===== Deciding =====

    [Fact]
    public void KeepA_AfterConfirmation_RemovesBAndItsFile_AndGoesBack()
    {
        var dialogs = new FakeDialogs();
        int back = 0;
        var toasts = new List<(string, string)>();
        using var vm = NewVm(goBack: () => back++, toasts: toasts, dialogs: dialogs);
        vm.Open(_a, [_b]);
        Settle(vm);
        string pathB;
        using (var context = PaperbunkrDb.CreateContext())
        {
            pathB = context.Issues.Single(i => i.Id == _b).FilePath!;
        }

        // Nothing is deleted when the confirmation is declined.
        dialogs.Answer = false;
        Run(vm.KeepACommand.ExecuteAsync(null));
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.True(context.Issues.Any(i => i.Id == _b));
        }

        Assert.Equal(0, back);
        Assert.Contains("Twins 01 (low).cbz", dialogs.LastMessage);               // the confirmation names both files
        Assert.Contains("Twins 01 (high).cbz", dialogs.LastMessage);

        dialogs.Answer = true;
        Run(vm.KeepACommand.ExecuteAsync(null));

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.False(context.Issues.Any(i => i.Id == _b));
            Assert.True(context.Issues.Any(i => i.Id == _a));
        }

        Assert.Equal(1, back);
        Assert.Contains(toasts, t => t.Item1.StartsWith("Kept"));
        Assert.False(File.Exists(pathB));                                         // to the Recycle Bin (the file is gone from its folder)
    }

    [Fact]
    public void KeepB_RemovesA_AndWithoutADialogNothingIsDeleted()
    {
        using (var noDialog = NewVm())
        {
            noDialog.Open(_a, [_b]);
            Settle(noDialog);
            Run(noDialog.KeepBCommand.ExecuteAsync(null));
            using var context = PaperbunkrDb.CreateContext();
            Assert.Equal(3, context.Issues.Count());
        }

        var dialogs = new FakeDialogs();
        using var vm = NewVm(dialogs: dialogs);
        vm.Open(_a, [_b]);
        Settle(vm);

        Run(vm.KeepBCommand.ExecuteAsync(null));

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.False(context.Issues.Any(i => i.Id == _a));
            Assert.True(context.Issues.Any(i => i.Id == _b));
        }
    }

    [Fact]
    public void NotADuplicate_MarksBothAsReviewed_AndKeepsTheFiles()
    {
        int back = 0;
        using var vm = NewVm(goBack: () => back++);
        vm.Open(_a, [_b]);
        Settle(vm);

        vm.NotADuplicateCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.True(context.Issues.Single(i => i.Id == _a).DuplicateAcknowledged);
        Assert.True(context.Issues.Single(i => i.Id == _b).DuplicateAcknowledged);
        Assert.False(context.Issues.Single(i => i.Id == _c).DuplicateAcknowledged);
        Assert.Equal(1, back);
        Assert.True(File.Exists(context.Issues.Single(i => i.Id == _b).FilePath));
    }

    [Fact]
    public void Closing_ChangesNothing_AndReleasesTheFiles()
    {
        int back = 0;
        using var vm = NewVm(goBack: () => back++);
        vm.Open(_a, [_b]);
        Settle(vm);

        vm.CloseCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Equal(1, back);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(3, context.Issues.Count());
        Assert.False(context.Issues.Any(i => i.DuplicateAcknowledged));
    }

    // ===== Entry points =====

    [Fact]
    public void TheDuplicateReviewRow_ComparesTheKeptCopyWithTheOthers()
    {
        int? keep = null;
        IReadOnlyList<int>? others = null;
        var review = new NeedsReviewViewModel(_ => { });
        review.CompareRequested += (k, o) => { keep = k; others = o; };
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues) issue.Format = "TPB";
            context.SaveChanges();
        }

        review.Refresh();
        var group = Assert.Single(review.DuplicateGroupItems);
        group.CompareCommand.Execute(null);

        Assert.Equal(group.Candidates.First(c => c.IsKeep).IssueId, keep);
        Assert.Equal(2, others!.Count);
        Assert.DoesNotContain(keep!.Value, others);
    }

    private static IssueListRow Row(int id, int seriesId) => new()
    {
        Id = id,
        SeriesId = seriesId,
        SeriesName = "Twins",
        Title = "T",
        CoverBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Gray),
    };

    [Fact]
    public void TheLibraryCommand_NeedsExactlyTwoLocalComics()
    {
        var toasts = new List<(string, string)>();
        var library = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { }, showToast: (t, m) => toasts.Add((t, m)), loadOnConstruction: false);
        (int, IReadOnlyList<int>)? requested = null;
        library.CompareRequested += (a, o) => requested = (a, o);
        var rows = new List<IssueListRow> { Row(_a, _seriesId), Row(_b, _seriesId), Row(_c, _seriesId) };

        library.CompareSelectedIssuesCommand.Execute(_a);                       // nothing selected: just the clicked one
        Assert.Null(requested);
        Assert.Contains(toasts, t => t.Item1 == "Select two comics to compare");

        library.Selection.SelectAll(rows);                                      // three selected: still refused
        library.CompareSelectedIssuesCommand.Execute(_a);
        Assert.Null(requested);

        library.Selection.Clear(rows);
        library.Selection.Toggle(rows, rows[0], isShiftHeld: false);
        library.Selection.Toggle(rows, rows[1], isShiftHeld: false);
        library.CompareSelectedIssuesCommand.Execute(_a);

        Assert.NotNull(requested);
        Assert.Equal(new[] { _a, _b }.OrderBy(i => i), new[] { requested!.Value.Item1 }.Concat(requested.Value.Item2).OrderBy(i => i));

        using (var context = PaperbunkrDb.CreateContext())
        {
            File.Delete(context.Issues.Single(i => i.Id == _b).FilePath!);      // a missing file is refused
        }

        requested = null;
        library.CompareSelectedIssuesCommand.Execute(_a);
        Assert.Null(requested);
        Assert.Contains(toasts, t => t.Item1 == "Can't compare these");
    }

    [Fact]
    public void TheShell_OpensTheScreen_HidesTheRail_AndBackLeavesIt()
    {
        var shell = new MainViewModel();
        shell.Compare.PageHashProvider = (path, page) => Picture(page);
        shell.Library.CompareSelectedIssuesCommand.CanExecute(_a);
        shell.NeedsReview.CompareRequested += (_, _) => { };

        // The Library raises CompareRequested; the shell listens.
        var rows = new List<IssueListRow> { Row(_a, _seriesId), Row(_b, _seriesId) };
        shell.Library.Selection.SelectAll(rows);
        shell.Library.CompareSelectedIssuesCommand.Execute(_a);
        for (int i = 0; i < 100 && !shell.IsCompare; i++)
        {
            TestDispatcher.Drain();
            Thread.Sleep(20);
        }

        Assert.True(shell.IsCompare);
        Assert.True(shell.IsInReader);                                              // full-bleed: the rail and status bar hide
        Assert.Same(shell.Compare, shell.ActiveDrillDownContent);
        Assert.True(shell.Compare.IsOpen);

        shell.EscapeCommand.Execute(null);                                          // Escape closes it
        for (int i = 0; i < 100 && shell.IsCompare; i++)
        {
            TestDispatcher.Drain();
            Thread.Sleep(20);
        }

        Assert.False(shell.IsCompare);
        Assert.False(shell.Compare.IsOpen);
    }

    // ===== On screen =====

    [Fact]
    public void TheScreen_ShowsTwoCanvases_LinkedZoom_AndTakesItsKeys()
    {
        var vm = NewVm();
        vm.Open(_a, [_b]);
        Settle(vm);
        var screen = ReaderScreenTestHost_CreateCompare(vm);
        var window = new Avalonia.Controls.Window { Width = 1200, Height = 800, Content = screen };
        window.Show();
        try
        {
            TestDispatcher.Drain();
            window.UpdateLayout();
            var canvasA = screen.CanvasA;
            var canvasB = screen.CanvasB;
            Assert.NotNull(canvasA.Page);
            Assert.NotNull(canvasB.Page);
            Assert.True(canvasB.IsVisible);

            vm.ZoomLevel = 2.0;                                                     // one shared zoom: both canvases follow
            TestDispatcher.Drain();
            Assert.Equal(2.0, canvasA.ZoomLevel);
            Assert.Equal(2.0, canvasB.ZoomLevel);

            ReaderScreenTestHost.Press(window, Key.Right);
            Settle(vm);
            Assert.Equal(1, vm.PageIndex);

            ReaderScreenTestHost.Press(window, Key.M);
            TestDispatcher.Drain();
            Assert.True(vm.IsFlicker);
            Assert.False(canvasB.IsVisible);
            ReaderScreenTestHost.Press(window, Key.Space);
            TestDispatcher.Drain();
            Assert.True(vm.ShowingB);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    private static CompareScreen ReaderScreenTestHost_CreateCompare(CompareScreenViewModel vm)
    {
        var resources = Avalonia.Application.Current!.Resources;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new CompareScreen { DataContext = vm };
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex.InnerException is KeyNotFoundException)
            {
                var match = System.Text.RegularExpressions.Regex.Match(ex.ToString(), @"Static resource '([^']+)' not found");
                if (!match.Success)
                {
                    throw;
                }

                string key = match.Groups[1].Value;
                resources[key] = key switch
                {
                    _ when key.Contains("Ease") => new Avalonia.Animation.Easings.CubicEaseOut(),
                    _ when key.Contains("Motion") || key.Contains("Duration") => TimeSpan.FromMilliseconds(150),
                    _ when key.Contains("Radius") => new Avalonia.CornerRadius(4),
                    _ when key.Contains("Brush") => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Gray),
                    _ when key.Contains("Thickness") || key.Contains("Padding") => new Avalonia.Thickness(4),
                    _ => 12.0,
                };
            }
        }

        throw new InvalidOperationException("Too many missing resources.");
    }
}
