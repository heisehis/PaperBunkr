using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views.Preferences;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-09-26-preferences-reader-organize-tabs-design.md - the shared tab state and the strip bound to it.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SettingsTabsTests
{
    private static SettingsTabs Make() => new(new SettingsTabItem[] { new("a", "Alpha"), new("b", "Beta"), new("c", "Gamma") });

    [Fact]
    public void FirstTab_IsSelectedByDefault()
    {
        var tabs = Make();

        Assert.Equal("a", tabs.SelectedKey);
        Assert.Same(tabs.Items[0], tabs.Selected);
    }

    [Fact]
    public void Select_KnownKey_SwitchesAndNotifiesSelectedKey()
    {
        var tabs = Make();
        var changed = new List<string?>();
        tabs.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(tabs.Select("c"));

        Assert.Equal("c", tabs.SelectedKey);
        Assert.Contains(nameof(SettingsTabs.SelectedKey), changed);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData(null)]
    public void Select_UnknownKey_ChangesNothing(string? key)
    {
        var tabs = Make();
        tabs.Select("b");

        Assert.False(tabs.Select(key));

        Assert.Equal("b", tabs.SelectedKey);
    }

    [Fact]
    public void NoTabs_IsRejected() => Assert.Throws<ArgumentException>(() => new SettingsTabs(Array.Empty<SettingsTabItem>()));

    /// <summary>Runs <paramref name="check"/> against a shown window hosting a strip bound to <paramref name="tabs"/>, with the Fluent theme applied.</summary>
    private static void WithStrip(SettingsTabs tabs, Action<SettingsTabStrip, TabStrip> check)
    {
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            var strip = new SettingsTabStrip { Tabs = tabs };
            var window = new Window { Width = 700, Height = 200, Content = strip };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                check(strip, strip.GetVisualDescendants().OfType<TabStrip>().Single());
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
        }
    }

    [Fact]
    public void Strip_ShowsOneItemPerTab_WithTheModelsSelectionSelected()
    {
        var tabs = Make();
        tabs.Select("b");

        WithStrip(tabs, (strip, tabStrip) =>
        {
            Assert.Equal(3, strip.GetVisualDescendants().OfType<TabStripItem>().Count());
            Assert.Equal("Beta", (tabStrip.SelectedItem as SettingsTabItem)?.Title);
        });
    }

    [Fact]
    public void Strip_FollowsTheModel_AndWritesUserSelectionBack()
    {
        var tabs = Make();

        WithStrip(tabs, (_, tabStrip) =>
        {
            tabs.Select("c");
            Assert.Same(tabs.Items[2], tabStrip.SelectedItem); // a search hit selects a tab from code

            tabStrip.SelectedItem = tabs.Items[1]; // the user clicks (or arrows to) a tab
            Assert.Equal("b", tabs.SelectedKey);
        });
    }

    [Fact]
    public void Strip_BoundLateToAModelOnALaterTab_DoesNotResetItToTheFirstTab()
    {
        var tabs = Make();
        tabs.Select("c"); // e.g. a deep link picked the tab before the section was ever shown

        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            var strip = new SettingsTabStrip();
            var window = new Window { Width = 700, Height = 200, Content = strip };
            window.Show();
            try
            {
                window.UpdateLayout();
                strip.Tabs = tabs; // whatever the TabStrip selects by default while taking the list must not reach the model
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("c", tabs.SelectedKey);
                Assert.Same(tabs.Items[2], strip.GetVisualDescendants().OfType<TabStrip>().Single().SelectedItem);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
        }
    }
}
