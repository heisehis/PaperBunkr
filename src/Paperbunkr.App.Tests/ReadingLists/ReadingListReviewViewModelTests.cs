using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.App.Tests.ReadingLists;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class ReadingListReviewViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "pb-review-ui-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly Dialogs _dialogs = new();
    public ReadingListReviewViewModelTests() { using var db = Open(); db.Database.EnsureCreated(); }
    private PaperbunkrDbContext Open() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options);
    private ReadingListReviewViewModel Create() => new(_dialogs, () => { }, () => { }, Open);
    private static PreparedImport Input => new([new PreparedReadingList("Test list", [new("Avengers", "1", Year: 2020), new("Unknown", "2")])], []);
    private int Seed(string name)
    {
        using var db = Open();
        var list = new ReadingList { Name = name };
        var resolution = ReadingListResolver.Resolve(db, [new ComicReference("Avengers", "1", Year: 2020)]);
        foreach (var r in ReadingListResolver.Materialize(db, resolution)) list.Items.Add(new() { Issue = r.Issue, Source = r.Source });
        db.ReadingLists.Add(list); db.SaveChanges(); return list.Id;
    }
    private void AddComic()
    {
        using var db = Open(); db.Issues.Add(new() { Series = new Series { Name = "Avengers" }, Number = "1", Year = 2025, FilePath = "test.cbz" }); db.SaveChanges();
    }
    [Fact] public async Task ImportStagesWithoutWritingAndExitHonorsDraftConfirmation()
    {
        var vm = Create(); await vm.StartImportAsync(Input, null);
        Assert.True(vm.HasDraft); Assert.True(vm.IsImport); Assert.Equal(2, vm.Rows.Count);
        using (var db = Open()) { Assert.Empty(db.ReadingLists); Assert.Empty(db.Issues); }
        _dialogs.Accept = false; Assert.False(await vm.ConfirmExitAsync()); Assert.False(await vm.CanStartAsync()); Assert.Equal(2, vm.Rows.Count);
        _dialogs.Accept = true; Assert.True(await vm.CanStartAsync()); Assert.False(vm.HasDraft);
    }
    [Fact] public async Task ImportOmissionAndApplyUpdateDraftState()
    {
        var vm = Create(); await vm.StartImportAsync(Input, null); vm.KeepUnresolved = false;
        Assert.Contains("2 excluded", vm.Summary);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.False(vm.HasDraft); Assert.Contains("Imported 1", vm.Status);
        using var db = Open(); Assert.Single(db.ReadingLists); Assert.Empty(db.Issues);
    }
    [Fact] public async Task CrossListChoicesSurviveRecheckAndApplyAsOneBatch()
    {
        int a = Seed("First"), b = Seed("Second"); AddComic();
        var vm = Create(); await vm.StartReviewAsync([a]); await vm.SearchCommand.ExecuteAsync(null);
        vm.ChooseCommand.Execute(vm.Candidates.Single());
        await vm.ApplyElsewhereCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count); Assert.All(vm.Rows, r => Assert.NotNull(r.Choice));
        await vm.RecheckCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count); Assert.All(vm.Rows, r => Assert.NotNull(r.Choice));
        await vm.ApplyCommand.ExecuteAsync(null);
        using var db = Open(); Assert.Single(db.ReadingListReviewBatches); Assert.All(db.ReadingListItems.Include(i => i.Issue), i => Assert.False(i.Issue!.IsPlaceholder));
        Assert.True(vm.HasImmediateUndo);
        await vm.UndoLastCommand.ExecuteAsync(null);
        db.ChangeTracker.Clear(); Assert.All(db.ReadingListItems.Include(i => i.Issue), i => Assert.True(i.Issue!.IsPlaceholder));
        Assert.NotEmpty(vm.Rows);
    }
    [Fact] public async Task GallerySelectorReviewsOnlyChosenLists()
    {
        int a = Seed("First"), b = Seed("Second");
        var vm = Create(); await vm.StartReviewAsync([]); Assert.True(vm.IsSelector);
        vm.Lists.Single(l => l.Id == b).Selected = true;
        await vm.ReviewSelectedCommand.ExecuteAsync(null);
        Assert.False(vm.IsSelector); Assert.Equal(b, vm.Rows.Single().Entry!.ListId);
    }
    [Fact] public async Task ImportCanStageEquivalentExistingEntriesAndUndoOnlyThoseLinks()
    {
        int existing = Seed("Existing"); AddComic();
        var vm = Create();
        await vm.StartImportAsync(new PreparedImport([new("Imported", [new("Avengers", "1", Year: 2020)])], []), null);
        await vm.SearchCommand.ExecuteAsync(null); vm.ChooseCommand.Execute(vm.Candidates.Single());
        await vm.ApplyElsewhereCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Rows.Count);
        await vm.ApplyCommand.ExecuteAsync(null);
        using (var db = Open()) { Assert.Equal(2, db.ReadingLists.Count()); Assert.Single(db.ReadingListReviewBatches); }
        await vm.UndoLastCommand.ExecuteAsync(null);
        using (var db = Open())
        {
            Assert.True(db.ReadingListItems.Include(i => i.Issue).Single(i => i.ReadingListId == existing).Issue!.IsPlaceholder);
            Assert.False(db.ReadingListItems.Include(i => i.Issue).Single(i => i.ReadingListId != existing).Issue!.IsPlaceholder);
        }
    }
    [Theory]
    [InlineData("default", 750)] [InlineData("daylight", 750)] [InlineData("matrix", 750)]
    [InlineData("default", 1100)] [InlineData("daylight", 1100)] [InlineData("matrix", 1100)]
    public async Task ReviewRendersWithCompactQueueAndAccessibleSearch(string theme, int width)
    {
        var vm = Create(); await vm.StartImportAsync(Input, null);
        await vm.SearchCommand.ExecuteAsync(null);
        PinnedThread.Run(() =>
        {
        Theming.FoundationStyleTests.WithFoundation(theme, () =>
        {
            var view = new ReadingListReviewView { DataContext = vm };
            var window = new Window { Content = view, Width = width, Height = 650 };
            window.Show(); window.UpdateLayout(); TestDispatcher.Drain();
            Assert.Equal(width < 900, vm.Compact);
            var search = view.FindControl<TextBox>("ReviewSearch")!;
            Assert.NotNull(Avalonia.Automation.AutomationProperties.GetLabeledBy(search));
            var keep = view.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Keep unresolved entries"));
            Assert.Equal(((Avalonia.Media.ISolidColorBrush)Application.Current!.FindResource("PbTextBrush")!).Color,
                ((Avalonia.Media.ISolidColorBrush)keep.Foreground!).Color);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Apply") && b.IsEffectivelyVisible);
            if (width < 900)
            {
                Assert.False(view.FindControl<ListBox>("EntryQueue")!.IsEffectivelyVisible);
                vm.ShowQueueCommand.Execute(null); window.UpdateLayout(); TestDispatcher.Drain();
                Assert.True(view.FindControl<ListBox>("EntryQueue")!.IsEffectivelyVisible);
            }
            string? output = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, 650)); bitmap.Render(window);
                bitmap.Save(Path.Combine(output, $"review-{theme}-{width}.png"));
            }
            window.Close();
        });
        return 0;
        });
    }
    private sealed class Dialogs : IDialogService
    {
        public bool Accept { get; set; } = true;
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(Accept ? 0 : 1);
        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(Accept);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_path); }
}
