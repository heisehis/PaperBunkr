using System.Collections.Specialized;
using System.ComponentModel;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

public class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_RaisesExactlyOneReset()
    {
        var collection = new BulkObservableCollection<int>(new[] { 1, 2, 3 });
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.ReplaceAll(new[] { 10, 20, 30, 40 });

        Assert.Equal(new[] { 10, 20, 30, 40 }, collection);
        var only = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, only.Action);
    }

    [Fact]
    public void ReplaceAll_RaisesCountAndIndexerPropertyChanges()
    {
        var collection = new BulkObservableCollection<int>();
        var names = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        collection.ReplaceAll(new[] { 1 });

        Assert.Contains("Count", names);
        Assert.Contains("Item[]", names);
    }

    [Fact]
    public void ReplaceAll_WithEmpty_ClearsAndStillRaisesReset()
    {
        var collection = new BulkObservableCollection<int>(new[] { 1, 2 });
        int resets = 0;
        collection.CollectionChanged += (_, e) => resets += e.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

        collection.ReplaceAll(Array.Empty<int>());

        Assert.Empty(collection);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void ReplaceAll_EmptyOverEmpty_RaisesNothing()
    {
        var collection = new BulkObservableCollection<int>();
        int events = 0;
        collection.CollectionChanged += (_, _) => events++;

        collection.ReplaceAll(Array.Empty<int>());

        Assert.Equal(0, events);
    }

    [Fact]
    public void ReplaceAll_WithItself_KeepsContent()
    {
        var collection = new BulkObservableCollection<int>(new[] { 1, 2, 3 });

        collection.ReplaceAll(collection);

        Assert.Equal(new[] { 1, 2, 3 }, collection);
    }

    [Fact]
    public void ReplaceAll_WithLazyQueryOverItself_MaterializesFirst()
    {
        var collection = new BulkObservableCollection<int>(new[] { 1, 2, 3 });

        collection.ReplaceAll(collection.Where(x => x > 1));

        Assert.Equal(new[] { 2, 3 }, collection);
    }

    [Fact]
    public void ReplaceAll_Null_Throws()
    {
        var collection = new BulkObservableCollection<int>();

        Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));
    }
}
