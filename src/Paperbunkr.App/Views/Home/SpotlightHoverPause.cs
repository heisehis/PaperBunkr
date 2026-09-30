using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.ViewModels.Home;

namespace Paperbunkr.App.Views.Home;

/// <summary>
/// Hovering the spotlight hero or its dots pauses rotation (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §5).
/// An attached behavior because the hero now lives in a code-behind-less section template (<c>SpotlightSection.axaml</c>).
/// </summary>
public static class SpotlightHoverPause
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(SpotlightHoverPause));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    static SpotlightHoverPause()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.PointerEntered -= OnEntered;
            control.PointerExited -= OnExited;
            if (e.GetNewValue<bool>())
            {
                control.PointerEntered += OnEntered;
                control.PointerExited += OnExited;
            }
        });
    }

    private static void OnEntered(object? sender, PointerEventArgs e)
        => ((sender as Control)?.DataContext as SpotlightSectionViewModel)?.Home.PauseSpotlightRotation();

    private static void OnExited(object? sender, PointerEventArgs e)
        => ((sender as Control)?.DataContext as SpotlightSectionViewModel)?.Home.ResumeSpotlightRotation();
}
