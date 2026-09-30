using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>Home screen's "Because you read {SeedSeriesName}" row (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md Module 3) - one per recently-opened seed series that produced at
/// least one <c>RecommendationResolver</c> candidate; seeds with zero candidates never get a row.</summary>
public sealed class BecauseYouReadRow
{
    public required string SeedSeriesName { get; init; }
    public required ObservableCollection<SeriesCardSample> Cards { get; init; }

    /// <summary>The series the row is seeded from, shown as the row's outlined lead card (docs/superpowers/specs/
    /// 2026-09-28-home-cosmetics-design.md C8). Null only in hand-built test rows.</summary>
    public SeriesCardSample? SeedSeries { get; init; }
}
