namespace Paperbunkr.App.Models;

/// <summary>
/// A click-through target a finished job (or an alert) can carry
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). <see cref="Payload"/> is opaque
/// to everything except <c>MainViewModel.ResolveActivityLink</c>, which switches on
/// <see cref="Kind"/>.
/// </summary>
/// <param name="Kind">Which destination.</param>
/// <param name="Payload">Destination-specific argument (a series id, a filter blob, a prefs tab key, …). May be empty.</param>
public sealed record ActivityLink(ActivityLinkKind Kind, string Payload = "");
