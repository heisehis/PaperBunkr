using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Preferences Appearance tab's skin grid — key, display name, whether it's the
/// currently active skin, and a handful of the skin's real theme.json colors (docs/superpowers/
/// specs/2026-09-07-appearance-redesign-design.md) driving its mini UI-mockup preview card.
/// </summary>
public class SkinSummary
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>Preview card background — the skin's Bg color.</summary>
    public required IBrush BackgroundBrush { get; init; }

    /// <summary>Preview card's mini title-bar — the skin's Chrome color.</summary>
    public required IBrush ChromeBrush { get; init; }

    /// <summary>Preview card's title-bar dot — the skin's Accent color.</summary>
    public required IBrush AccentBrush { get; init; }

    /// <summary>Preview card's mini sidebar — the skin's Surface3 color.</summary>
    public required IBrush SurfaceBrush { get; init; }

    /// <summary>Preview card's mini content lines — the skin's TextMuted color.</summary>
    public required IBrush TextMutedBrush { get; init; }
}
