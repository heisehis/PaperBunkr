using System.Collections.Generic;

namespace Paperbunkr.App.Models;

public sealed partial class IssueListRow : ITileCollectionSource
{
    public IReadOnlyList<CollectionBadge> CollectionBadges => CollectionBadgeStore.ForIssue(Id, SeriesId);

    public string? CollectionBadgeTooltip => CollectionBadgeText.Tooltip(CollectionBadges);
}
