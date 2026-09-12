using System;
using FluentIcons.Common;

namespace Paperbunkr.App.Models;

/// <summary>One reading act row on the Detail screen's Activity tab (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md's <c>ReadingEvent</c> log, surfaced per-series).</summary>
public sealed class ActivitySample
{
    public required string Label { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required Symbol Icon { get; init; }
}
