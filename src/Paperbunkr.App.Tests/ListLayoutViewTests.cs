using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The list-layout views actually load and lay out (docs/superpowers/specs/2026-10-04-list-layouts-design.md §8): the
/// List Options and Edit Layouts overlays, the Details header's resize grips, and a Library with custom caption lines
/// and tile elements. Headless; how the drags feel is an on-screen check.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ListLayoutViewTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ListLayoutViewTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_listlayout_view_test_{Guid.NewGuid():N}.db");
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

    private static LibraryScreenViewModel CreateVm() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { }, promptForName: (_, cb) => cb("Saved"));

    /// <summary>The Fluent theme and the app tokens the views' StaticResources need (App.axaml isn't loaded headless) - the same
    /// baseline <see cref="LibraryScreenViewTests"/> uses.</summary>
    private static void WithThemeAndTokens(Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var resources = Application.Current.Resources;
        var tokens = new Dictionary<string, object>
        {
            ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
            ["PbIconSizeXs"] = 14d,
            ["PbIconSizeSm"] = 16d,
            ["PbIconSizeLg"] = 24d,
            ["PbRadiusChip"] = new CornerRadius(6),
            ["PbElevationShadow"] = Avalonia.Media.BoxShadows.Parse("0 2 8 0 #40000000"),
            ["PbDisplayFontFamily"] = new Avalonia.Media.FontFamily("avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue"),
        };
        var added = tokens.Keys.Where(k => !resources.ContainsKey(k)).ToList();
        foreach (var key in added)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in added)
            {
                resources.Remove(key);
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 1400, Height = 900 };
        window.Show();
        RunLayout(window);
        return window;
    }

    private static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    [Fact]
    public void ListOptions_Library_ShowsEveryColumn_AndSwitchesTabs()
    {
        WithThemeAndTokens(() =>
        {
            var vm = CreateVm();
            var options = vm.CreateListOptions(() => { });

            // CE opens the dialog on the tab of the current view mode: a poster grid opens on Thumbnails.
            Assert.True(options.IsThumbnailsTab);
            options.SelectTabCommand.Execute("details");
            var window = Show(new ListOptionsOverlay { DataContext = options });
            try
            {
                var lists = window.GetVisualDescendants().OfType<ListBox>().ToList();
                var columns = lists.Single(l => l.Name is null && ReferenceEquals(l.ItemsSource, options.Columns));
                Assert.True(columns.IsEffectivelyVisible);
                Assert.Equal(IssueListFieldCatalog.ColumnFields.Count, options.Columns.Count);

                options.SelectTabCommand.Execute("tiles");
                RunLayout(window);

                Assert.False(columns.IsEffectivelyVisible);
                var tiles = window.GetVisualDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, options.TileElements));
                Assert.True(tiles.IsEffectivelyVisible);

                // The tab strip is the shared "tab" / "active" convention, so the bumpers and Ctrl+PageUp/PageDown drive it.
                var tabs = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tab") && b.IsEffectivelyVisible).ToList();
                Assert.Equal(3, tabs.Count);
                Assert.Single(tabs, b => b.Classes.Contains("active"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ListOptions_Books_HasNoTabStrip()
    {
        WithThemeAndTokens(() =>
        {
            var books = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
            var window = Show(new ListOptionsOverlay { DataContext = books.CreateListOptions(() => { }) });
            try
            {
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("tab") && b.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EditLayouts_ListsTheSavedLayouts()
    {
        WithThemeAndTokens(() =>
        {
            var vm = CreateVm();
            vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
            var window = Show(new EditLayoutsOverlay { DataContext = vm.ListLayouts });
            try
            {
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Saved" && t.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DetailsHeader_HasAResizeGripPerVisibleColumn_ThatFollowsTheColumnWidth()
    {
        WithThemeAndTokens(() =>
        {
            var vm = CreateVm();
            vm.Granularity = LibraryContentGranularity.Issue;
            vm.SetViewModeCommand.Execute(LibraryViewMode.DetailsTable);
            var window = Show(new LibraryScreen { DataContext = vm });
            try
            {
                var grips = window.GetVisualDescendants().OfType<Thumb>().Where(t => t.Classes.Contains("columnGrip") && t.IsEffectivelyVisible).ToList();
                Assert.Equal(vm.DetailsColumns.Count(c => c.IsVisible), grips.Count);

                var column = vm.DetailsColumns.First(c => c.IsVisible);
                var header = window.GetVisualDescendants().OfType<Button>()
                    .First(b => b.Classes.Contains("detailsHeader") && ReferenceEquals(b.DataContext, column));
                vm.SetDetailsColumnWidth(column, 260, commit: true);
                RunLayout(window);

                Assert.Equal(260, header.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Library_WithCustomCaptionsAndTileElements_LaysOut()
    {
        WithThemeAndTokens(() =>
        {
            var vm = CreateVm();
            vm.ApplyListOptions(new ListLayoutState(
                CaptionFields: new[] { IssueListSortField.Title, IssueListSortField.Writer, IssueListSortField.Publisher },
                TileElements: new[] { TileTextElement.Title, TileTextElement.Writer, TileTextElement.Year }));
            var window = Show(new LibraryScreen { DataContext = vm });
            try
            {
                vm.SelectTilesGridCommand.Execute(null);
                RunLayout(window);
                vm.SelectPosterGridCommand.Execute(null);
                RunLayout(window);

                Assert.True(vm.HasCustomCaptions);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheShortcuts_AreCeS_AndClashWithNothingElse()
    {
        var byId = InputActions.Core.ToDictionary(a => a.Id);
        var listOptions = byId[InputActionIds.ListOptions];
        var editLayouts = byId[InputActionIds.EditListLayouts];

        Assert.Equal(InputBinding.ForKey(Avalonia.Input.Key.L, Avalonia.Input.KeyModifiers.Control), Assert.Single(listOptions.Defaults));
        Assert.Equal(InputBinding.ForKey(Avalonia.Input.Key.L, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt), Assert.Single(editLayouts.Defaults));
        Assert.Empty(byId[InputActionIds.SaveListLayout].Defaults);

        foreach (var action in new[] { listOptions, editLayouts })
        {
            var binding = action.Defaults.Single();
            Assert.DoesNotContain(InputActions.Core, other => other.Id != action.Id && other.Defaults.Contains(binding));
        }
    }
}
