using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row of the Story Events sidebar's "Possible duplicates" (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §4):
/// a pair of events that may be the same arc (Merge / Not the same), or one event whose sources disagree (Check again).
/// </summary>
public sealed partial class StoryEventDuplicateRowViewModel : ViewModelBase
{
    private readonly Action<StoryEventDuplicateRowViewModel> _merge;
    private readonly Action<StoryEventDuplicateRowViewModel> _notTheSame;
    private readonly Action<StoryEventDuplicateRowViewModel> _checkAgain;

    public StoryEventDuplicateRowViewModel(
        StoryEventReviewItem item,
        Action<StoryEventDuplicateRowViewModel> merge,
        Action<StoryEventDuplicateRowViewModel> notTheSame,
        Action<StoryEventDuplicateRowViewModel> checkAgain)
    {
        Item = item;
        _merge = merge;
        _notTheSame = notTheSame;
        _checkAgain = checkAgain;
    }

    public StoryEventReviewItem Item { get; }

    public bool IsPair => !Item.IsConflictOnly;

    public bool IsConflictOnly => Item.IsConflictOnly;

    /// <summary>"Hulk: Planet Hulk" and "Planet Hulk", or the single event's name.</summary>
    public string Title => Item.IsConflictOnly ? Item.EventAName : $"{Item.EventAName}  ·  {Item.EventBName}";

    public string Reason => Item.Reason;

    /// <summary>"Keeps “Planet Hulk”" - which name survives a Merge.</summary>
    public string KeptLabel => Item.KeptName is { } kept ? $"Keeps “{kept}”" : string.Empty;

    public string AutomationName => Item.IsConflictOnly
        ? $"{Item.EventAName}: {Item.Reason}"
        : $"Possible duplicate: {Item.EventAName} and {Item.EventBName}. {Item.Reason}. {KeptLabel}";

    [RelayCommand]
    private void Merge() => _merge(this);

    [RelayCommand]
    private void NotTheSame() => _notTheSame(this);

    [RelayCommand]
    private void CheckAgain() => _checkAgain(this);
}
