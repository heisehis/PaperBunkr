using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The theme swatches must work once the sheet's content is moved into its own window (how the reader shows it over the page): they
/// once looked for the sheet as an ancestor, which that window does not have, so a click did nothing.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderSettingsSheetThemeTests
{
    [Fact]
    public void ThemeSwatch_InTheOverlayWindow_RunsSetThemeAndRingsTheActiveTheme()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            using var styles = AppStyles();
            BookTheme? chosen = null;
            var host = new Grid();
            var settings = new BookReaderSettings { Theme = BookTheme.Sepia };
            var sheet = new ReaderSettingsSheet
            {
                Settings = settings,
                ThemeOptions = ReaderSettingsSheet.AllThemes,
                SetThemeCommand = new RelayCommand<BookTheme>(t => chosen = t),
                OverlayReference = host,
            };
            host.Children.Add(sheet);
            var window = new Window { Content = host, Width = 1366, Height = 768 };
            window.Show();
            RunLayout(window);

            sheet.IsOpen = true;
            var overlayRoot = sheet.FindNameScope()!.Find<Control>("OverlayRoot")!;
            var overlayWindow = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(overlayRoot));
            Assert.NotSame(window, overlayWindow);
            RunLayout(overlayWindow);

            Button Swatch(BookTheme t) => overlayRoot.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == t.ToString());
            static Avalonia.Media.IBrush? Ring(Button b) => ((Border)b.Content!).BorderBrush;
            Assert.NotEqual(Ring(Swatch(BookTheme.Sepia))?.ToString(), Ring(Swatch(BookTheme.Dark))?.ToString());

            var dark = Swatch(BookTheme.Dark);
            Assert.NotNull(dark.Command);
            dark.Command!.Execute(dark.CommandParameter);
            Assert.Equal(BookTheme.Dark, chosen);

            sheet.IsOpen = false;
            window.Close();
        });
    }
}
