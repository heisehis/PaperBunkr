using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// <see cref="ObservableCollection{T}"/> that can swap its whole content with a single
/// <see cref="NotifyCollectionChangedAction.Reset"/> notification (docs/superpowers/specs/
/// 2026-09-19-library-search-perf-design.md §5). <c>Clear()</c> followed by N <c>Add()</c> calls
/// raises N+1 notifications, and <c>VirtualizingWrapPanel.OnItemsChanged</c> re-realizes every
/// container on each one - a Library search over a few thousand cards paid that once per card.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection()
    {
    }

    public BulkObservableCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    /// <summary>Replaces the whole content with <paramref name="items"/> and raises exactly one
    /// <c>Reset</c> (plus <c>Count</c>/<c>Item[]</c> property changes). Safe to pass this collection
    /// itself or a lazily-evaluated query over it - the source is materialized first.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        IReadOnlyList<T> source = items is IReadOnlyList<T> list && !ReferenceEquals(items, this)
            ? list
            : items.ToList();

        bool wasEmpty = Items.Count == 0;
        if (wasEmpty && source.Count == 0)
        {
            return;
        }

        Items.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            Items.Add(source[i]);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
