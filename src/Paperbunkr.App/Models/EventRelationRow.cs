using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One story event's row on the Related tab (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §6.2): the main series
/// first ("Main event"), then the tie-in series with their issue ranges. The current series' own card is left out.
/// </summary>
public sealed class EventRelationRow(int storyEventId, string title, string contextLabel)
{
    public int StoryEventId { get; } = storyEventId;

    public string Title { get; } = title;

    /// <summary>"12 series · open event" - clicking it opens the event page.</summary>
    public string ContextLabel { get; } = contextLabel;

    public ObservableCollection<PosterRailItem> Items { get; } = new();
}
