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
/// left/right half or pressing Left/Right invokes <see cref="PreviousPageCommand"/>/
/// <see cref="NextPageCommand"/> - bound from XAML like every other command in this codebase,
/// rather than a code-behind event the ViewModel would need to subscribe to.
/// </summary>
public class PageCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, Bitmap?>(nameof(Page));

    public static readonly StyledProperty<ICommand?> PreviousPageCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(PreviousPageCommand));

    public static readonly StyledProperty<ICommand?> NextPageCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(NextPageCommand));

    static PageCanvas()
    {
        AffectsRender<PageCanvas>(PageProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public Bitmap? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public ICommand? PreviousPageCommand
    {
        get => GetValue(PreviousPageCommandProperty);
        set => SetValue(PreviousPageCommandProperty, value);
    }

    public ICommand? NextPageCommand
    {
        get => GetValue(NextPageCommandProperty);
        set => SetValue(NextPageCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new ReaderPageDrawOperation(new Rect(Bounds.Size), Page));
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
        if (e.Key == Key.Left && TryExecute(PreviousPageCommand))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Right && TryExecute(NextPageCommand))
        {
            e.Handled = true;
        }
    }

    private void InvokeZoneCommand(double x)
    {
        var command = x < Bounds.Width / 2 ? PreviousPageCommand : NextPageCommand;
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
