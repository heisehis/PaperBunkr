using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>A text box reached with the arrow keys (or a controller) is only browsed: it does not take typing and the arrows go on past it, until Enter starts editing (docs/superpowers/specs/2026-10-03-input-service-design.md §14.4).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TextEntryModeTests
{
    private sealed class Region : UserControl
    {
        public Region() => KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
    }

    private sealed record Rig(Window Window, Button Before, TextBox Box, Button After);

    private static void WithRow(Action<Rig> body)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var before = new Button { Content = "before", Width = 100 };
            var box = new TextBox { Width = 200, Text = "" };
            var after = new Button { Content = "after", Width = 100 };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) };
            row.Children.Add(before);
            row.Children.Add(box);
            row.Children.Add(after);
            var region = new Region { Content = row };
            var window = new Window { Content = region, Width = 700, Height = 200 };
            InputHost.Attach(window, ReaderTestInput.Create());
            window.Show();
            RunLayout(window);
            body(new Rig(window, before, box, after));
            window.Close();
        });
    }

    private static void Type(Window window, string text)
    {
        window.KeyTextInput(text);
        RunLayout(window);
    }

    [Fact]
    public void ArrowingOntoABox_DoesNotTakeTyping_AndTheNextArrowMovesOn()
    {
        WithRow(rig =>
        {
            rig.Before.Focus(NavigationMethod.Directional);
            RunLayout(rig.Window);

            Press(rig.Window, Key.Right);
            Assert.True(rig.Box.IsFocused, "the first Right lands on the box");
            Assert.True(TextEntryMode.IsBrowsing(rig.Box));

            Type(rig.Window, "x");
            Assert.Equal("", rig.Box.Text);

            Press(rig.Window, Key.Right);
            Assert.True(rig.After.IsFocused, "the second Right moves on instead of moving a caret");
            Assert.False(rig.Box.IsReadOnly, "the box is restored when focus leaves it");
        });
    }

    [Fact]
    public void Enter_StartsEditing_ThenTheArrowsMoveTheCaret_AndEscGoesBackToBrowsing()
    {
        WithRow(rig =>
        {
            rig.After.Focus(NavigationMethod.Directional);
            Press(rig.Window, Key.Left);
            Assert.True(rig.Box.IsFocused && TextEntryMode.IsBrowsing(rig.Box));

            Press(rig.Window, Key.Return);
            Assert.False(TextEntryMode.IsBrowsing(rig.Box));
            Assert.False(rig.Box.IsReadOnly);

            Type(rig.Window, "ab");
            Assert.Equal("ab", rig.Box.Text);

            Press(rig.Window, Key.Left);
            Assert.True(rig.Box.IsFocused, "Left inside an edited box moves the caret");
            Assert.Equal(1, rig.Box.CaretIndex);

            Press(rig.Window, Key.Escape);
            Assert.True(rig.Box.IsFocused && TextEntryMode.IsBrowsing(rig.Box), "Esc leaves editing but not the box");
            Assert.Equal("ab", rig.Box.Text);

            Press(rig.Window, Key.Right);
            Assert.True(rig.After.IsFocused);
        });
    }

    [Fact]
    public void TabbingIntoABox_StillTakesTypingAtOnce()
    {
        WithRow(rig =>
        {
            rig.Box.Focus(NavigationMethod.Tab);
            RunLayout(rig.Window);

            Assert.False(TextEntryMode.IsBrowsing(rig.Box));
            Type(rig.Window, "hi");
            Assert.Equal("hi", rig.Box.Text);
        });
    }

    [Fact]
    public void AClickIntoABrowsingBox_StartsEditing()
    {
        WithRow(rig =>
        {
            rig.Before.Focus(NavigationMethod.Directional);
            Press(rig.Window, Key.Right);
            Assert.True(TextEntryMode.IsBrowsing(rig.Box));

            var centre = rig.Box.TranslatePoint(new Point(rig.Box.Bounds.Width / 2, rig.Box.Bounds.Height / 2), rig.Window)!.Value;
            rig.Window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
            rig.Window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
            RunLayout(rig.Window);

            Assert.False(TextEntryMode.IsBrowsing(rig.Box));
            Type(rig.Window, "z");
            Assert.Equal("z", rig.Box.Text);
        });
    }

    [Fact]
    public void ABoxThatIsReadOnlyByDesign_IsNotMadeEditableByEnter()
    {
        WithRow(rig =>
        {
            rig.Box.IsReadOnly = true;
            rig.Before.Focus(NavigationMethod.Directional);
            Press(rig.Window, Key.Right);
            Assert.True(rig.Box.IsFocused);
            Assert.False(TextEntryMode.IsBrowsing(rig.Box), "a read-only box has nothing to browse");

            Press(rig.Window, Key.Right);
            Assert.True(rig.After.IsFocused, "its arrows go on past it");
            Assert.True(rig.Box.IsReadOnly);
        });
    }
}
