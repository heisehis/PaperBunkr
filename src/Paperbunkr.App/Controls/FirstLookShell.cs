using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Shared card chrome for the two "first-look" overlays - the redesigned first-run
/// <c>WelcomeOverlay</c> and the new <c>WhatsNewOverlay</c> (docs/superpowers/specs/2026-09-09-
/// startup-onboarding-whats-new-design.md, Decision 3). A brand header band (emblem + title +
/// subtitle, echoing the splash), the default <see cref="ContentControl.Content"/> as the body,
/// and a <see cref="Footer"/> slot.
///
/// Code-only <see cref="TemplatedControl"/> (the <c>OverlayShell</c>/<c>BrandMark</c> pattern -
/// template is an implicit <c>ControlTheme</c> in <c>Styles/FirstLook.axaml</c>), deriving from
/// <see cref="ContentControl"/> so it can host arbitrary body content.
/// </summary>
public class FirstLookShell : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<FirstLookShell, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<FirstLookShell, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<FirstLookShell, object?>(nameof(Footer));

    /// <summary>Header title, e.g. "Welcome to Paperbunkr".</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One-line header subtitle under the title. Hidden when null/empty.</summary>
    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Footer content - typically a link on the left and a primary button on the right.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
