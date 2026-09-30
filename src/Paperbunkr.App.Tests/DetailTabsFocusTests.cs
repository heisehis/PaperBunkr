using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard reach for the Detail screen's issue tiles (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md):
/// focus lands on a tile with no prior click, Enter opens the issue, Space focuses it, and focus survives an issue-list reset.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailTabsFocusTests
{
    private static IssueCardSample Issue(int id) => new() { Id = id, Title = $"#{id}", CoverBrush = Brushes.Gray };

    private static (Window Window, DetailTabs Tabs, DetailTabsViewModel Vm, List<int> Opened, Button Sibling) Show(params int[] ids)
    {
        var opened = new List<int>();
        var vm = new DetailTabsViewModel(_ => { }, _ => { }, null, () => throw new InvalidOperationException("no database in this test"), openInReader: opened.Add);
        Fill(vm, ids);
        vm.ActiveTab = "issues";
        var tabs = new DetailTabs { DataContext = vm, Margin = new Avalonia.Thickness(28, 0, 28, 0) }; // as DetailScreen.axaml hosts it
        var sibling = new Button { Content = "Rail" };
        var panel = new StackPanel();
        panel.Children.Add(sibling);
        panel.Children.Add(tabs);
        var window = new Window { Content = new ScrollViewer { Content = panel }, Width = 1000, Height = 800 };
        window.Show();
        RunLayout(window);
        return (window, tabs, vm, opened, sibling);
    }

    private static void Fill(DetailTabsViewModel vm, int[] ids)
    {
        var issues = ids.Select(Issue).ToList();
        vm.Issues.Clear();
        vm.IssueGroups.Clear();
        foreach (var issue in issues)
        {
            vm.Issues.Add(issue);
        }

        var group = new IssueRunGroup();
        foreach (var issue in issues)
        {
            group.Items.Add(issue);
        }

        vm.IssueGroups.Add(group);
    }

    private static Control FocusedTile(Window window) => Assert.IsAssignableFrom<Control>(Focused(window));

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var (window, tabs, vm, _, _) = Show(1, 2, 3, 4, 5, 6, 7, 8);
            var clipped = new List<string>();
            foreach (var mode in Enum.GetValues<Paperbunkr.Data.Entities.DetailIssueViewMode>())
            {
                vm.IssueViewMode = mode;
                RunLayout(window);
                clipped.AddRange(ClippedFocusRings(window, tabs).Select(c => $"[{mode}] {c}"));
            }

            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_FocusLandsOnAnIssueTile()
    {
        WithThemeAndTokens(() =>
        {
            var (window, tabs, _, _, _) = Show(1, 2, 3);

            Assert.True(FocusIsInside(window, tabs), $"focus on {Focused(window)}");
            Assert.IsType<IssueCardSample>(FocusedTile(window).DataContext);
            window.Close();
        });
    }

    [Fact]
    public void Enter_OpensTheIssue_SpaceFocusesIt()
    {
        WithThemeAndTokens(() =>
        {
            var (window, _, vm, opened, _) = Show(1, 2, 3);
            var second = tilesOf(window)[1];
            second.Focus();
            RunLayout(window);

            Press(window, Key.Space);
            Assert.True(vm.Issues[1].IsSelected);
            Assert.Empty(opened);

            Press(window, Key.Enter);
            Assert.Equal(new[] { 2 }, opened);
            window.Close();
        });

        static List<Control> tilesOf(Window w) => w.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Focusable && c.DataContext is IssueCardSample && c.IsEffectivelyVisible).ToList();
    }

    [Fact]
    public void Arrows_MoveBetweenIssueTiles_InEveryViewMode()
    {
        WithThemeAndTokens(() =>
        {
            var (window, _, vm, _, _) = Show(1, 2, 3);
            foreach (var mode in Enum.GetValues<Paperbunkr.Data.Entities.DetailIssueViewMode>())
            {
                vm.IssueViewMode = mode;
                RunLayout(window);
                // Only the tile elements themselves - a card also holds focusable controls of its own that share its DataContext.
                var tiles = window.GetVisualDescendants().OfType<Control>()
                    .Where(c => c.Focusable && c.IsEffectivelyVisible && (c.Classes.Contains("issueTile") || c.Classes.Contains("issueRow") || c.Classes.Contains("issueCard")))
                    .ToList();
                tiles[0].Focus(NavigationMethod.Directional);
                RunLayout(window);

                Press(window, mode == Paperbunkr.Data.Entities.DetailIssueViewMode.List ? Key.Down : Key.Right);

                Assert.True(ReferenceEquals(tiles[1], Focused(window)), $"[{mode}] focus on {Focused(window)}, expected the second tile");
            }

            window.Close();
        });
    }

    [Fact]
    public void ResettingTheIssueList_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            var (window, tabs, vm, _, _) = Show(1, 2, 3);
            Assert.True(FocusIsInside(window, tabs));

            Fill(vm, new[] { 4, 5 });
            RunLayout(window);

            Assert.True(FocusIsInside(window, tabs), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void AResetWhileFocusIsInASiblingRegion_DoesNotStealFocus()
    {
        WithThemeAndTokens(() =>
        {
            var (window, _, vm, _, sibling) = Show(1, 2, 3);
            sibling.Focus();
            RunLayout(window);
            Assert.Same(sibling, Focused(window));

            Fill(vm, new[] { 4, 5 });
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }
}
