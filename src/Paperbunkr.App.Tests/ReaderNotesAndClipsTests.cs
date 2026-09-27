using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Page notes, region clips and the Markdown export (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderNotesAndClipsTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_notes_test_{Guid.NewGuid():N}.db");
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_notes_test_{Guid.NewGuid():N}.cbz");
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_notes_out_{Guid.NewGuid():N}");
    private readonly List<string> _clipFiles = new();
    private readonly int _issueId;

    public ReaderNotesAndClipsTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        CbzFixture.Create(_cbzPath, pageCount: 5, pageSize: _ => new System.Drawing.Size(400, 600));
        var series = new Series { Name = "Notes" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "7", FilePath = _cbzPath };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
    }

    public void Dispose()
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            _clipFiles.AddRange(context.PageClips.Select(c => c.ImagePath));
        }
        catch (Exception)
        {
        }

        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in _clipFiles.Concat(new[] { _dbPath, _cbzPath }))
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        try
        {
            if (Directory.Exists(_outputDirectory)) Directory.Delete(_outputDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ReaderScreenViewModel Load()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);
        return vm;
    }

    // ===== Notes =====

    [Fact]
    public void SavingANote_StoresItOnThePage_MarksThePageDot_AndAReloadReadsItBack()
    {
        var vm = Load();
        vm.GoToPage(2);
        vm.NoteDraft = "  the map is on this page  ";

        vm.SaveNoteCommand.Execute(null);

        using (var context = PaperbunkrDb.CreateContext())
        {
            var row = context.PageNotes.Single();
            Assert.Equal(2, row.PageNumber);
            Assert.Equal("the map is on this page", row.Text);
        }

        Assert.True(vm.Thumbnails[2].HasNote);
        Assert.False(vm.Thumbnails[1].HasNote);
        Assert.True(vm.HasCurrentNote);

        var again = Load();
        Assert.True(again.Thumbnails[2].HasNote);
        again.GoToPage(2);
        Assert.Equal("the map is on this page", again.NoteDraft);          // the box follows the page
        again.GoToPage(3);
        Assert.Equal(string.Empty, again.NoteDraft);
        Assert.False(again.HasCurrentNote);
    }

    [Fact]
    public void SavingAgain_UpdatesTheSameRow_ANoteIsCappedAt2000_AndAnEmptyBoxDeletesIt()
    {
        var vm = Load();
        vm.NoteDraft = "first";
        vm.SaveNoteCommand.Execute(null);
        vm.NoteDraft = new string('x', 2600);
        vm.SaveNoteCommand.Execute(null);

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(PageNote.MaxTextLength, context.PageNotes.Single().Text.Length);
        }

        vm.NoteDraft = "   ";
        vm.SaveNoteCommand.Execute(null);

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Empty(context.PageNotes);
        }

        Assert.False(vm.Thumbnails[0].HasNote);
    }

    [Fact]
    public void TheList_HoldsNotesClipsAndBookmarkNotesByPage_AndClickingARowJumpsThere()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.IssueBookmarks.Add(new IssueBookmark { IssueId = _issueId, PageNumber = 1, Label = "Fight", Note = "the big fight", CreatedTime = DateTime.UtcNow });
            context.PageNotes.Add(new PageNote { IssueId = _issueId, PageNumber = 3, Text = "a note", CreatedTime = DateTime.UtcNow, ModifiedTime = DateTime.UtcNow });
            context.PageClips.Add(new PageClip { IssueId = _issueId, PageNumber = 3, RectWidth = 0.5, RectHeight = 0.5, ImagePath = "missing.png", Caption = "lineup", CreatedTime = DateTime.UtcNow });
            context.SaveChanges();
        }

        var vm = Load();

        Assert.Equal(new[] { NoteItemKind.BookmarkNote, NoteItemKind.Note, NoteItemKind.Clip }, vm.NoteItems.Select(i => i.Kind).ToArray());
        Assert.Equal(new[] { 1, 3, 3 }, vm.NoteItems.Select(i => i.PageIndex).ToArray());
        Assert.True(vm.HasNoteItems);

        vm.GoToNoteItemCommand.Execute(vm.NoteItems[1]);
        Assert.Equal("PAGE 4 / 5", vm.PageLabel);
    }

    [Fact]
    public void NoteOnThisPage_OpensTheDrawerAtNotes_AndAsksForFocus()
    {
        var vm = Load();
        int focus = 0;
        vm.NoteFocusRequested += () => focus++;

        vm.NoteOnThisPageCommand.Execute(null);

        Assert.True(vm.IsDrawerOpen);
        Assert.True(vm.IsNotesSectionExpanded);
        Assert.Equal(1, focus);
    }

    // ===== Clips =====

    [Fact]
    public void CapturingAClip_SavesAPng_AndARow_AndTheRowIsListed()
    {
        var vm = Load();
        vm.GoToPage(1);
        var toasts = new List<ToastRequest>();
        vm.ToastRequested += toasts.Add;
        vm.IsClipMode = true;

        vm.CaptureClip(new Rect(0.1, 0.2, 0.5, 0.4));

        Assert.False(vm.IsClipMode);                                         // one clip per activation
        using var context = PaperbunkrDb.CreateContext();
        var clip = context.PageClips.Single();
        Assert.Equal(1, clip.PageNumber);
        Assert.Equal(0.5, clip.RectWidth);
        Assert.True(File.Exists(clip.ImagePath));
        _clipFiles.Add(clip.ImagePath);
        Assert.Contains(vm.NoteItems, i => i.IsClip && i.ClipId == clip.Id && i.Thumbnail is not null);
        Assert.Contains(toasts, t => t.Title == "Clip saved");
    }

    [Fact]
    public async Task ACaptionIsStored_CopyAndSaveWork_AndDeleteRemovesRowAndFile()
    {
        var vm = Load();
        vm.CaptureClip(new Rect(0, 0, 0.5, 0.5));
        var item = vm.NoteItems.Single(i => i.IsClip);
        _clipFiles.Add(item.ImagePath!);

        vm.CommitClipCaption(item, "  the lineup ");
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal("the lineup", context.PageClips.Single().Caption);
        }

        int copied = 0;
        vm.BitmapClipboardWriter = _ => { copied++; return Task.FromResult(true); };
        await vm.CopyClipCommand.ExecuteAsync(item);
        Assert.Equal(1, copied);

        string target = Path.Combine(_outputDirectory, "clip.png");
        Directory.CreateDirectory(_outputDirectory);
        vm.SaveFilePicker = (_, _, _, _) => Task.FromResult<string?>(target);
        await vm.SaveClipCommand.ExecuteAsync(item);
        Assert.True(File.Exists(target));

        vm.DeleteClipCommand.Execute(item);
        TestDispatcher.Drain();
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Empty(context.PageClips);
        }

        Assert.False(File.Exists(item.ImagePath));
        Assert.DoesNotContain(vm.NoteItems, i => i.IsClip);
    }

    [Fact]
    public void ClipMode_IsRefusedInContinuousReading()
    {
        var vm = Load();
        var toasts = new List<ToastRequest>();
        vm.ToastRequested += toasts.Add;
        vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

        vm.ToggleClipModeCommand.Execute(null);

        Assert.False(vm.IsClipMode);
        Assert.Contains(toasts, t => t.Title.StartsWith("Clips are cut from a single page"));
    }

    [Fact]
    public void DeletingTheIssue_RemovesItsClipFilesToo()
    {
        var vm = Load();
        vm.CaptureClip(new Rect(0, 0, 0.5, 0.5));
        string path = vm.NoteItems.Single(i => i.IsClip).ImagePath!;
        _clipFiles.Add(path);
        Assert.True(File.Exists(path));

        using (var context = PaperbunkrDb.CreateContext())
        {
            LibraryDeletionHelper.RemoveIssue(context, context.Issues.Single(i => i.Id == _issueId), deleteFile: false);
            context.SaveChanges();
            Assert.Empty(context.PageClips);
            Assert.Empty(context.PageNotes);
        }

        Assert.False(File.Exists(path));
    }

    // ===== Export =====

    [Fact]
    public void TheMarkdownExporter_WritesAHeadingPerPage_AndCopiesClipImagesBesideIt()
    {
        Directory.CreateDirectory(_outputDirectory);
        string image = Path.Combine(_outputDirectory, "src-clip.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        string markdown = Path.Combine(_outputDirectory, "out", "notes.md");

        int copied = NotesMarkdownExporter.Write(
            markdown,
            "Series #7 - notes",
            [new ExportNote(3, "second note"), new ExportNote(0, "first note")],
            [new ExportClip(3, image, "the [lineup]"), new ExportClip(5, Path.Combine(_outputDirectory, "gone.png"), null)]);

        Assert.Equal(1, copied);
        string text = File.ReadAllText(markdown);
        Assert.StartsWith("# Series #7 - notes", text);
        Assert.True(text.IndexOf("## Page 1", StringComparison.Ordinal) < text.IndexOf("## Page 4", StringComparison.Ordinal));   // by page
        Assert.Contains("first note", text);
        Assert.Contains("![the \\[lineup\\]](notes-images/page004-1-src-clip.png)", text);
        Assert.True(File.Exists(Path.Combine(_outputDirectory, "out", NotesMarkdownExporter.ImagesFolderName, "page004-1-src-clip.png")));
        Assert.Contains("image file is missing", text);                      // a clip whose file is gone is still listed
    }

    [Fact]
    public async Task ExportNotes_AsksForAFile_AndWritesIt()
    {
        var vm = Load();
        vm.NoteDraft = "hello";
        vm.SaveNoteCommand.Execute(null);
        Directory.CreateDirectory(_outputDirectory);
        string target = Path.Combine(_outputDirectory, "notes.md");
        vm.SaveFilePicker = (_, _, extension, _) => { Assert.Equal("md", extension); return Task.FromResult<string?>(target); };

        await vm.ExportNotesCommand.ExecuteAsync(null);

        Assert.Contains("hello", File.ReadAllText(target));

        var empty = new ReaderScreenViewModel(goBack: () => { });
        var toasts = new List<ToastRequest>();
        empty.ToastRequested += toasts.Add;
        // (a view model with nothing loaded has nothing to export)
        await empty.ExportNotesCommand.ExecuteAsync(null);
        Assert.Contains(toasts, t => t.Title == "Nothing to export");
    }

    // ===== On screen =====

    [Fact]
    public void DraggingARectangleWithClipModeOn_SavesAClipOfThePage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var (window, screen, canvas) = ReaderScreenTestHost.Open(vm, _issueId);
        try
        {
            vm.ToggleClipModeCommand.Execute(null);
            TestDispatcher.Drain();
            Assert.True(vm.IsClipMode);

            var image = canvas.GetCurrentImageBounds();
            var start = canvas.TranslatePoint(new Point(image.X + (image.Width * 0.2), image.Y + (image.Height * 0.2)), window)!.Value;
            var end = canvas.TranslatePoint(new Point(image.X + (image.Width * 0.6), image.Y + (image.Height * 0.5)), window)!.Value;
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2));
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            TestDispatcher.Drain();

            using var context = PaperbunkrDb.CreateContext();
            Assert.True(context.PageClips.Any(), $"no clip: clipMode {vm.IsClipMode}, overlay {screen.ClipOverlay.Bounds} hit {screen.ClipOverlay.IsHitTestVisible} capture {screen.ClipOverlay.IsCaptureMode}, image {image}, start {start}, end {end}, page {canvas.Page?.PixelSize}, hit {window.InputHitTest(start)?.GetType().Name}/{(window.InputHitTest(start) as Avalonia.Visual)?.Bounds}");
            var clip = Assert.Single(context.PageClips);
            _clipFiles.Add(clip.ImagePath);
            Assert.InRange(clip.RectX, 0.15, 0.25);
            Assert.InRange(clip.RectWidth, 0.35, 0.45);
            Assert.False(vm.IsClipMode);
        }
        finally
        {
            window.Close();
        }
    }
}
