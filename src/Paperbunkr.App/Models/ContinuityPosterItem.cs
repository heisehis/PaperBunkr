using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One poster on a continuity's Overview (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Series"): a member series
/// (<see cref="Card"/>, with its read-progress ring and hover actions) or a dimmed placeholder for a continuation that isn't in the library,
/// which opens its comics.org page. <see cref="ArrowBefore"/> draws the → joining it to the poster before it in a run.
/// </summary>
public sealed class ContinuityPosterItem
{
    public SeriesCardSample? Card { get; init; }

    public required string Name { get; init; }

    public required string Years { get; init; }

    public bool IsPlaceholder { get; init; }

    public string? Url { get; init; }

    public bool ArrowBefore { get; init; }

    public bool IsSeries => Card is not null;

    public bool HasYears => !string.IsNullOrEmpty(Years);

    public IBrush CoverBrush => Card?.CoverBrush ?? SeriesCardSample.CoverBrushFor(Name);

    public string AutomationName => IsPlaceholder ? $"{Name}, not in your library, open on comics.org" : Name;
}

/// <summary>A row of posters joined by arrows: one run of continued series.</summary>
public sealed class ContinuityRunRow
{
    public required string Label { get; init; }

    public required System.Collections.Generic.IReadOnlyList<ContinuityPosterItem> Posters { get; init; }
}
