using FluentIcons.Common;

namespace Paperbunkr.App.Models;

/// <summary>
/// The one colour + icon language for a series' publication status (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-2-design.md #10) - used identically by the Detail hero, Library rows, and anywhere else a status shows.
/// Brush keys are theme resources (never hex), so it follows the active skin. <c>Unknown</c> maps to <see langword="null"/>:
/// no chip. Input is the raw enum name the row models already carry (<c>SeriesStatusLabel</c>, e.g. "Ongoing").
/// </summary>
public sealed record SeriesStatusStyle(string Label, Symbol Icon, string ForegroundKey, string BackgroundKey)
{
    public static SeriesStatusStyle? For(string? status) => status switch
    {
        "Ongoing" => new("Ongoing", Symbol.PlayCircle, "PbSuccessBrush", "PbSuccessSoftBrush"),
        "Completed" => new("Completed", Symbol.CheckmarkCircle, "PbAccentTextBrush", "PbAccentSoftBrush"),
        "Hiatus" => new("Hiatus", Symbol.PauseCircle, "PbBadgeBrush", "PbBadgeSoftBrush"),
        "Cancelled" => new("Cancelled", Symbol.DismissCircle, "PbDangerBrush", "PbDangerSoftBrush"),
        _ => null,
    };
}
