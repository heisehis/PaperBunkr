using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One reading mode as people see it: its name, a one-line explanation, and a short tag for badges.</summary>
public sealed record ReadingModeInfo(ReadingMode Mode, string Label, string Description, string Badge);

/// <summary>
/// The single list of reading modes and their names (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §14). The reader's mode palette and label, the Detail
/// screen's picker, the manga header badge and Edit Series all read this, so a mode can never be called one thing in the reader and another on the series.
/// Order is the order the reader's palette offers them.
/// </summary>
public static class ReadingModeCatalog
{
    public static readonly IReadOnlyList<ReadingModeInfo> All = new ReadingModeInfo[]
    {
        new(ReadingMode.LeftToRight, "Left to Right", "Pages turn left to right, like a Western comic.", "LTR"),
        new(ReadingMode.RightToLeft, "Right to Left", "Pages turn right to left, like Japanese manga.", "RTL"),
        new(ReadingMode.TopToBottom, "Top to Bottom", "One page at a time, turning downwards.", "Vertical"),
        new(ReadingMode.Webtoon, "Long Strip", "Pages joined edge to edge in one vertical scroll, for webtoons and manhwa.", "Webtoon"),
        new(ReadingMode.VerticalContinuous, "Longstrip (gapped)", "A vertical scroll with a small gap between pages.", "Vertical"),
        new(ReadingMode.HorizontalContinuous, "Horizontal Long Strip", "Pages side by side in one horizontal scroll, left to right.", "Strip"),
        new(ReadingMode.HorizontalContinuousRightToLeft, "Horizontal Long Strip (RTL)", "Pages side by side in one horizontal scroll, right to left.", "RTL strip"),
    };

    public static string[] Labels { get; } = All.Select(m => m.Label).ToArray();

    public static ReadingModeInfo InfoFor(ReadingMode mode) => All.FirstOrDefault(m => m.Mode == mode) ?? All[0];

    public static string LabelFor(ReadingMode mode) => InfoFor(mode).Label;

    public static string BadgeFor(ReadingMode mode) => InfoFor(mode).Badge;

    public static string DescriptionFor(ReadingMode mode) => InfoFor(mode).Description;

    /// <summary>The mode with this label (as typed or picked), or null for text that is not one of them.</summary>
    public static ReadingMode? FromLabel(string? label) =>
        All.FirstOrDefault(m => string.Equals(m.Label, label?.Trim(), StringComparison.OrdinalIgnoreCase))?.Mode;

    /// <summary>Whether the file's manga flag should say right-to-left for this mode (what ComicInfo can record; the long-strip modes cannot be written to a file).</summary>
    public static bool IsRightToLeft(ReadingMode mode) => mode is ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft;
}
