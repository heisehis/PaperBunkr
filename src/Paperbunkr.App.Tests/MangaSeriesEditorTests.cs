using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Edit Series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D11): the editor's edit buffer, the real flow from the Manga Detail screen through the
/// overlay and back, and the real main window actually showing it. Throwaway database throughout.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MangaSeriesEditorTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;
    private readonly CoverCacheTestRedirect _cover;

    public MangaSeriesEditorTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_mangaeditor_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        _cover = new CoverCacheTestRedirect();
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        _cover.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static int SeedSeries(int chapters, Action<int, Issue>? perChapter = null, ContentType type = ContentType.Manga)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Test Manga", ContentType = type };
        for (int n = 1; n <= chapters; n++)
        {
            var issue = new Issue { FilePath = $"c{n}.cbz", Number = n.ToString(), Title = $"Chapter {n}", Publisher = "X", Writer = "A", PageCount = 10 };
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Action", Category = "Genre" });
            perChapter?.Invoke(n, issue);
            series.Issues.Add(issue);
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static MangaSeriesEditorViewModel Editor(Action? goBack = null, Action<int>? enqueue = null) =>
        new(goBack ?? (() => { }), new MangaSeriesMetadataService(PaperbunkrDb.CreateContext), enqueueWriteBack: enqueue);

    private static MangaFieldRowViewModel Row(MangaSeriesEditorViewModel editor, string key) =>
        editor.CreditRows.Concat(editor.PublicationRows).Concat(editor.ClassificationRows).Single(r => r.Key == key);

    [Fact]
    public void Load_FillsTheBuffer_FromWhatTheChaptersAgreeOn()
    {
        int id = SeedSeries(3);
        var editor = Editor();

        Assert.True(editor.Load(id));

        Assert.Equal("Test Manga", editor.Name);
        Assert.Equal("X", Row(editor, "Publisher").Text);
        Assert.Equal("A", Row(editor, "Writer").Text);
        Assert.Equal(7, editor.CreditRows.Count);
        Assert.False(editor.HasUnsavedChanges());
        Assert.False(editor.NeedsReview);
    }

    [Fact]
    public void Load_FlagsFieldsTheChaptersDisagreeOn()
    {
        int id = SeedSeries(10, (n, issue) => issue.Writer = n <= 7 ? "A" : "B");
        var editor = Editor();

        editor.Load(id);

        Assert.True(editor.NeedsReview);
        Assert.Contains("Writer", editor.ReviewSummary);
        Assert.True(Row(editor, "Writer").HasReviewNote);
        Assert.Contains("A", Row(editor, "Writer").ReviewNote);
        Assert.Contains("B", Row(editor, "Writer").ReviewNote);
        Assert.False(Row(editor, "Publisher").HasReviewNote);
    }

    [Fact]
    public void Load_ReturnsFalse_ForAMissingSeries_AndForAnotherLibrarysSeries()
    {
        Assert.False(Editor().Load(9999));

        int id = SeedSeries(2);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var source = new RemoteSource { InstanceId = "x", DisplayName = "Elsewhere", Host = "h" };
            context.RemoteSources.Add(source);
            context.SaveChanges();
            context.Series.Single(s => s.Id == id).RemoteSourceId = source.Id;
            context.SaveChanges();
        }

        // Not visible to the editor at all (remote books are read-only by construction), so it simply does not open.
        Assert.False(Editor().Load(id));
    }

    [Fact]
    public async Task Save_ChangesEveryChapter_QueuesTheFiles_AndClosesWithoutAskingAboutChapterNumbers()
    {
        int id = SeedSeries(5);
        var queued = new List<int>();
        bool closed = false;
        var editor = Editor(() => closed = true, queued.Add);
        editor.Load(id);

        Row(editor, "Publisher").Text = "Y";
        Row(editor, "Writer").Text = "B, C";
        Assert.True(editor.HasUnsavedChanges());
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.Equal(5, queued.Count);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i =>
        {
            Assert.Equal("Y", i.Publisher);
            Assert.Equal("B, C", i.Writer);
        });
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, context.Issues.OrderBy(i => i.Id).Select(i => i.Number).ToArray());
    }

    [Fact]
    public async Task Save_WhenSomeChaptersAlreadyDiffer_AsksFirst_AndWritesNothingUntilAnswered()
    {
        int id = SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        bool closed = false;
        var editor = Editor(() => closed = true);
        editor.Load(id);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(editor.ShowExceptionPrompt);
        Assert.False(closed);
        Assert.Contains("Publisher on 3 chapters", editor.ExceptionPromptText);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(3, context.Issues.Count(i => i.Publisher == "Other"));
            Assert.Equal(0, context.Series.Single().CanonicalReviewVersion);
        }

        await editor.KeepAsExceptionsCommand.ExecuteAsync(null);

        Assert.True(closed);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(3, verify.Issues.Count(i => i.Publisher == "Other"));
        Assert.Equal(3, verify.IssueFieldOverrides.Count(o => o.FieldKey == "Publisher"));
    }

    [Fact]
    public async Task ReplaceThem_OverwritesTheDifferingChapters()
    {
        int id = SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        var editor = Editor();
        editor.Load(id);
        await editor.SaveCommand.ExecuteAsync(null);

        await editor.ReplaceAllCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("X", i.Publisher));
    }

    [Fact]
    public async Task Back_FromTheQuestion_LeavesEverythingAsItWas()
    {
        int id = SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        var editor = Editor();
        editor.Load(id);
        await editor.SaveCommand.ExecuteAsync(null);

        editor.DismissExceptionPromptCommand.Execute(null);

        Assert.False(editor.ShowExceptionPrompt);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(3, context.Issues.Count(i => i.Publisher == "Other"));
    }

    [Fact]
    public async Task Save_WithoutAName_IsRefused()
    {
        int id = SeedSeries(2);
        string? toast = null;
        var editor = new MangaSeriesEditorViewModel(() => { }, new MangaSeriesMetadataService(PaperbunkrDb.CreateContext), (title, _) => toast = title);
        editor.Load(id);
        editor.Name = "  ";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Can't save", toast);
    }

    [Fact]
    public async Task Cancel_ClosesWithoutSaving_AndTheQuestionGoesBackFirst()
    {
        int id = SeedSeries(10, (n, issue) => issue.Publisher = n <= 3 ? "Other" : "X");
        int closes = 0;
        var editor = Editor(() => closes++);
        editor.Load(id);
        Row(editor, "Writer").Text = "Changed";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.ShowExceptionPrompt);

        editor.CancelCommand.Execute(null);
        Assert.False(editor.ShowExceptionPrompt);
        Assert.Equal(0, closes);

        editor.CancelCommand.Execute(null);
        Assert.Equal(1, closes);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("A", i.Writer));
    }

    [Fact]
    public async Task AnEditedField_BecomesLocked_AndUnlockingItIsSaved()
    {
        int id = SeedSeries(3);
        var editor = Editor();
        editor.Load(id);
        Row(editor, "Publisher").Text = "Y";
        await editor.SaveCommand.ExecuteAsync(null);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Contains("Publisher", context.Series.Single().LockedFields!);
        }

        var again = Editor();
        again.Load(id);
        var row = Row(again, "Publisher");
        Assert.True(row.IsLocked);
        row.UnlockCommand.Execute(null);
        Assert.False(row.IsLocked);
        Assert.True(again.HasUnsavedChanges());
        await again.SaveCommand.ExecuteAsync(null);

        using var verify = PaperbunkrDb.CreateContext();
        Assert.Null(verify.Series.Single().LockedFields);
    }

    [Fact]
    public void Tabs_SwitchWhichSectionIsShown()
    {
        int id = SeedSeries(2);
        var editor = Editor();
        editor.Load(id);
        Assert.True(editor.IsIdentityTab);

        editor.GoCreatorsCommand.Execute(null);
        Assert.True(editor.IsCreatorsTab);
        Assert.False(editor.IsIdentityTab);

        editor.GoAdvancedCommand.Execute(null);
        Assert.True(editor.IsAdvancedTab);
    }

    // ---------- the real flow ----------

    [Fact]
    public async Task EditSeries_FromTheMangaScreen_OpensTheOverlay_SavesAndReloadsTheScreen()
    {
        int id = SeedSeries(4);
        var vm = new MainViewModel();
        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
        var action = vm.MangaDetail.Actions.Single(a => a.Label == "Edit Series");
        Assert.True(action.IsEnabled);

        vm.MangaDetail.EditSeriesCommand.Execute(null);

        Assert.True(vm.IsMangaSeriesEditorOverlayOpen);
        Row(vm.MangaSeriesEditor, "Publisher").Text = "Y";
        await vm.MangaSeriesEditor.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsMangaSeriesEditorOverlayOpen);
        Assert.True(vm.IsMangaDetail);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("Y", i.Publisher));
        Assert.Equal("Y", vm.MangaDetail.Band.PublisherText);
    }

    [Fact]
    public void EditSeries_NeedsNoChapter_AndIsDisabledForAnotherLibrarysSeries()
    {
        int empty = SeedSeries(0);
        var vm = new MainViewModel();
        vm.MangaDetail.LoadSeries(empty);
        Assert.True(vm.MangaDetail.EditSeriesCommand.CanExecute(null));

        int remote = SeedSeries(2);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var source = new RemoteSource { InstanceId = "y", DisplayName = "Elsewhere", Host = "h" };
            context.RemoteSources.Add(source);
            context.SaveChanges();
            context.Series.Single(s => s.Id == remote).RemoteSourceId = source.Id;
            context.SaveChanges();
        }

        vm.MangaDetail.LoadSeries(remote);
        Assert.False(vm.MangaDetail.EditSeriesCommand.CanExecute(null));
    }

    [Fact]
    public void Escape_ClosesTheOverlay_WithoutSaving()
    {
        int id = SeedSeries(3);
        var vm = new MainViewModel();
        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
        vm.MangaDetail.EditSeriesCommand.Execute(null);
        Row(vm.MangaSeriesEditor, "Publisher").Text = "Y";

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsMangaSeriesEditorOverlayOpen);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("X", i.Publisher));
    }

    [Fact]
    public void AComicSeries_StillEditsThroughTheIssueEditor_Unchanged()
    {
        int id = SeedSeries(2, type: ContentType.Comic);
        var vm = new MainViewModel();
        vm.Detail.LoadSeries(id);
        Assert.Equal("Edit", vm.Detail.EditButtonLabel);
        Assert.DoesNotContain(vm.Detail.Actions, a => a.Label == "Edit Series");
    }

    // ---------- Sources ----------

    private sealed class FakeProvider : Paperbunkr.Data.Metadata.IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaBaka;

        public Paperbunkr.Data.Metadata.ExternalMediaMetadata? Result { get; set; }

        public Task<IReadOnlyList<Paperbunkr.Data.Metadata.MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Paperbunkr.Data.Metadata.MetadataSearchResult>>(Array.Empty<Paperbunkr.Data.Metadata.MetadataSearchResult>());

        public Task<Paperbunkr.Data.Metadata.ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private (MangaSeriesEditorViewModel Editor, int SeriesId, FakeProvider Provider) SourcesEditor(bool summaryLocked = true)
    {
        int id = SeedSeries(4);
        var service = new MangaSeriesMetadataService(PaperbunkrDb.CreateContext);
        var edit = service.Load(id)!;
        service.Save(edit);
        if (summaryLocked)
        {
            edit = service.Load(id)!;
            edit.Summary = "My own summary";
            service.Save(edit);
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = id, Provider = ExternalMetadataProvider.MangaBaka, ExternalId = "42", Url = "https://example.test/42" });
            context.SaveChanges();
        }

        var provider = new FakeProvider
        {
            Result = new Paperbunkr.Data.Metadata.ExternalMediaMetadata("42", "Test Manga", "https://example.test/42", "Provider summary", "Completed", 10, 2, Creator: "Pat Author, Sam Artist"),
        };
        var editor = new MangaSeriesEditorViewModel(() => { }, service, providers: _ => provider);
        Assert.True(editor.Load(id));
        return (editor, id, provider);
    }

    /// <summary>Runs the dispatcher's queued work on this (UI) thread until <paramref name="done"/>: an async test would resume off it, which is why these tests start the command and pump instead of awaiting.</summary>
    private static void Pump(Func<bool> done, int timeoutMs = 8000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!done())
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < end, "Timed out waiting for the background work.");
            Thread.Sleep(10);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Refresh(MangaSeriesEditorViewModel editor)
    {
        editor.Sources.Links.Single().RefreshCommand.Execute(null);
        Pump(() => !editor.Sources.IsBusy && editor.Sources.StatusText.StartsWith("Refreshed", StringComparison.Ordinal));
    }

    [Fact]
    public void Sources_ListsTheLinkedProviders()
    {
        var (editor, _, _) = SourcesEditor();

        var link = Assert.Single(editor.Sources.Links);
        Assert.Equal("MangaBaka", link.ProviderLabel);
        Assert.Equal("42", link.ExternalId);
        Assert.True(editor.Sources.HasLinks);
        Assert.False(editor.Sources.HasCandidates);
    }

    [Fact]
    public void Refresh_KeepsAHandWrittenSummary_AndOffersTheProvidersAsACandidate()
    {
        var (editor, id, _) = SourcesEditor();

        Refresh(editor);

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("My own summary", context.Series.Single().Summary);
        }

        var summary = editor.Sources.Candidates.Single(c => c.Candidate.Field == MetadataProposalField.Summary);
        Assert.Equal("My own summary", summary.CurrentText);
        Assert.Equal("Provider summary", summary.ProposedText);
        Assert.True(summary.IsLocked);
        var credit = editor.Sources.Candidates.Single(c => c.Candidate.Field == MetadataProposalField.Creator);
        Assert.True(credit.NeedsRole);
        Assert.Contains("Refreshed", editor.Sources.StatusText);
    }

    [Fact]
    public void Accepting_ACandidate_AppliesIt_AndTheEditorShowsTheNewValue()
    {
        var (editor, id, _) = SourcesEditor();
        Refresh(editor);
        var summary = editor.Sources.Candidates.Single(c => c.Candidate.Field == MetadataProposalField.Summary);

        summary.AcceptCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Provider summary", editor.Summary);
        Assert.DoesNotContain(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.Summary);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Provider summary", context.Series.Single().Summary);
    }

    [Fact]
    public void AcceptingAJoinedCreditList_UsesTheRoleThatWasChosen()
    {
        var (editor, id, _) = SourcesEditor();
        Refresh(editor);
        var credit = editor.Sources.Candidates.Single(c => c.Candidate.Field == MetadataProposalField.Creator);
        credit.Role = "Penciller";

        credit.AcceptCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Pat Author, Sam Artist", Row(editor, "Penciller").Text);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("Pat Author, Sam Artist", i.Penciller));
    }

    [Fact]
    public void Dismissing_ACandidate_ChangesNothing()
    {
        var (editor, id, _) = SourcesEditor();
        Refresh(editor);

        editor.Sources.Candidates.Single(c => c.Candidate.Field == MetadataProposalField.Summary).RejectCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.Summary);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("My own summary", context.Series.Single().Summary);
    }

    [Fact]
    public void WithUnsavedEdits_RefreshAndAcceptWaitUntilTheyAreSaved()
    {
        string? toast = null;
        var (_, id, provider) = SourcesEditor();
        var editor = new MangaSeriesEditorViewModel(() => { }, new MangaSeriesMetadataService(PaperbunkrDb.CreateContext), (title, _) => toast = title, providers: _ => provider);
        editor.Load(id);
        Row(editor, "Writer").Text = "Someone else";

        editor.Sources.Links.Single().RefreshCommand.Execute(null);
        Pump(() => !editor.Sources.IsBusy);

        Assert.Equal("Save your edits first", toast);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.MetadataProposals.Where(p => p.Status == MetadataProposalStatus.Pending));
    }

    [Fact]
    public void AProviderWithNoFetcher_SaysSo()
    {
        var (_, id, _) = SourcesEditor();
        var editor = new MangaSeriesEditorViewModel(() => { }, new MangaSeriesMetadataService(PaperbunkrDb.CreateContext), providers: _ => null);
        editor.Load(id);

        editor.Sources.Links.Single().RefreshCommand.Execute(null);
        Pump(() => !editor.Sources.IsBusy);

        Assert.Contains("can't be refreshed", editor.Sources.StatusText);
    }

    // ---------- weighted tags and reading mode in the editor ----------

    [Fact]
    public void GenresAndTags_AreWeightedRows_NotTextBoxes()
    {
        int id = SeedSeries(3);
        var editor = Editor();

        editor.Load(id);

        var action = Assert.Single(editor.Genres.Rows);
        Assert.Equal("Action", action.Value);
        Assert.Equal("Genre", action.Category);
        Assert.Equal(IssueTagWeight.Unset, action.Weight);
        Assert.Empty(editor.Tags.Rows);
        Assert.DoesNotContain(editor.ClassificationRows, r => r.Field.Kind == MangaFieldKind.Tags);
    }

    [Fact]
    public void AddingValues_AcceptsACommaList_SkipsDuplicates_AndNeedsSaving()
    {
        int id = SeedSeries(3);
        var editor = Editor();
        editor.Load(id);
        Assert.False(editor.HasUnsavedChanges());

        editor.Genres.AddText = "Fantasy, drama, action";   // "action" is already there
        editor.Genres.AddCommand.Execute(null);

        Assert.Equal(new[] { "Action", "Fantasy", "drama" }, editor.Genres.Rows.Select(r => r.Value).ToArray());
        Assert.Equal(string.Empty, editor.Genres.AddText);
        Assert.True(editor.HasUnsavedChanges());
    }

    [Fact]
    public async Task SettingWeights_SavesThemOnTheSeries_AndOnEveryChapter()
    {
        int id = SeedSeries(4);
        var editor = Editor();
        editor.Load(id);
        editor.Genres.Rows.Single().SetWeightCommand.Execute(IssueTagWeight.Core);
        editor.Tags.AddText = "Gangs";
        editor.Tags.AddCommand.Execute(null);
        editor.Tags.Rows.Single().Category = "Activities";
        editor.Tags.Rows.Single().SetWeightCommand.Execute(IssueTagWeight.Defining);
        Assert.True(editor.HasUnsavedChanges());

        await editor.SaveCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.SeriesTags).Single();
        Assert.Equal(IssueTagWeight.Core, series.SeriesTags.Single(t => t.Field == IssueTagField.Genre).Weight);
        var gangs = series.SeriesTags.Single(t => t.Field == IssueTagField.Tags);
        Assert.Equal(("Gangs", "Activities", IssueTagWeight.Defining), (gangs.Value, gangs.Category, gangs.Weight));
        foreach (var chapter in context.Issues.Include(i => i.Tags).ToList())
        {
            Assert.Equal(IssueTagWeight.Core, chapter.Tags.Single(t => t.Field == IssueTagField.Genre).Weight);
            var tag = chapter.Tags.Single(t => t.Field == IssueTagField.Tags);
            Assert.Equal(("Gangs", "Activities", IssueTagWeight.Defining), (tag.Value, tag.Category, tag.Weight));
        }
    }

    [Fact]
    public async Task RemovingARow_TakesItOffTheSeriesAndTheChapters_AfterTheClickHasFinished()
    {
        int id = SeedSeries(3);
        var editor = Editor();
        editor.Load(id);
        editor.Genres.AddText = "Drama";
        editor.Genres.AddCommand.Execute(null);

        editor.Genres.RemoveCommand.Execute(editor.Genres.Rows.Single(r => r.Value == "Action"));
        Assert.Equal(2, editor.Genres.Rows.Count);   // not removed inline (the row's own button is still routing its click)
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Drama" }, editor.Genres.Rows.Select(r => r.Value).ToArray());

        await editor.SaveCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.IssueTags.Where(t => t.Field == IssueTagField.Genre), t => Assert.Equal("Drama", t.Value));
    }

    [Fact]
    public async Task ATagListYouChanged_IsLocked_AndUnlockingItIsSaved()
    {
        int id = SeedSeries(3);
        var editor = Editor();
        editor.Load(id);
        editor.Genres.Rows.Single().SetWeightCommand.Execute(IssueTagWeight.Defining);
        await editor.SaveCommand.ExecuteAsync(null);

        var again = Editor();
        again.Load(id);
        Assert.True(again.Genres.IsLocked);
        Assert.False(again.Tags.IsLocked);
        again.Genres.UnlockCommand.Execute(null);
        Assert.False(again.Genres.IsLocked);
        Assert.True(again.HasUnsavedChanges());
        await again.SaveCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.DoesNotContain("Genre", MangaFieldLocks.Parse(context.Series.Single().LockedFields));
    }

    [Fact]
    public async Task TheReadingMode_IsAFieldOfTheEditor_AndSavesOnTheSeries()
    {
        int id = SeedSeries(3);
        var editor = Editor();
        editor.Load(id);
        Assert.Equal("Left to Right", editor.ReadingModeText);
        Assert.Equal(7, editor.ReadingModeOptions.Length);

        editor.ReadingModeText = "Long Strip";
        Assert.True(editor.HasUnsavedChanges());
        Assert.Contains("vertical scroll", editor.ReadingModeDescription);
        await editor.SaveCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingMode.Webtoon, context.Series.Single().ReadingMode);
        var reloaded = Editor();
        reloaded.Load(id);
        Assert.Equal("Long Strip", reloaded.ReadingModeText);
    }

    [Fact]
    public async Task FlippingRightToLeft_MarksTheChaptersForTheirFiles_ButALongStripDoesNot()
    {
        int id = SeedSeries(3);
        var queued = new List<int>();
        var editor = Editor(enqueue: queued.Add);
        editor.Load(id);
        editor.ReadingModeText = "Long Strip";
        await editor.SaveCommand.ExecuteAsync(null);
        queued.Clear();
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.ExecuteUpdate(s => s.SetProperty(i => i.EmbeddedDirty, false));
        }

        var second = Editor(enqueue: queued.Add);
        second.Load(id);
        second.ReadingModeText = "Right to Left";
        await second.SaveCommand.ExecuteAsync(null);

        Assert.Equal(3, queued.Distinct().Count());
        using var verify = PaperbunkrDb.CreateContext();
        Assert.All(verify.Issues, i => Assert.True(i.EmbeddedDirty));
    }

    // ---------- Sources: spoilers and Accept all ----------

    [Fact]
    public void SpoilerTags_AreOnlyImportedWhenTheSwitchIsOn()
    {
        var (editor, id, provider) = SourcesEditor(summaryLocked: false);
        provider.Result = new Paperbunkr.Data.Metadata.ExternalMediaMetadata("42", "Test Manga", null, "Provider summary", "Completed", 10, 2,
            WeightedTags: new[]
            {
                new Paperbunkr.Data.Metadata.ProviderTag("Gangs", "Activities", IssueTagWeight.Defining, false, false),
                new Paperbunkr.Data.Metadata.ProviderTag("Yandere", "Character Archetype", IssueTagWeight.Recurrent, false, true),
            });

        Refresh(editor);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Contains(context.SeriesTagRows, t => t.Value == "Gangs");
            Assert.DoesNotContain(context.SeriesTagRows, t => t.Value == "Yandere");
        }

        editor.Sources.IncludeSpoilerTags = true;
        editor.Sources.StatusText = string.Empty;
        Refresh(editor);

        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(IssueTagWeight.Recurrent, verify.SeriesTagRows.Single(t => t.Value == "Yandere").Weight);
    }

    [Fact]
    public void AcceptAll_TakesTheOpenOffers_ButLeavesAHandSetFieldAndTheReadingModeAlone()
    {
        var (editor, id, provider) = SourcesEditor(summaryLocked: true);
        provider.Result = new Paperbunkr.Data.Metadata.ExternalMediaMetadata("42", "Test Manga", null, "Provider summary", "Completed", 10, 2,
            Authors: new[] { "Pat Author" }, PublicationFormat: "manhwa", OriginalLanguage: "ko");
        Refresh(editor);
        Assert.Contains(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.Summary && c.IsLocked);
        Assert.Contains(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.Writer);
        Assert.Contains(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.ReadingMode);

        editor.Sources.AcceptAllCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Single();
        Assert.Equal("My own summary", series.Summary);                  // hand-set: left alone
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);        // a suggestion: left for you
        Assert.Equal(new[] { "Pat Author" }, context.SeriesCredits.Where(c => c.Role == "Writer").Select(c => c.Name).ToArray());
        Assert.Contains(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.Summary);
        Assert.Contains(editor.Sources.Candidates, c => c.Candidate.Field == MetadataProposalField.ReadingMode);
    }

    // ---------- what the Manga screen shows ----------

    [Fact]
    public async Task TheNeedsReviewChip_ShowsUntilTheSeriesIsConfirmed()
    {
        int id = SeedSeries(10, (n, issue) => issue.Writer = n <= 7 ? "A" : "B");
        var vm = new MainViewModel();
        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
        Assert.True(vm.MangaDetail.NeedsReview);

        vm.MangaDetail.EditSeriesCommand.Execute(null);
        await vm.MangaSeriesEditor.SaveCommand.ExecuteAsync(null);
        await vm.MangaSeriesEditor.KeepAsExceptionsCommand.ExecuteAsync(null);

        Assert.False(vm.MangaDetail.NeedsReview);
    }

    [Fact]
    public void ASeriesWhoseChaptersAgree_NeverShowsTheChip()
    {
        int id = SeedSeries(5);
        var vm = new MainViewModel();
        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));

        Assert.False(vm.MangaDetail.NeedsReview);
    }

    [Fact]
    public async Task TheDetailsTab_ListsTheSeriesCredits_NotAChapterOwnGuestWriter()
    {
        int id = SeedSeries(4, (n, issue) => issue.Writer = n == 2 ? "Guest" : "A");
        var vm = new MainViewModel();
        vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
        // Before confirmation the aggregate over every chapter is shown, as it always was.
        Assert.Contains("Guest", vm.MangaDetail.Tabs.CreditRoles.Single(g => g.Label == "Writer").Chips.Select(c => c.Value));

        vm.MangaDetail.EditSeriesCommand.Execute(null);
        await vm.MangaSeriesEditor.SaveCommand.ExecuteAsync(null);
        await vm.MangaSeriesEditor.KeepAsExceptionsCommand.ExecuteAsync(null);

        var writers = vm.MangaDetail.Tabs.CreditRoles.Single(g => g.Label == "Writer").Chips.Select(c => c.Value).ToList();
        Assert.Equal(new[] { "A" }, writers);
    }

    // ---------- the real window ----------

    [Fact]
    public void TheRealWindow_ShowsAWeightSelectorForEachTagRow_AndTheReadingModePicker()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int id = SeedSeries(3);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
            vm.MangaDetail.EditSeriesCommand.Execute(null);
            vm.MangaSeriesEditor.Tags.AddText = "Gangs, Smoking";
            vm.MangaSeriesEditor.Tags.AddCommand.Execute(null);
            vm.MangaSeriesEditor.GoClassificationCommand.Execute(null);
            RunLayout(window);

            var overlay = window.GetVisualDescendants().OfType<MangaSeriesEditorOverlay>().Single();
            var weightButtons = overlay.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("segItem") && b.IsEffectivelyVisible).ToList();
            // One genre row plus two tag rows, five weights each.
            Assert.Equal(15, weightButtons.Count);

            vm.MangaSeriesEditor.GoPublicationCommand.Execute(null);
            RunLayout(window);
            Assert.Contains(overlay.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "READING MODE");

            window.Close();
        });
    }


    [Fact]
    public void TheRealWindow_ShowsTheOverlay_AndItsSectionsSwitch()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int id = SeedSeries(3);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.OpenDeepLink(new Paperbunkr.App.Services.NavigationCliTarget("series", id));
            RunLayout(window);

            vm.MangaDetail.EditSeriesCommand.Execute(null);
            RunLayout(window);

            var overlay = window.GetVisualDescendants().OfType<MangaSeriesEditorOverlay>().Single();
            Assert.True(overlay.IsEffectivelyVisible);
            Assert.Contains(overlay.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "Test Manga");

            vm.MangaSeriesEditor.GoCreatorsCommand.Execute(null);
            RunLayout(window);
            var creditBoxes = overlay.GetVisualDescendants().OfType<Paperbunkr.App.Controls.SuggestBox>().Where(t => t.IsEffectivelyVisible && t.DataContext is MangaFieldRowViewModel).ToList();
            Assert.Equal(7, creditBoxes.Count);

            window.Close();
        });
    }

}
