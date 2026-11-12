using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

/// <summary>An item in the List view's flat list: a section header or an issue row.</summary>
public abstract class EventListItem : ObservableObject
{
    public abstract int SectionKey { get; }
}

/// <summary>A List section header (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §1).</summary>
public sealed partial class EventListHeaderItem : EventListItem
{
    public EventListHeaderItem(EventListSection section, int read, bool expanded, IRelayCommand<EventListHeaderItem> toggle,
        IRelayCommand<EventListHeaderItem> readSection)
    {
        Section = section;
        _read = read;
        _isExpanded = expanded;
        ToggleCommand = toggle;
        ReadSectionCommand = readSection;
    }

    public EventListSection Section { get; }
    public override int SectionKey => Section.Key;
    public string Label => Section.Label;
    public int Count => Section.Indices.Count;
    public string CountLabel => $"{Read}/{Count}";
    public bool IsDone => Count > 0 && Read == Count;
    public bool HasUnread => Read < Count;
    public IRelayCommand<EventListHeaderItem> ToggleCommand { get; }
    public IRelayCommand<EventListHeaderItem> ReadSectionCommand { get; }
    public string AutomationName => $"{Label}, {Read} of {Count} read, {(IsExpanded ? "expanded" : "collapsed")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(IsDone), nameof(HasUnread), nameof(AutomationName))]
    private int _read;
}

/// <summary>An issue row in the List view; shares its <see cref="EventMapCardViewModel"/> with the map, so read state stays live.</summary>
public sealed partial class EventListIssueItem : EventListItem
{
    public EventListIssueItem(EventMapCardViewModel card, int sectionKey, string? tiesInto, bool isNextUp)
    {
        Card = card;
        SectionKey = sectionKey;
        TiesInto = tiesInto;
        _isNextUp = isNextUp;
    }

    public EventMapCardViewModel Card { get; }
    public override int SectionKey { get; }
    public string? TiesInto { get; }
    public bool HasTiesInto => TiesInto is not null;

    /// <summary>The first unread issue: drawn with the "Next up" chip (M6b §1).</summary>
    [ObservableProperty]
    private bool _isNextUp;
}

/// <summary>One jump-bar row: a section with its progress.</summary>
public sealed partial class EventListJumpItem : ObservableObject
{
    public EventListJumpItem(int key, string label, int read, int count)
    {
        Key = key;
        Label = label;
        Count = count;
        _read = read;
    }

    public int Key { get; }
    public string Label { get; }
    public int Count { get; }
    public string CountLabel => $"{Read}/{Count}";
    public bool IsDone => Count > 0 && Read == Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(IsDone))]
    private int _read;

    [ObservableProperty]
    private bool _isCurrent;
}

public sealed partial class EventMapViewModel
{
    private readonly HashSet<int> _collapsedListSections = new();
    private EventListModel _listModel = EventListModel.Empty;

    public ObservableCollection<EventListItem> ListItems { get; } = new();

    public ObservableCollection<EventListJumpItem> JumpItems { get; } = new();

    public bool IsListMode => MapMode == EventMapMode.List;

    /// <summary>Asks the view to bring a section header to the top of the list.</summary>
    public event Action<int>? ScrollToListSectionRequested;

    [RelayCommand]
    private void ShowListMode() => SetMapMode(EventMapMode.List);

