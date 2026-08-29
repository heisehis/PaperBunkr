using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Bebas display / heading text with a ~1px RGB mis-registration ("misprinted comic") treatment -
/// three stacked copies of the same string: a red layer nudged one way, a cyan layer the other,
/// the real cream text on top. Drives the Home masthead + section headings and the shared
/// <c>DetailHero</c> title (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §5).
///
/// Code-only <see cref="TemplatedControl"/> - the visual template lives in
/// <c>Styles/Typography.axaml</c> as an implicit <c>ControlTheme</c>, so this is not a new
/// <c>x:Class</c> View and doesn't hit the AVLN2000 new-view build gotcha (see CLAUDE.md).
/// <c>FontFamily</c> / <c>FontSize</c> / <c>FontWeight</c> / <c>Foreground</c> come from
/// <see cref="TemplatedControl"/> already, so a consumer sets them the same way it would on a
/// <c>TextBlock</c> (directly, or via a <c>Classes=</c> style).
/// </summary>
public class SplitText : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SplitText, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The two colour clones surface to UIA as extra "Text" peers; keep only the real string
        // on the control itself so a screen reader announces the heading once.
        if (change.Property == TextProperty)
        {
            AutomationProperties.SetName(this, Text ?? string.Empty);
        }
    }
}
