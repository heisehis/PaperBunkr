namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which tap/click zone layout the comic reader uses in one mode (paged or continuous), backing <see cref="AppSettings.PagedTapZoneLayout"/> /
/// <see cref="AppSettings.ContinuousTapZoneLayout"/> (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 4). The named layouts follow Mihon's
/// navigation layouts (mihonapp/mihon <c>ui/reader/viewer/navigation</c>); <see cref="Default"/> is Paperbunkr's own long-standing behaviour. Not a ComicRack CE
/// setting: CE has fixed hot spots (<c>GestureHitTest</c>) mapped to remappable commands, not layouts.
/// </summary>
public enum TapZoneLayout
{
    /// <summary>Today's behaviour: touch = three columns (left third back, right third forward, middle third toggles the chrome); mouse = two halves.</summary>
    Default,

    /// <summary>Mihon "L-shaped": left-middle and top strips go back, right-middle and bottom strips go forward.</summary>
    LShaped,

    /// <summary>Mihon "Kindle-like": the left third goes back, everything else goes forward.</summary>
    Kindlish,

    /// <summary>Mihon "Edge": left and right thirds go forward, the bottom-middle goes back.</summary>
    Edge,

    /// <summary>Mihon "Right and left": left and right thirds turn the page in that spatial direction, whichever way the book reads.</summary>
    RightAndLeft,

    /// <summary>No zones at all: clicks and taps do nothing (touch still drags, keys and the wheel still work).</summary>
    Disabled,
}

/// <summary>Mirrors a <see cref="TapZoneLayout"/> for left-handed or otherwise inverted use, on top of the automatic right-to-left mirroring (Mihon's <c>TappingInvertMode</c>).</summary>
public enum TapZoneInvert
{
    None,
    Horizontal,
    Vertical,
    Both,
}
