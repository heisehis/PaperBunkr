using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The shared control families in Styles/Controls.axaml (docs/superpowers/specs/2026-10-19-ui-polish-shared-controls-design.md), loaded with
/// the real tokens over the stock Fluent theme (see FoundationStyleTests). Each test pins one decision the user made: compact 28/22 buttons,
/// solid-accent segments, the underline tab, the accent-bar sidebar item, the soft-accent filter chip and the raised card.
/// Set PB_RENDER_OUT to also write a sheet of every family per theme.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SharedControlsStyleTests
{
    [Fact]
    public void Buttons_AreCompact()
    {
        WithFoundation("default", () =>
        {
            var normal = new Button { Content = "Save", Classes = { "primary" } };
            var small = new Button { Content = "Read", Classes = { "primary", "sm" } };
            var window = Host(new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = { normal, small } });

            Assert.Equal(28, normal.Bounds.Height);
            Assert.Equal(22, small.Bounds.Height);
            window.Close();
        });
    }

    [Fact]
    public void GhostOutline_IsTheFaintColour()
    {
        WithFoundation("default", () =>
        {
            var ghost = new Button { Content = "Cancel", Classes = { "ghost" } };
            var window = Host(ghost);

            // PbButtonGhostBorder is a theme role; its fallback (and the role's default token) is the faint text colour, not PbBorder.
            Assert.Equal(Color.Parse("#77726A"), ((ISolidColorBrush)ghost.BorderBrush!).Color);
            window.Close();
        });
    }

    [Fact]
    public void ActiveSegment_IsSolidAccent()
    {
        WithFoundation("default", () =>
        {
            var on = new Button { Content = "Series", Classes = { "segItem", "active" } };
            var off = new Button { Content = "Issues", Classes = { "segItem" } };
            var window = Host(new Border { Classes = { "segGroup" }, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { on, off } } });

            Assert.Equal(Token("PbAccentColor"), ((ISolidColorBrush)Presenter(on).Background!).Color);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)Presenter(off).Background!).Color);
            window.Close();
        });
    }

    [Fact]
    public void ActiveTab_HasTheAccentUnderline()
    {
        WithFoundation("default", () =>
        {
            var tab = new Button { Content = "Issues", Classes = { "tab", "active" } };
            var window = Host(tab);

            Assert.Equal(new Thickness(0, 0, 0, 2), tab.BorderThickness);
            Assert.Equal(Token("PbAccentColor"), ((ISolidColorBrush)Presenter(tab).BorderBrush!).Color);
            window.Close();
        });
    }

    [Fact]
    public void ActiveSidebarItem_HasTheAccentBar()
    {
        WithFoundation("default", () =>
        {
            var item = new Button { Content = "Earth-616", Classes = { "sideItemButton", "active" } };
            var other = new Button { Content = "Earth-1610", Classes = { "sideItemButton" } };
            var window = Host(new StackPanel { Width = 200, Children = { item, other } });

            Assert.Equal(new Thickness(3, 0, 0, 0), item.BorderThickness);
            Assert.Equal(Token("PbAccentColor"), ((ISolidColorBrush)Presenter(item).BorderBrush!).Color);
            // The bar is always there (transparent when not selected), so both labels start at the same x.
            Assert.Equal(item.BorderThickness, other.BorderThickness);
            window.Close();
        });
    }

    [Fact]
    public void ActiveFilterChip_IsSoftAccent()
    {
        WithFoundation("default", () =>
        {
            var chip = new Button { Content = "Unread", Classes = { "filterChip", "active" } };
            var window = Host(chip);

            Assert.Equal(Token("PbAccentSoftColor"), ((ISolidColorBrush)Presenter(chip).Background!).Color);
            window.Close();
        });
    }

    [Fact]
    public void Card_IsRaised()
    {
        WithFoundation("default", () =>
        {
            var card = new Border { Classes = { "card" }, Child = new TextBlock { Text = "Unread manga" } };
            var window = Host(card);

            Assert.Equal(Token("PbSurface2Color"), ((ISolidColorBrush)card.Background!).Color);
            Assert.Equal(new Thickness(16), card.Padding);
            window.Close();
        });
    }

    /// <summary>Not an assertion: every family in Default, Daylight and Matrix, written when PB_RENDER_OUT is set.</summary>
    [Fact]
    public void RenderControlSheets()
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
                StackPanel Row(params Control[] items)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
                    row.Children.AddRange(items);
                    return row;
                }

                Border Chip(string text, params string[] classes)
                {
                    var b = new Border { Child = new TextBlock { Text = text } };
                    b.Classes.AddRange(classes);
                    return b;
                }

                Button B(string text, params string[] classes)
                {
                    var b = new Button { Content = text };
                    b.Classes.AddRange(classes);
                    return b;
                }

                var sheet = new StackPanel
                {
                    Margin = new Thickness(24),
                    Children =
                    {
                        Row(B("Save", "primary"), B("Edit", "secondary"), B("Cancel", "ghost"), B("Delete", "danger"), B("…", "iconButton")),
                        Row(B("Read", "primary", "sm"), B("Add series", "secondary", "sm"), B("Clear", "ghost", "sm"), B("Pinned", "secondary", "active")),
                        Row(new Border { Classes = { "segGroup" }, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { B("Series", "segItem", "active"), B("Issues", "segItem") } } },
                            new Border { Classes = { "segGroup" }, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { B("Overview", "segItem"), B("Map", "segItem", "active"), B("Timeline", "segItem") } } }),
                        new Border { Classes = { "tabStrip" }, Margin = new Thickness(0, 0, 0, 12), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { B("Issues", "tab", "active"), B("Related", "tab"), B("Details", "tab") } } },
                        Row(B("Unread 579", "filterChip", "active"), B("Reading 28", "filterChip"), Chip("Delinquents", "pbChip"), Chip("Ongoing", "statusBadge", "success"), Chip("Missing", "statusBadge", "danger")),
                        new StackPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Children = { B("DC Universe", "sideItemButton"), B("Earth-616", "sideItemButton", "active"), B("Earth-1610", "sideItemButton") } },
                        Row(new Border { Classes = { "card" }, Width = 200, Child = new TextBlock { Text = "Unread manga", TextWrapping = TextWrapping.Wrap } },
                            new Button { Classes = { "clickCard" }, Width = 200, Content = "Missing metadata" }),
                        new Border { Classes = { "dropdown" }, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Child = new StackPanel { Children = { B("Default view", "menuRow", "active"), B("Save current view as…", "menuRow") } } },
                    },
                };
                var window = new Window { Content = sheet, Width = 620, Height = 560, Background = (IBrush)Application.Current!.FindResource("PbBgBrush")! };
                window.Show();
                RunLayout(window);

                var rtb = new RenderTargetBitmap(new PixelSize(620, 560));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"controls-{theme}.png"));
                window.Close();
            });
        }
    }

    private static ContentPresenter Presenter(Button button) => Part<ContentPresenter>(button, "PART_ContentPresenter");
}
