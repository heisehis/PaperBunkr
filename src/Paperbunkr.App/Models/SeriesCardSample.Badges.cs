using System.Collections.Generic;

namespace Paperbunkr.App.Models;

public sealed partial class SeriesCardSample : ITileCollectionSource
{
    public IReadOnlyList<CollectionBadge> CollectionBadges => CollectionBadgeStore.ForSeries(SeriesId);

    public string? CollectionBadgeTooltip => CollectionBadgeText.Tooltip(CollectionBadges);
}
