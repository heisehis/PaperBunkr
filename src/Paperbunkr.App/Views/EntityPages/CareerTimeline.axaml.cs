using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views.EntityPages;

public partial class CareerTimeline : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<CareerTimeline, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<string?> AxisStartProperty =
        AvaloniaProperty.Register<CareerTimeline, string?>(nameof(AxisStart));

    public static readonly StyledProperty<string?> AxisEndProperty =
        AvaloniaProperty.Register<CareerTimeline, string?>(nameof(AxisEnd));

    public CareerTimeline() => InitializeComponent();

    /// <summary><c>CareerRunItem</c>s.</summary>
    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? AxisStart
    {
        get => GetValue(AxisStartProperty);
        set => SetValue(AxisStartProperty, value);
    }

    public string? AxisEnd
    {
        get => GetValue(AxisEndProperty);
        set => SetValue(AxisEndProperty, value);
    }

    /// <summary>Closes the run's flyout after its command ran - one dispatcher tick later, so the popup isn't torn down while the Click that
    /// started it is still routing through its content (CLAUDE.md runtime gotcha).</summary>
    private void OnFlyoutAction(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        foreach (var bar in this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("careerBar")).ToList()) bar.Flyout?.Hide();
    });
}