    [RelayCommand]
    private void JumpToSection(EventListJumpItem? item)
    {
        if (item is null)
        {
            return;
        }

        // A collapsed section opens when you jump to it, so the jump lands on something.
        if (_collapsedListSections.Remove(item.Key))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshList();
                ScrollToListSectionRequested?.Invoke(item.Key);
            });
            return;
        }

        ScrollToListSectionRequested?.Invoke(item.Key);
    }

    [RelayCommand]
    private void ToggleListSection(EventListHeaderItem? header)
    {
        if (header is null)
        {
            return;
        }

        bool expand = !header.IsExpanded;
        if (expand)
        {
            _collapsedListSections.Remove(header.SectionKey);
        }
        else
        {
            _collapsedListSections.Add(header.SectionKey);
        }

        // The header's own button raised this; rebuilding the list now would detach it mid-route (CLAUDE.md), so defer a tick.
        Dispatcher.UIThread.Post(RefreshList);
    }

    [RelayCommand]
    private void ReadListSection(EventListHeaderItem? header)
    {
        if (header is null || Cards.Count == 0)
        {
            return;
        }

        var available = header.Section.Indices.Where(i => i < Cards.Count).ToList();
        if (available.Count == 0)
        {
            return;
        }

        int index = available.FirstOrDefault(i => !Cards[i].IsRead, available[0]);
        Select(index, reveal: false);
        OpenReaderCommand.Execute(null);
    }

    /// <summary>Marks the jump-bar row for the section at the top of the list (the pinned header's section).</summary>
    public void SetCurrentListSection(int? key)
    {
        foreach (var item in JumpItems)
        {
            item.IsCurrent = item.Key == key;
        }
    }

    /// <summary>The section a list item belongs to, for the pinned header.</summary>
    public EventListHeaderItem? HeaderFor(int sectionKey) =>
        ListItems.OfType<EventListHeaderItem>().FirstOrDefault(h => h.SectionKey == sectionKey);

    private void ResetListState()
    {
        _collapsedListSections.Clear();
        _listSignature = string.Empty;
    }

    private string _listSignature = string.Empty;

    private void RefreshList()
    {
        _listModel = EventListBuilder.Build(Layout);
        int? nextUp = Layout.Cells.OrderBy(c => c.Index).FirstOrDefault(c => c.Index < Cards.Count && !Cards[c.Index].IsRead)?.Index;

        // Same sections, same rows, same collapsed set: only the counts and the "Next up" marker can have moved, so update those in place
        // and keep the list's selection and scroll position (a Mark read must not rebuild the list under the user).
        string signature = string.Join(";", _listModel.Sections.Select(s =>
            $"{s.Key}:{s.Label}:{(_collapsedListSections.Contains(s.Key) ? "c" : string.Join(",", s.Indices.Select(i => i < Cards.Count ? Cards[i].IssueId : -1)))}"));
        if (signature == _listSignature && ListItems.Count > 0)
        {
            foreach (var header in ListItems.OfType<EventListHeaderItem>())
            {
                header.Read = header.Section.Indices.Count(i => i < Cards.Count && Cards[i].IsRead);
            }

            foreach (var jump in JumpItems)
            {
                jump.Read = _listModel.Sections.FirstOrDefault(s => s.Key == jump.Key)?.Indices.Count(i => i < Cards.Count && Cards[i].IsRead) ?? 0;
            }

            foreach (var row in ListItems.OfType<EventListIssueItem>())
            {
                row.IsNextUp = row.Card.Index == nextUp;
            }

            return;
        }

        _listSignature = signature;
        var items = new List<EventListItem>();
        var jumps = new List<EventListJumpItem>();
        foreach (var section in _listModel.Sections)
        {
            int read = section.Indices.Count(i => i < Cards.Count && Cards[i].IsRead);
            bool expanded = !_collapsedListSections.Contains(section.Key);
            items.Add(new EventListHeaderItem(section, read, expanded, ToggleListSectionCommand, ReadListSectionCommand));
            jumps.Add(new EventListJumpItem(section.Key, section.Label, read, section.Indices.Count));
            if (!expanded)
            {
                continue;
            }

            foreach (int index in section.Indices)
            {
                if (index < Cards.Count)
                {
                    items.Add(new EventListIssueItem(Cards[index], section.Key, EventListBuilder.TiesInto(Layout, index), index == nextUp));
                }
            }
        }

        ListItems.Clear();
        foreach (var item in items)
        {
            ListItems.Add(item);
        }

        JumpItems.Clear();
        foreach (var jump in jumps)
        {
            JumpItems.Add(jump);
        }

        ListRebuilt?.Invoke();
    }

    /// <summary>Raised after the list's items were replaced, so the view can restore its selection.</summary>
    public event Action? ListRebuilt;
}
