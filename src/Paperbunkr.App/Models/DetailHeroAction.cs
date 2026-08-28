using System.Windows.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// One button in a <c>DetailHero</c>'s action row (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md). Each detail-screen ViewModel supplies
/// its own ordered list via <see cref="ViewModels.IDetailHeaderSource.Actions"/> - the hero control
/// itself is content-agnostic. <see cref="IsPrimary"/> picks the accent-filled
/// <c>Button.detailAction.primary</c> style; the rest render as <c>.ghost</c>.
/// </summary>
public sealed record DetailHeroAction(string Label, ICommand Command, bool IsPrimary = false, bool IsEnabled = true);
