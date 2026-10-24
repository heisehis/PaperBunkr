using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The 2026-10-19 UI polish foundation's shared field, popup and tooltip looks (docs/superpowers/specs/2026-10-19-ui-polish-foundation-
/// design.md §4-5, §8). Loads the real AppTokens.axaml, FluentBridge.axaml, FormControls.axaml and Overlays.axaml over the stock Fluent theme
/// (the test assembly never builds FluentAvaloniaTheme; see FocusTestHarness.LoadAppResources for why). Stock Fluent and FluentAvalonia draw a
/// TextBox from the same TextControl* keys, so the bridge is exercised either way; BridgeKeysExistInFluentAvalonia separately pins every key
/// to the FluentAvalonia build the app actually ships.
/// Set PB_RENDER_OUT to a folder to also write a form sample per theme as PNG for a visual check.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FoundationStyleTests
{
    [Fact]
    public void BridgeKeysExistInFluentAvalonia()
    {
        string bridge = File.ReadAllText(Path.Combine(AppRoot(), "Styles", "FluentBridge.axaml"));
        var keys = Regex.Matches(bridge, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(keys);

        byte[] dll = File.ReadAllBytes(typeof(FluentAvalonia.Styling.FluentAvaloniaTheme).Assembly.Location);
        var missing = keys.Where(k => !Contains(dll, Encoding.UTF8.GetBytes(k)) && !Contains(dll, Encoding.Unicode.GetBytes(k))).ToList();

        Assert.True(missing.Count == 0,
            "FluentBridge.axaml overrides keys this FluentAvalonia build doesn't use (renamed in an upgrade?) - the fields would silently fall back " +
            "to stock Fluent: " + string.Join(", ", missing));
    }

    [Fact]
    public void TextBox_IsAnUnderlinedField()
    {
        WithFoundation("default", () =>
        {
            var box = new TextBox { Text = "Earth-616", Width = 240 };
            var window = Host(box);
            var border = Part<Border>(box, "PART_BorderElement");

            Assert.Equal(new Thickness(0, 0, 0, 1), border.BorderThickness);
            Assert.Equal(new CornerRadius(4, 4, 0, 0), border.CornerRadius);
            Assert.Equal(Token("PbTextMutedColor"), ((ISolidColorBrush)border.BorderBrush!).Color);
            Assert.Equal(Token("PbSurface2Color"), ((ISolidColorBrush)border.Background!).Color);
            window.Close();
        });
    }

    [Fact]
    public void SearchBox_KeepsItsRoundedBorder()
    {
        WithFoundation("default", () =>
        {
            var box = new TextBox { Classes = { "searchBox" }, Width = 240 };
            var window = Host(box);
            var border = Part<Border>(box, "PART_BorderElement");

            Assert.Equal(new Thickness(1), border.BorderThickness);
            window.Close();
        });
    }

    [Fact]
    public void FieldColours_FollowALiveThemeSwitch()
    {
        WithFoundation("default", () =>
        {
            var box = new TextBox { Text = "x", Width = 240 };
            var window = Host(box);
            var border = Part<Border>(box, "PART_BorderElement");

            // What ThemeRuntimeApplier.SetColor does on a theme switch: rewrite the Pb*Color (and a fresh Pb*Brush) in Application.Resources.
            ApplyTheme("matrix");
            RunLayout(window);

            Assert.Equal(Token("PbSurface2Color"), ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(Color.Parse("#1FAF48"), ((ISolidColorBrush)border.BorderBrush!).Color);
            window.Close();
        });
    }

    [Fact]
    public void Placeholder_UsesTheMutedColour()
    {
        WithFoundation("default", () =>
        {
            var box = new TextBox { PlaceholderText = "Optional", Width = 240 };
            var window = Host(box);

            Assert.True(box.TryFindResource("TextControlPlaceholderForeground", box.ActualThemeVariant, out var brush));
            Assert.Equal(Token("PbTextMutedColor"), ((ISolidColorBrush)brush!).Color);
            window.Close();
        });
    }

    [Fact]
    public void FlyoutAndToolTip_UseTheFloatingPanelSurfaces()
    {
        WithFoundation("default", () =>
        {
            var presenter = new FlyoutPresenter { Content = "77 series" };
            var tip = new ToolTip { Content = "Close" };
            var window = Host(new StackPanel { Children = { presenter, tip } });

            Assert.Same(Application.Current!.FindResource("PbPanelFloatingBackground"), presenter.Background);
            Assert.Equal(Token("PbSurface3Color"), ((ISolidColorBrush)tip.Background!).Color);
            Assert.Equal(320, tip.MaxWidth);
            window.Close();
        });
    }

    /// <summary>Not an assertion: writes the form sample in Default, Daylight and Matrix for a human look when PB_RENDER_OUT is set.</summary>
    [Fact]
    public void RenderFormSamples()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            WithFoundation(theme, () =>
            {
                var focused = new TextBox { Text = "The main Marvel universe" };
                var error = new TextBox { Text = "personal-client-7a99" };
                var form = new StackPanel
                {
                    Margin = new Thickness(24),
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "NAME", Classes = { "fieldLabel" } },
                        new TextBox { Text = "Earth-616" },
                        new TextBlock { Text = "PUBLISHER", Classes = { "fieldLabel" }, Margin = new Thickness(0, 10, 0, 4) },
                        new TextBox { PlaceholderText = "Optional" },
                        new TextBlock { Text = "DESCRIPTION", Classes = { "fieldLabel" }, Margin = new Thickness(0, 10, 0, 4) },
                        focused,
                        new TextBlock { Text = "CLIENT ID", Classes = { "fieldLabel" }, Margin = new Thickness(0, 10, 0, 4) },
                        error,
                        new TextBlock { Text = "WORKFLOW", Classes = { "fieldLabel" }, Margin = new Thickness(0, 10, 0, 4) },
                        new ComboBox { ItemsSource = new[] { "Publisher Universe" }, SelectedIndex = 0, Width = 220 },
                        new TextBlock { Text = "SEARCH", Classes = { "fieldLabel" }, Margin = new Thickness(0, 10, 0, 4) },
                        new TextBox { Classes = { "searchBox" }, PlaceholderText = "Search settings…" },
                        new FlyoutPresenter { Content = new TextBlock { Text = "77 series from its events aren't in this continuity" }, Margin = new Thickness(0, 14, 0, 0) },
                    },
                };
                var window = new Window { Content = form, Width = 420, Height = 560, Background = (IBrush)Application.Current!.FindResource("PbBgBrush")! };
                window.Show();
                RunLayout(window);
                focused.Focus();
                DataValidationErrors.SetErrors(error, new object[] { "rejected" });
                RunLayout(window);

                var rtb = new RenderTargetBitmap(new PixelSize(420, 560));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"foundation-form-{theme}.png"));
                window.Close();
            });
        }
    }

    // --------------------------------------------------------------------------------------------------------------------------------

    private static readonly string[] ThemeColorKeys =
        ["bg", "chrome", "border", "text", "textMuted", "textFaint", "accent", "accentText", "accentSoft", "surface0", "surface1", "surface2", "surface3", "success", "badge", "badgeText"];

    internal static void WithFoundation(string theme, Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var app = Application.Current!;
        var fluent = new Avalonia.Themes.Fluent.FluentTheme();
        var dictionaries = new[]
        {
            new ResourceInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/AppTokens.axaml") },
            new ResourceInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/FluentBridge.axaml") },
        };
        var styles = new[]
        {
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Typography.axaml") },
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/FormControls.axaml") },
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/ScreenChrome.axaml") },
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Overlays.axaml") },
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Badges.axaml") },
            new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Controls.axaml") },
        };
        app.Styles.Add(fluent);
        foreach (var d in dictionaries)
        {
            app.Resources.MergedDictionaries.Add(d);
        }

        foreach (var s in styles)
        {
            app.Styles.Add(s);
        }

        // A real ThemeService apply in an earlier test writes theme values (e.g. a component role's PbButtonGhostBorder) straight into
        // app.Resources, where they beat the token dictionaries merged above - so this test would see that theme's colours, not the foundation's.
        // Set those leftovers aside for the body and put them back after.
        var setAside = new Dictionary<object, object?>();
        foreach (var d in dictionaries)
        {
            foreach (object key in d.Loaded is IDictionary<object, object?> loaded ? loaded.Keys : [])
            {
                if (app.Resources.TryGetValue(key, out object? value) && setAside.TryAdd(key, value))
                {
                    app.Resources.Remove(key);
                }
            }
        }

        var written = new List<string>();
        try
        {
            written.AddRange(ApplyTheme(theme));
            body();
        }
        finally
        {
            foreach (string key in written)
            {
                app.Resources.Remove(key);
            }

            foreach (var (key, value) in setAside)
            {
                app.Resources[key] = value;
            }

            foreach (var s in styles)
            {
                app.Styles.Remove(s);
            }

            foreach (var d in dictionaries)
            {
                app.Resources.MergedDictionaries.Remove(d);
            }

            app.Styles.Remove(fluent);
        }
    }

    /// <summary>Writes a v1 theme's colours the way ThemeRuntimeApplier.SetColor does (Pb*Color plus a fresh Pb*Brush) and returns the keys.</summary>
    internal static IEnumerable<string> ApplyTheme(string theme)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppRoot(), "Assets", "Skins", theme, "theme.json")));
        var colors = doc.RootElement.GetProperty("colors");
        var keys = new List<string>();
        foreach (string name in ThemeColorKeys)
        {
            var color = Color.Parse(colors.GetProperty(name).GetString()!);
            string pb = "Pb" + char.ToUpperInvariant(name[0]) + name[1..];
            Application.Current!.Resources[pb + "Color"] = color;
            Application.Current.Resources[pb + "Brush"] = new SolidColorBrush(color);
            keys.Add(pb + "Color");
            keys.Add(pb + "Brush");
        }

        return keys;
    }

    internal static Window Host(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        RunLayout(window);
        return window;
    }

    internal static T Part<T>(Control templated, string name) where T : Control =>
        templated.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    internal static Color Token(string key) => (Color)Application.Current!.FindResource(key)!;

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static string TestFolder([CallerFilePath] string caller = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));

    private static string AppRoot() => Path.GetFullPath(Path.Combine(TestFolder(), "..", "Paperbunkr.App"));
}
