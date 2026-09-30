using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The hero band shared by the Continuity screen's continuity and event pages (docs/superpowers/specs/2026-09-28-continuity-screen-
/// redesign-design.md, "Hero band"): a dimmed, blurred collage of up to eight covers (<see cref="CoverKeys"/>, loaded lazily through
/// <c>AsyncCoverImage</c>) fading into the page on the left through the skin's <c>PbHeroGradientStart/EndColor</c>, with the page's own
/// title, stats, chips and buttons as <see cref="ContentControl.Content"/>. With no covers it's a plain surface. Code-only; its template
/// is the ControlTheme in <c>Styles/ContinuityChrome.axaml</c>.
/// </summary>
public class HeroBand : ContentControl
{
    public static readonly StyledProperty<IEnumerable?> CoverKeysProperty =
        AvaloniaProperty.Register<HeroBand, IEnumerable?>(nameof(CoverKeys));

    /// <summary>Cover cache keys (<c>CoverFingerprint</c> stems) for the collage.</summary>
    public IEnumerable? CoverKeys
    {
        get => GetValue(CoverKeysProperty);
        set => SetValue(CoverKeysProperty, value);
    }
}
