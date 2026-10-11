using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The "New Smart List" gallery (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.2): kind tabs, "Blank list" first, a
/// template makes an ordinary editable list with no link back, and the dialog behaves as a modal on the real Smart screen.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SmartTemplateGalleryTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SmartTemplateGalleryTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_gallery_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static SmartScreenViewModel NewScreenViewModel() => new(goToSeries: _ => { }, goToBook: _ => { });

    private static List<SmartList> UserLists()
    {
        using var context = PaperbunkrDb.CreateContext();
        var ids = context.SmartLists.Where(l => !l.IsSystem).Select(l => l.Id).ToList();
        return ids.Select(id => SmartListTreeLoader.LoadWithTree(context, id)!).ToList();
    }

    [Fact]
    public void Open_ShowsTheKindsTemplates_WithBlankListFirstAndSelected()
    {
        var gallery = new SmartTemplateGalleryViewModel((_, _) => { });

        gallery.Open(SmartListTargetKind.Series);

        Assert.True(gallery.IsOpen);
        Assert.Equal("Series", gallery.Kinds.Single(k => k.IsActive).Label);
        Assert.True(gallery.Cards[0].IsBlank);
        Assert.True(gallery.Cards[0].IsSelected);
        Assert.Equal(SmartListTemplateCatalog.For(SmartListTargetKind.Series).Select(t => t.Name), gallery.Cards.Skip(1).Select(c => c.Name));
        Assert.Equal("Create blank list", gallery.CreateLabel);
    }

    [Fact]
    public void SelectKind_SwapsTheCards_AndResetsTheSelectionToBlank()
    {
        var gallery = new SmartTemplateGalleryViewModel((_, _) => { });
        gallery.Open(SmartListTargetKind.Issue);
        gallery.SelectCardCommand.Execute(gallery.Cards[1]);

        gallery.SelectKindCommand.Execute(gallery.Kinds.Single(k => k.Kind == SmartListTargetKind.Novel));

        Assert.Equal(SmartListTargetKind.Novel, gallery.Kind);
        Assert.True(gallery.Cards[0].IsSelected);
        Assert.Single(gallery.Cards, c => c.IsSelected);
        Assert.Contains(gallery.Cards, c => c.Name == "In progress");
    }

    [Fact]
    public void SelectingATemplate_NamesItOnTheCreateButton_AndShowsItsRulesAsChips()
    {
        var gallery = new SmartTemplateGalleryViewModel((_, _) => { });
        gallery.Open(SmartListTargetKind.Series);
        var behind = gallery.Cards.Single(c => c.Name == "Behind on ongoing series");

        gallery.SelectCardCommand.Execute(behind);

        Assert.Equal("Create from \"Behind on ongoing series\"", gallery.CreateLabel);
        Assert.Equal(["Status is Ongoing", "Unread Issues is greater than 2", "Days Since Last Read is greater than 30"], behind.Chips);
    }

    [Fact]
    public void Chips_ReadToggleAndNegatedRules_InWords()
    {
        var gallery = new SmartTemplateGalleryViewModel((_, _) => { });
        gallery.Open(SmartListTargetKind.Issue);

        Assert.Equal(["Has Pending Proposal"], gallery.Cards.Single(c => c.Name == "Needs-review flagged").Chips);
        Assert.Contains("Date Added not within last (days) 90", gallery.Cards.Single(c => c.Name == "Never opened (90+ days)").Chips);
        Assert.Contains("Summary is empty", gallery.Cards.Single(c => c.Name == "Missing metadata").Chips);
    }

    [Fact]
    public void CreateFromATemplate_MakesAnOrdinaryEditableList_AndOpensItInTheEditor()
    {
        var vm = NewScreenViewModel();
        vm.OpenSeriesGalleryCommand.Execute(null);
        vm.Gallery.SelectCardCommand.Execute(vm.Gallery.Cards.Single(c => c.Name == "Almost finished"));

        vm.Gallery.CreateCommand.Execute(null);

        var list = Assert.Single(UserLists());
        Assert.Equal("Almost finished", list.Name);
        Assert.Equal(SmartListTargetKind.Series, list.TargetKind);
        Assert.False(list.IsSystem);
        Assert.Equal(2, list.RootGroup.Conditions.Count);
        Assert.False(vm.Gallery.IsOpen);
        Assert.Equal("Almost finished", vm.ListName);
        Assert.True(vm.CanSaveList);     // an ordinary user list: editable, not read-only
    }

    [Fact]
    public void CreateBlank_MakesAnEmptyListOfTheGallerysKind()
    {
        var vm = NewScreenViewModel();
        vm.OpenNovelGalleryCommand.Execute(null);

        vm.Gallery.CreateCommand.Execute(null);

        var list = Assert.Single(UserLists());
        Assert.Equal(SmartListTargetKind.Novel, list.TargetKind);
        Assert.Empty(list.RootGroup.Conditions);
    }

    [Fact]
    public void Close_CreatesNothing()
    {
        var vm = NewScreenViewModel();
        vm.OpenGalleryCommand.Execute(null);
        vm.Gallery.SelectCardCommand.Execute(vm.Gallery.Cards[1]);

        vm.Gallery.CloseCommand.Execute(null);

        Assert.False(vm.Gallery.IsOpen);
        Assert.Empty(UserLists());
    }

    [Fact]
    public void OnTheSmartScreen_TheGalleryTakesFocus_EscapeClosesIt_AndFocusReturnsToTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var vm = NewScreenViewModel();
            var screen = new SmartScreen { DataContext = vm };
            var window = new Window { Content = screen, Width = 1300, Height = 900 };
            window.Show();
            RunLayout(window);

            vm.OpenGalleryCommand.Execute(null);
            RunLayout(window);
            TestDispatcher.Drain();
            RunLayout(window);

            var gallery = screen.GetVisualDescendants().OfType<SmartTemplateGallery>().Single();
            Assert.True(gallery.IsEffectivelyVisible);
            Assert.True(FocusIsInside(window, gallery), $"focus on {Focused(window)} while the gallery is open");
            int cards = gallery.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("tplCard"));
            Assert.Equal(vm.Gallery.Cards.Count, cards);

            Press(window, Key.Escape);

            Assert.False(vm.Gallery.IsOpen);
            Assert.Empty(UserLists());
            window.Close();
        });
    }
}
