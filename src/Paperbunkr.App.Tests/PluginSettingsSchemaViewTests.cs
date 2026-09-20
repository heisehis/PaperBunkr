using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Mounts the real <see cref="PluginSettingsSchemaView"/> in a headless window (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §6.4). The view-model tests can't see a broken compiled binding, a
/// wrong <c>x:DataType</c> or a template that shows the wrong control for a type - only the real XAML can -
/// so this checks that each setting type gets exactly its own control, that the values arrive, that an
/// edit typed into the real control is saved, and that an error message actually appears. It is a
/// structural check, not a look at how it renders: on-screen appearance is verified by a person.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PluginSettingsSchemaViewTests : IDisposable
{
    private const string Key = "view-plugin";

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _pluginsRoot;

    public PluginSettingsSchemaViewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_settingsview_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _pluginsRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_settingsview_plugins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_pluginsRoot, Key));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public bool IsAvailable => true;
        public string Protect(string plaintext) => "fake:" + plaintext;
        public string? TryUnprotect(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal) ? stored["fake:".Length..] : null;
        public bool IsProtected(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal);
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private PluginSettingsAccess Access()
    {
        File.WriteAllText(Path.Combine(_pluginsRoot, Key, "plugin.xml"), $"""
            <Plugin key="{Key}" name="View Plugin">
              <Command hook="Startup" key="{Key}.startup" name="Startup" script="run.csx" />
              <Settings>
                <Setting key="url" label="API URL" type="text" default="https://default.example" description="Where to sync"/>
                <Setting key="mode" label="Mode" type="choice" default="fast">
                  <Choice value="fast" label="Fast"/>
                  <Choice value="thorough" label="Thorough"/>
                </Setting>
                <Setting key="limit" label="Limit" type="number" default="10" min="1" max="100"/>
                <Setting key="enabled" label="Enabled" type="toggle" default="true"/>
                <Setting key="token" label="API token" type="secret"/>
              </Settings>
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(_pluginsRoot, Key, "run.csx"), "return 1;");
        var engine = new PluginEngine();
        engine.Discover(_pluginsRoot, new TestPluginEnvironment());
        return new PluginSettingsAccess(NewContext, k => engine.SettingsSchemas.GetValueOrDefault(k), new FakeProtector());
    }

    /// <summary>
    /// The headless test app is a bare <c>Application</c> - no theme, none of App.axaml's resources - so a
    /// templated control has no template and renders nothing. A <c>FluentTheme</c> goes on the test window
    /// only (so no other test sees it), which is enough for the window, ScrollViewer, TextBox and ToggleSwitch;
    /// <c>SettingsRow</c> resolves <c>PbIconSizeSm</c> as a static resource against the application, so that one
    /// token is added if absent (harmless: other tests only ever read their own keys).
    /// <para>
    /// <b>Known limit of this environment:</b> even so, an <c>ItemsControl</c> here never gets a control
    /// template, so the view's own <c>ItemsControl</c> stays empty. The header, the note and the real row
    /// <c>DataTemplate</c> (taken from that <c>ItemsControl</c>) are still the view's own XAML, so each row is
    /// built from that template directly and stacked under the view. What this therefore cannot show is the
    /// <c>ItemsControl</c> itself laying rows out in the running app - a person checks that on screen.
    /// </para>
    /// </summary>
    private static Window Show(PluginSettingsAccess access)
    {
        if (!Application.Current!.Resources.ContainsKey("PbIconSizeSm"))
        {
            Application.Current.Resources["PbIconSizeSm"] = 16.0;
        }

        var vm = new PluginSettingsSchemaViewModel(Key, "View Plugin", access.SchemaFor(Key)!, access);
        var stack = new StackPanel();
        stack.Children.Add(new PluginSettingsSchemaView { DataContext = vm });
        var window = new Window { Content = stack, Width = 700, Height = 900 };
        window.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        Settle(window);

        var items = window.GetVisualDescendants().OfType<ItemsControl>().Single();
        foreach (var row in vm.Rows)
        {
            var built = (Control)items.ItemTemplate!.Build(row)!;
            built.DataContext = row;
            stack.Children.Add(built);
        }

        Settle(window);
        return window;
    }

    /// <summary>ItemsControl realises its rows during a layout pass, which headless doesn't run on its own - force one, then let any queued binding work land.</summary>
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T[] Visible<T>(Window window) where T : Visual =>
        window.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible).ToArray();

    private string? Stored(string key)
    {
        using var context = NewContext();
        return context.PluginSettingStates.Where(s => s.PluginKey == Key && s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    [Fact]
    public void The_view_loads_and_shows_the_title_and_one_row_per_setting()
    {
        var window = Show(Access());
        try
        {
            var texts = Visible<TextBlock>(window).Select(t => t.Text).ToList();

            Assert.Contains("View Plugin settings", texts);
            foreach (string label in new[] { "API URL", "Mode", "Limit", "Enabled", "API token" })
            {
                Assert.Contains(label, texts);
            }

            Assert.Contains("Where to sync", texts);   // the description
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Each_setting_type_shows_exactly_its_own_control()
    {
        var window = Show(Access());
        try
        {
            Assert.Single(Visible<ToggleSwitch>(window));
            Assert.Single(Visible<SuggestBox>(window));

            // Three visible TextBoxes: the text setting, the number setting, the secret. (A SuggestBox
            // may host a TextBox internally, so count only those not inside one.)
            var textBoxes = Visible<TextBox>(window).Where(t => t.FindAncestorOfType<SuggestBox>() is null).ToArray();
            Assert.Equal(3, textBoxes.Length);
            Assert.Single(textBoxes, t => t.PasswordChar == '●');       // the secret is masked
            Assert.Equal(2, textBoxes.Count(t => t.PasswordChar == default));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void The_values_arrive_in_the_controls()
    {
        var access = Access();
        access.Set(Key, "limit", "42");
        var window = Show(access);
        try
        {
            var texts = Visible<TextBox>(window).Select(t => t.Text).ToList();

            Assert.Contains("https://default.example", texts);
            Assert.Contains("42", texts);
            Assert.True(Visible<ToggleSwitch>(window).Single().IsChecked);
            Assert.Equal("Fast", Visible<SuggestBox>(window).Single().Text);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Typing_into_the_real_text_box_saves_the_setting()
    {
        var access = Access();
        var window = Show(access);
        try
        {
            var url = Visible<TextBox>(window).Single(t => t.Text == "https://default.example");

            url.Text = "https://typed.example";
            Settle(window);

            Assert.Equal("https://typed.example", Stored("url"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Flipping_the_real_toggle_saves_the_setting()
    {
        var access = Access();
        var window = Show(access);
        try
        {
            Visible<ToggleSwitch>(window).Single().IsChecked = false;
            Settle(window);

            Assert.Equal("false", Stored("enabled"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Typing_a_secret_into_the_masked_box_stores_it_encrypted()
    {
        var access = Access();
        var window = Show(access);
        try
        {
            var secretBox = Visible<TextBox>(window).Single(t => t.PasswordChar == '●');

            secretBox.Text = "hunter2";
            Settle(window);

            Assert.Equal("fake:hunter2", Stored("token"));
            Assert.Equal("hunter2", access.Get(Key, "token"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void An_invalid_edit_shows_its_message_in_the_view_and_is_not_saved()
    {
        var access = Access();
        access.Set(Key, "limit", "20");
        var window = Show(access);
        try
        {
            var limit = Visible<TextBox>(window).Single(t => t.Text == "20" && t.PasswordChar == default);

            limit.Text = "5000";
            Settle(window);

            Assert.Contains(Visible<TextBlock>(window), t => t.Text?.Contains("at most 100") == true);
            Assert.Equal("20", Stored("limit"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void An_invalid_stored_value_shows_its_flag_as_soon_as_the_overlay_opens()
    {
        var access = Access();
        using (var context = NewContext())
        {
            context.PluginSettingStates.Add(new Paperbunkr.Data.Entities.PluginSettingState { PluginKey = Key, Key = "limit", Value = "5000" });
            context.SaveChanges();
        }

        var window = Show(access);
        try
        {
            Assert.Contains(Visible<TextBlock>(window), t => t.Text?.Contains("'5000' isn't valid") == true);
            Assert.Equal("5000", Stored("limit"));   // shown, not repaired
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_setting_with_no_problem_shows_no_error_text()
    {
        var window = Show(Access());
        try
        {
            Assert.DoesNotContain(Visible<TextBlock>(window), t => t.Text?.Contains("isn't valid") == true || t.Text?.Contains("Must be") == true);
        }
        finally
        {
            window.Close();
        }
    }
}
