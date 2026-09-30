using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Controls;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Left/Right across the Spotlight carousel as Home really builds it: the AccordionPanel is an ItemsControl's panel, so its
/// children are item containers that cannot take focus - the buttons inside them must.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class AccordionPanelKeyboardTests
{
    [Fact]
    public void LeftRight_InsideAnItemsControl_MoveFocusBetweenPanelsAndOpenThem()
    {
        WithThemeAndTokens(() =>
        {
            var activated = new List<object?>();
            var items = new ItemsControl
            {
                ItemsSource = new[] { "a", "b", "c" },
                ItemsPanel = new FuncTemplate<Panel?>(() => new AccordionPanel
                {
                    ActivateCommand = new RelayCommand<object?>(activated.Add),
                }),
                ItemTemplate = new FuncDataTemplate<string>((item, _) => new Button { Content = item }),
            };
            var window = new Window { Content = items, Width = 900, Height = 320 };
            window.Show();
            RunLayout(window);

            var buttons = items.ItemsPanelRoot!.Children.Select(c => Assert.IsType<Button>(((ContentPresenter)c).Child)).ToList();
            buttons[0].Focus(NavigationMethod.Tab);
            RunLayout(window);

            Press(window, Key.Right);
            Assert.Same(buttons[1], Focused(window));

            Press(window, Key.Right);
            Assert.Same(buttons[2], Focused(window));

            Press(window, Key.Right);
            Assert.Same(buttons[0], Focused(window));

            Press(window, Key.Left);
            Assert.Same(buttons[2], Focused(window));

            Assert.Contains("b", activated);
            Assert.Contains("c", activated);
            window.Close();
        });
    }
}
