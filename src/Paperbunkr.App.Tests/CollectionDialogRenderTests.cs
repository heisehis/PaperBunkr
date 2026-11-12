using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Loads the real Edit Collection dialog (docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md) with the app styles, checks the
/// new parts exist and respond to the view model, and - with PB_RENDER_OUT set - writes PNGs per theme for a human look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionDialogRenderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collectiondialogrender_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _collectionId;

    public CollectionDialogRenderTests()
    {
        CollectionPropertiesScreenViewModel.ResetSectionState();
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var ids = new List<int>();
        foreach (string name in new[] { "Annihilation 2099", "Conquest 2099", "Miguel O'Hara: Spider-Man 2099" })
        {
            var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
            context.Series.Add(series);
            context.SaveChanges();
            ids.Add(series.Id);
        }

        var collection = CollectionService.Create(context, "2099");
        CollectionService.SetAppearance(context, collection.Id, null, "#C97C9E", null, isAutoCover: true);
        CollectionService.AddItems(context, collection.Id, seriesIds: ids);
        _collectionId = collection.Id;
    }

    public void Dispose()
    {
        CollectionPropertiesScreenViewModel.ResetSectionState();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private CollectionPropertiesOverlay NewOverlay(out CollectionPropertiesScreenViewModel vm)
    {
        vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        return new CollectionPropertiesOverlay { DataContext = vm };
    }

    [Fact]
    public void Dialog_LoadsWithTheNewPartsAndFollowsTheViewModel()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            using var styles = AppStyles();
            var overlay = NewOverlay(out var vm);
            var window = new Window { Content = overlay, Width = 700, Height = 1100 };
            window.Show();
            RunLayout(window);

            Assert.NotNull(overlay.FindControl<Button>("CustomSwatchButton"));
            Assert.NotNull(overlay.FindControl<Button>("SortMembersButton"));

            // Three member rows, each with a drag handle and a cover slot.
            var handles = overlay.GetVisualDescendants().OfType<Border>().Where(b => CollectionMemberRowDrag.GetHandle(b)).ToList();
            Assert.Equal(3, handles.Count);

            // The icon picker shows every symbol, hero, publisher and "My icons" choice, and a cover-member button sits on each manual row.
            var glyphButtons = overlay.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("glyphChoice")).ToList();
            Assert.Equal(Paperbunkr.App.Models.CollectionGlyphs.All.Count + Paperbunkr.App.Models.HeroGlyphs.All.Count + vm.PublisherChoices.Count + vm.MyIconChoices.Count,
                glyphButtons.Count);
            var useAsCover = overlay.GetVisualDescendants().OfType<Button>()
                .Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Use as the cover").ToList();
            Assert.Equal(3, useAsCover.Count);

            // The icon sections' first-column tiles keep room for their focus ring (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).
            foreach (var section in glyphButtons.Select(b => b.GetVisualAncestors().OfType<ItemsControl>().First()).Distinct())
            {
                Assert.Empty(ClippedFocusRings(window, section));
            }

            vm.SetGlyphCommand.Execute("Rocket");
            RunLayout(window);
            Assert.Single(overlay.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("glyphChoice") && b.Classes.Contains("selected")));

            // Collapsed sections stay out of the way until opened.
            Assert.False(vm.IsRulesOpen);
            vm.ToggleRulesCommand.Execute(null);
            RunLayout(window);
            Assert.True(vm.IsRulesOpen);

            window.Close();
        });
    }

    [Fact]
    public void RenderDialog()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var overlay = NewOverlay(out var vm);
                vm.Description = "A short description of the collection.";
                var window = new Window { Content = overlay, Width = 700, Height = 1100 };
                window.Show();
                RunLayout(window);
                var rtb = new RenderTargetBitmap(new PixelSize(700, 1100));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"collection-dialog-{theme}.png"));
                window.Close();
            });
        }
    }
}
