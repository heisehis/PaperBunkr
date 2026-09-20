using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Events;

/// <summary>What changed about a reading list's membership or order in one operation.</summary>
[Flags]
public enum ReadingListChangeKind
{
    None = 0,

    /// <summary>One or more issues were added to an existing list.</summary>
    Added = 1,

    /// <summary>One or more items were removed from a list (including an issue being deleted from the library).</summary>
    Removed = 2,

    /// <summary>The order of existing items changed.</summary>
    Reordered = 4,

    /// <summary>The list was created from an import (CBL/CSV, an external arc, a continuity) with its items.</summary>
    Imported = 8,
}

/// <summary>A completed full folder scan (comic/manga library only - Books are scanned by a separate service).</summary>
public sealed record LibraryScanCompletedEvent(
    IReadOnlyList<string> FolderPaths,
    int AddedCount,
    int UpdatedCount,
    int SeriesTouched,
    TimeSpan Duration,
    IReadOnlyList<int> AddedItemIds,
    IReadOnlyList<int> UpdatedItemIds);

/// <summary>A file crossed the Library Health confirmed-missing threshold on this Verify pass.</summary>
public sealed record MissingFileConfirmedEvent(ReadingItemType ItemType, int ItemId, string FilePath, string Title);

/// <summary>One reading-list operation's net effect. Carries ids only - the items may already be gone by the time a listener runs.</summary>
public sealed record ReadingListChangedEvent(
    int ListId,
    string ListName,
    ReadingListChangeKind Kind,
    IReadOnlyList<int> AddedIssueIds,
    IReadOnlyList<int> RemovedIssueIds);

/// <summary>
/// The app-wide feed of library-level domain events (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
/// design.md §5). Producers (the folder scanner, Library Health, <c>ReadingListManager</c>) raise
/// here and stay unaware of plugins; <c>PluginHostService</c> subscribes and dispatches the matching
/// plugin hooks. This project has no DI container and its scanners/services are constructed in many
/// places, so producers take an optional instance and default to <see cref="Default"/> (the same
/// convention as their <c>contextFactory</c> parameters) - tests pass their own instance and never
/// see each other's events.
/// <para>
/// Handlers may run on any thread and a throwing handler never reaches the producer.
/// </para>
/// </summary>
public sealed class LibraryEvents
{
    public static LibraryEvents Default { get; } = new();

    public event Action<LibraryScanCompletedEvent>? LibraryScanCompleted;

    public event Action<MissingFileConfirmedEvent>? MissingFileConfirmed;

    public event Action<ReadingListChangedEvent>? ReadingListChanged;

    public void Raise(LibraryScanCompletedEvent e) => Invoke(LibraryScanCompleted, e);

    public void Raise(MissingFileConfirmedEvent e) => Invoke(MissingFileConfirmed, e);

    public void Raise(ReadingListChangedEvent e) => Invoke(ReadingListChanged, e);

    private static void Invoke<T>(Action<T>? handlers, T payload)
    {
        if (handlers is null)
        {
            return;
        }

        // Each handler isolated: one failing subscriber must not stop the others or reach the producer.
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<T>)handler)(payload);
            }
            catch (Exception)
            {
                // Deliberately swallowed - a listener's failure is the listener's problem.
            }
        }
    }
}
