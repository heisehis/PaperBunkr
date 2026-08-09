using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Reader screen's page canvas (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md
/// §4/§6). Renders <see cref="Page"/> via <see cref="ReaderPageDrawOperation"/>; clicking the
/// left/right half or pressing Left/Right invokes <see cref="LeftCommand"/>/<see cref="RightCommand"/>
/// - bound from XAML like every other command in this codebase, rather than a code-behind event
/// the ViewModel would need to subscribe to. Named spatially, not semantically ("Previous"/"Next"),
/// per docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §3 - which physical side
/// means "forward" depends on reading direction, and PageCanvas itself has no opinion on that.
/// </summary>
public class PageCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, Bitmap?>(nameof(Page));

    public static readonly StyledProperty<ICommand?> LeftCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(LeftCommand));

    public static readonly StyledProperty<ICommand?> RightCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(RightCommand));

    public static readonly StyledProperty<bool> HighQualityDisplayProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(HighQualityDisplay), defaultValue: true);

    static PageCanvas()
    {
        AffectsRender<PageCanvas>(PageProperty);
        AffectsRender<PageCanvas>(HighQualityDisplayProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public Bitmap? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public ICommand? LeftCommand
    {
        get => GetValue(LeftCommandProperty);
        set => SetValue(LeftCommandProperty, value);
    }

    public ICommand? RightCommand
    {
        get => GetValue(RightCommandProperty);
        set => SetValue(RightCommandProperty, value);
    }

    public bool HighQualityDisplay
    {
        get => GetValue(HighQualityDisplayProperty);
        set => SetValue(HighQualityDisplayProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new ReaderPageDrawOperation(new Rect(Bounds.Size), Page, HighQualityDisplay));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        InvokeZoneCommand(e.GetPosition(this).X);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Left && TryExecute(LeftCommand))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Right && TryExecute(RightCommand))
        {
            e.Handled = true;
        }
    }

    private void InvokeZoneCommand(double x)
    {
        var command = x < Bounds.Width / 2 ? LeftCommand : RightCommand;
        TryExecute(command);
    }

    private static bool TryExecute(ICommand? command)
    {
        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
