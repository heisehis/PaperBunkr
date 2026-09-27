using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// What a comic reader profile sets (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md section 1): a bundle of overrides of the reader's global settings, stored as the
/// <c>StateJson</c> of a <see cref="Workspace"/> row of <see cref="WorkspaceScreen.Reader"/>. Every field is nullable and <see langword="null"/> means "this profile does not set it", so
/// a built-in can set just what it needs while a profile captured from the reader sets everything. Reading mode and direction are deliberately not here (a series fact), nor are zoom
/// and pan, key bindings, or the gamepad, mouse-button, memory and pre-open settings.
/// </summary>
public sealed record ReaderProfileState(
    ImageFitMode? FitMode = null,
    bool? AutoRotate = null,
    PageLayoutMode? PageLayoutMode = null,
    PageTransitionStyle? PageTransitionStyle = null,
    int? PageTransitionDurationMs = null,
    // Absolute values in the same -100..100 units as AppSettings.DefaultBrightness etc.
    double? Brightness = null,
    double? Contrast = null,
    double? Saturation = null,
    double? Gamma = null,
    ImageBackgroundMode? ImageBackgroundMode = null,
    string? BackgroundColor = null,
    string? BackgroundTexture = null,
    bool? PageMarginEnabled = null,
    double? PageMarginPercentWidth = null,
    bool? ReaderAutoHideChrome = null,
    ReaderChromeHoverMode? ReaderChromeHoverMode = null,
    TapZoneLayout? PagedTapZoneLayout = null,
    TapZoneInvert? PagedTapZoneInvert = null,
    TapZoneLayout? ContinuousTapZoneLayout = null,
    TapZoneInvert? ContinuousTapZoneInvert = null,
    // Comfort (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md): appended, so a profile stored before these existed reads them as "not set". The warm shift
    // schedule times stay global; a profile only sets whether it is on and how strong.
    bool? ShowSessionHud = null,
    bool? WarmShiftEnabled = null,
    int? WarmShiftStrength = null,
    // Panels & zoom (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md): whether the reader opens in guided panel view.
    bool? GuidedView = null,
    // Image quality (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md): auto-levels, sharpen 0-3 and auto-crop of scan borders.
    bool? AutoLevels = null,
    int? Sharpen = null,
    bool? AutoCrop = null);

/// <summary>Serialization for a reader profile's <see cref="Workspace.StateJson"/>: enums as names, unknown and missing keys tolerated, a corrupt blob yields an empty profile (sets nothing) rather than throwing.</summary>
public static class ReaderProfileStateJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ReaderProfileState state) => JsonSerializer.Serialize(state, Options);

    public static ReaderProfileState Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ReaderProfileState();
        }

        try
        {
            return JsonSerializer.Deserialize<ReaderProfileState>(json, Options) ?? new ReaderProfileState();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A value the enum converter cannot map (a member renamed in a later version) or plain garbage: sets nothing.
            return new ReaderProfileState();
        }
    }
}
