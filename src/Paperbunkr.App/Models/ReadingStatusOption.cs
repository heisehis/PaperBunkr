using FluentIcons.Common;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One row in the reading-status setter flyout (docs/superpowers/specs/2026-09-04-detail-
/// screen-icons-and-glyphs-design.md Part 2 §C). Glyph + label come from
/// <see cref="ReadingStatusPresentation"/> so a status reads the same in the chip and the menu.</summary>
public sealed record ReadingStatusOption(ReadingStatus Value, string Label, Symbol Glyph, bool IsChecked);
