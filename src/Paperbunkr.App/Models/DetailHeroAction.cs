using System.Windows.Input;
using FluentIcons.Common;

namespace Paperbunkr.App.Models;

/// <summary>
/// One button in a <c>DetailHero</c>'s action row (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md). Each detail-screen ViewModel supplies
/// its own ordered list via <see cref="ViewModels.IDetailHeaderSource.Actions"/> - the hero control
/// itself is content-agnostic. <see cref="IsPrimary"/> picks the accent-filled
/// <c>Button.detailAction.primary</c> style; the rest render as <c>.ghost</c>.
/// <para><see cref="Icon"/> (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
/// design.md §2) is an optional leading FluentIcons glyph; <see langword="null"/> renders text
/// only, so callers that don't want an icon (Book detail) are unaffected.</para>
/// </summary>
public sealed record DetailHeroAction(string Label, ICommand? Command, bool IsPrimary = false, bool IsEnabled = true, Symbol? Icon = null, object? FlyoutContext = null)
{
    /// <summary>True when this action has a leading glyph - drives the icon's <c>IsVisible</c>.</summary>
    public bool HasIcon => Icon is not null;

    /// <summary>Non-nullable projection for the <c>fi:SymbolIcon.Symbol</c> compiled binding
    /// (which can't take a <see cref="Symbol"/>?); only rendered when <see cref="HasIcon"/>.</summary>
    public Symbol IconGlyph => Icon ?? Symbol.Circle;

    /// <summary>
    /// When set, this action renders as a <c>Button.Flyout</c>-hosting button (docs/superpowers/
    /// specs/2026-09-17-reader-save-page-and-cover-picker-design.md's cover picker is the first
    /// consumer) instead of a plain <see cref="Command"/> button - <see cref="Command"/> is unused
    /// in that case, since clicking a button with only a <c>Flyout</c> set opens it directly.
    /// </summary>
    public bool HasFlyout => FlyoutContext is not null;
}
