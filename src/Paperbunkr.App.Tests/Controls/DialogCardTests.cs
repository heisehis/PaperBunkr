using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Controls;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>The shared dialog anatomy (docs/superpowers/specs/2026-10-19-ui-polish-dialogs-design.md): fixed widths per size, a height cap
/// that follows the window, one close button (the card's, taking over an enclosing OverlayShell's), and no empty footer.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DialogCardTests
{
    [Theory]
    [InlineData(DialogSize.Small, 420)]
    [InlineData(DialogSize.Medium, 560)]
    [InlineData(DialogSize.Large, 760)]
    [InlineData(DialogSize.XLarge, 960)]
    public void Size_SetsTheWidth(DialogSize size, double width)
    {
        var card = new DialogCard { Size = size };
        card.Measure(new Size(2000, 2000));
        Assert.Equal(width, card.DesiredSize.Width);
    }

    [Fact]
    public void ANarrowHost_ShrinksTheCardInsteadOfClippingIt()
    {
        WithFoundation("default", () =>
        {
            var card = new DialogCard { Title = "Scrape complete", Size = DialogSize.Medium, Content = new TextBlock { Text = new string('x', 400), TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
            var window = new Window { Width = 480, Height = 400, Content = card };
            window.Show();
            RunLayout(window);
            Assert.True(card.Bounds.Width <= 480.5, $"card is {card.Bounds.Width} wide in a 480 window");
            Assert.True(card.Bounds.X >= -0.5, $"card starts at {card.Bounds.X}");
            window.Close();
        });
    }

    [Fact]
    public void MaxHeight_FollowsTheWindow()
    {
        WithFoundation("default", () =>
        {
            var card = new DialogCard { Title = "Edit reading list", Content = new Border { Height = 2000 } };
            var window = new Window { Width = 1200, Height = 700, Content = card };
            window.Show();
            RunLayout(window);
            Assert.Equal(700 - DialogCard.WindowMargin, card.MaxHeight);

            window.Height = 500;
            RunLayout(window);
            Assert.Equal(500 - DialogCard.WindowMargin, card.MaxHeight);
            Assert.True(card.Bounds.Height <= 500 - DialogCard.WindowMargin + 0.5);
            window.Close();
        });
    }

    [Fact]
    public void ConfirmDialog_LongMessageWrapsInsideTheCard()
    {
        WithFoundation("default", () =>
        {
            var vm = new Paperbunkr.App.ViewModels.ConfirmDialogViewModel();
            _ = new Paperbunkr.App.Services.DialogService(vm).ShowAsync(new Paperbunkr.App.Services.ConfirmDialogRequest(
                "Delete the reading list \"\"Avengers/X-Men\" Axis\"? The comics themselves stay in your library, only the list is removed."));
            var view = new Paperbunkr.App.Views.ConfirmDialogView { DataContext = vm };
            var window = new Window { Width = 440, Height = 400, Content = view };
            window.Show();
            RunLayout(window);

            var card = view.GetVisualDescendants().OfType<DialogCard>().Single();
            var message = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == vm.Message);
            var right = message.TranslatePoint(new Point(message.Bounds.Width, 0), card)!.Value.X;
            Assert.True(right <= card.Bounds.Width + 0.5, $"message ends at {right}, card is {card.Bounds.Width} wide");
            Assert.True(card.Bounds.X >= -0.5);
            window.Close();
        });
    }

    [Fact]
    public void InsideAnOverlayShell_TheCardOwnsTheOnlyCloseButton()
    {
        WithFoundation("default", () =>
        {
            int closed = 0;
            var shell = new OverlayShell { IsOpen = true, CloseCommand = new RelayCommand(() => closed++) };
            var card = new DialogCard { Title = "Confirm" };
            shell.Content = card;
            var window = new Window { Width = 900, Height = 700, Content = shell };
            window.Show();
            RunLayout(window);

            Assert.False(shell.ShowCloseButton);
            Assert.Same(shell.CloseCommand, card.EffectiveCloseCommand);
            card.EffectiveCloseCommand!.Execute(null);
            Assert.Equal(1, closed);
            window.Close();
        });
    }

    [Fact]
    public void Footer_IsHiddenWhenEmpty()
    {
        var card = new DialogCard();
        Assert.False(card.HasFooter);
        card.Footer = new Button { Content = "Save" };
        Assert.True(card.HasFooter);
    }

    /// <summary>Not an assertion: the New reading list dialog in Default, Daylight and Matrix, written when PB_RENDER_OUT is set.</summary>
    [Fact]
    public void RenderDialogSheets()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            WithFoundation(theme, () =>
            {
                var vm = new Paperbunkr.App.ViewModels.NewReadingListViewModel(new NoPicker(), _ => { }, () => { });
                var shell = new OverlayShell { IsOpen = true, CloseCommand = new RelayCommand(() => { }), Content = new Paperbunkr.App.Views.NewReadingListOverlay { DataContext = vm } };
                var window = new Window { Width = 760, Height = 620, Content = shell, Background = (Avalonia.Media.IBrush)Application.Current!.FindResource("PbBgBrush")! };
                window.Show();
                RunLayout(window);
                double scale = double.TryParse(Environment.GetEnvironmentVariable("PB_RENDER_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out double sc) ? sc : 1;
                var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(760 * scale), (int)(620 * scale)), new Vector(96 * scale, 96 * scale));
                rtb.Render(window);
                rtb.Save(System.IO.Path.Combine(outDir, $"dialog-{theme}@{scale}.png"));
                window.Close();
            });
        }
    }

    private sealed class NoPicker : Paperbunkr.App.Services.IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
