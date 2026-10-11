namespace Paperbunkr.Data.Entities;

/// <summary>What on the user's Metron account a <see cref="MetronSyncLink"/> row stands for.</summary>
public enum MetronSyncKind
{
    /// <summary>A followed series on the pull list. <see cref="MetronSyncLink.LocalId"/> is a <see cref="WatchedSeries"/> id, <see cref="MetronSyncLink.RemoteId"/> the Metron series id.</summary>
    PullListSeries = 0,

    /// <summary>A library issue in the collection. Local: <see cref="Issue"/> id; remote: the collection item id (not the issue id), which is what rating updates address.</summary>
    CollectionItem = 1,

    /// <summary>A want on the wish list. Local: <see cref="WantedIssue"/> id; remote: the wish-list item id.</summary>
    WishListItem = 2,

    /// <summary>
    /// A finished read this app recorded because the account already had it ("Import from Metron"). Local: the <see cref="ReadingEvent"/> id. Reading sync
    /// skips it once and drops the row, so a read that came from Metron is never sent back as a second one.
    /// </summary>
    ImportedRead = 3,
}

public enum MetronSyncState
{
    Synced = 0,

    /// <summary>It was on Metron and the user took it off there. Kept so it isn't sent again; the local row is left alone.</summary>
    RemovedRemotely = 1,

    /// <summary>A wish-list item whose issue arrived, marked acquired on Metron.</summary>
    Acquired = 2,

    /// <summary>Metron refused it (it has no such issue). Kept so the same request isn't made again every run.</summary>
    Rejected = 3,
}

/// <summary>
/// The ledger of what has been sent to the user's Metron account (docs/superpowers/specs/2026-10-05-metron-
/// account-sync-design.md). Account sync is a reconciliation: each run compares what the library says should
/// be on Metron with these rows and makes the calls that close the gap, so an interrupted or rate-limited
/// run loses nothing - whatever wasn't sent is still a gap next time. Not a foreign key to any of the three
/// local tables on purpose: a row has to outlive its local record long enough for the removal to be sent.
/// </summary>
public class MetronSyncLink
{
    public int Id { get; set; }

    public MetronSyncKind Kind { get; set; }

    public int LocalId { get; set; }

    public int RemoteId { get; set; }

    public MetronSyncState State { get; set; }

    /// <summary>Collection items only: the rating last sent (1-5), so a change - and only a change - is sent again.</summary>
    public int? PushedRating { get; set; }

    public DateTime SyncedAt { get; set; }
}
