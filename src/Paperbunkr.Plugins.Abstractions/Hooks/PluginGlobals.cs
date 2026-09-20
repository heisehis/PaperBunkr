using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.Plugins.Hooks;

/// <summary>
/// Base globals type every hook's script compiles against via Roslyn scripting's globals-object
/// mechanism (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §3). Each hook below gets
/// its own small derived type carrying a strongly-typed payload instead of CE's shared
/// <c>object[] data</c>, so a script gets compile-time errors on the exact seam CE plugin authors
/// used to get wrong (bad index, bad cast).
/// </summary>
public abstract class PluginGlobals
{
    public required IPluginEnvironment Environment { get; init; }

    /// <summary>
    /// Tripped when the host gives up on this invocation - today the domain-event hooks
    /// (<see cref="PluginHooks.BookRead"/> and friends) cancel it after a 30 s timeout. Cooperative:
    /// a plugin that never looks at it, or blocks synchronously, keeps running, so long-running
    /// plugin work should pass it to anything cancellable and check it in loops. Defaults to
    /// <see cref="System.Threading.CancellationToken.None"/> for every hook that has no timeout, and is
    /// deliberately not <c>required</c> so existing globals construction is unchanged.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>Shared by <see cref="PluginHooks.Library"/> and <see cref="PluginHooks.Editor"/> - both
/// are the same "operate on selected comics" command family, just surfaced at different sites
/// (Library grid right-click vs. the Issue Properties/Bulk Editing overlay toolbar).</summary>
public sealed class BooksHookGlobals : PluginGlobals
{
    public required IReadOnlyList<Issue> Books { get; init; }
}

/// <summary>
/// <see cref="PluginHooks.Books"/>'s own payload - deliberately *not* <see cref="BooksHookGlobals"/>
/// despite the shared name. CE's own <c>ScriptTypeBooks</c> is declared but never actually wired to
/// any real UI anchor anywhere in CE's source (verified: `grep -r ScriptTypeBooks` across
/// ComicRackCE only turns up the constant declaration and its manifest-validation list entry), so
/// there's no CE precedent for what it operates on. The 2026-08-24 v2 spec's choice to anchor it on
/// Paperbunkr's own Books screen (novels/EPUB/PDF - a Paperbunkr-only concept with no CE
/// equivalent) is a reasonable adaptation, but that screen's entities are <see cref="Book"/>, not
/// <see cref="Issue"/> - a completely separate schema with no shared columns or FK crossing (see
/// <see cref="Book"/>'s own doc comment). Reusing <c>BooksHookGlobals</c>'s <c>Issue</c>-typed
/// payload here would have been a straight type mismatch against what the Books screen's context
/// menu can actually pass.
/// </summary>
public sealed class NovelBooksHookGlobals : PluginGlobals
{
    public required IReadOnlyList<Book> Books { get; init; }
}

/// <summary>No input payload - the script returns a new draft <see cref="Issue"/>.</summary>
public sealed class NewBooksHookGlobals : PluginGlobals
{
}

/// <summary>No input payload - the script returns the <see cref="Issue"/> list backing a dynamic Smart List entry.</summary>
public sealed class CreateBookListHookGlobals : PluginGlobals
{
}

/// <summary>
/// A richer <see cref="PluginHooks.CreateBookList"/> return shape (docs/superpowers/specs/
/// 2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §1) - a script can still return a
/// flat <c>IEnumerable&lt;Issue&gt;</c> for today's flat Smart Lists results grid; returning
/// <c>IEnumerable&lt;PluginBookGroup&gt;</c> instead opens the Grouped Review overlay (per-group
/// keep/skip choices, one bulk delete action) and makes the command eligible for proactive
/// Activity Center alerts when a scan finds more groups than last time. Not duplicate-specific -
/// any <c>CreateBookList</c> plugin can opt into this shape. <see cref="SuggestedKeepIssueId"/> is
/// optional; the overlay defaults to the first book in <see cref="Books"/> when null.
/// </summary>
public sealed record PluginBookGroup(string Label, IReadOnlyList<Issue> Books, int? SuggestedKeepIssueId = null);

public sealed class ParseComicPathHookGlobals : PluginGlobals
{
    public required string Path { get; init; }
}

/// <summary>
/// A <see cref="PluginHooks.ParseComicPath"/> script's return value - deliberately not CE's own
/// <c>ComicNameInfo</c> (a <c>cYo.Projects.ComicRack.Engine</c> type <c>Paperbunkr.Plugins</c>
/// doesn't reference, matching this API's existing insulation from raw engine internals - see
/// <c>IComicDisplay</c>'s deliberate scope-down in docs/superpowers/specs/2026-08-24-plugin-api-v2-
/// design.md §4). Every field is optional: only the ones set here override the built-in filename
/// parser's own guess (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §7) -
/// a script returning a bare "series was X" doesn't have to also re-derive the issue number.
/// </summary>
public sealed record ParsedComicPath(string? Series = null, string? Number = null, int? Volume = null, int? Year = null);

public sealed class NetSearchHookGlobals : PluginGlobals
{
    public required string Query { get; init; }
}

/// <summary>
/// One <see cref="PluginHooks.NetSearch"/> match, returned as <c>IEnumerable&lt;NetSearchResult&gt;</c>
/// - deliberately not the App layer's own <c>AniListMatchSample</c>/<c>IMetadataProvider</c> shapes,
/// which a script has no business referencing (same insulation principle as
/// <see cref="ParsedComicPath"/> not being CE's <c>ComicNameInfo</c>). <see cref="Confidence"/> is
/// optional - a script with no real scoring can leave it null and let the UI show it unscored
/// rather than fabricate a number.
/// </summary>
public sealed record NetSearchResult(string ExternalId, string Title, string? Url = null, double? Confidence = null);

public sealed class StartupHookGlobals : PluginGlobals
{
}

public sealed class ShutdownHookGlobals : PluginGlobals
{
}

public sealed class BookOpenedHookGlobals : PluginGlobals
{
    public required Issue Book { get; init; }
}

public sealed class ReaderResizedHookGlobals : PluginGlobals
{
    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>No input payload beyond the paired command's own state - the script shows/persists its own config UI.</summary>
public sealed class ConfigScriptHookGlobals : PluginGlobals
{
}

/// <summary>Shared by <see cref="PluginHooks.ComicInfoHtml"/> and <see cref="PluginHooks.ComicInfoUI"/>.</summary>
public sealed class ComicInfoHookGlobals : PluginGlobals
{
    public required Issue Book { get; init; }
}

/// <summary>Shared by <see cref="PluginHooks.QuickOpenHtml"/> and <see cref="PluginHooks.QuickOpenUI"/>.</summary>
public sealed class QuickOpenHookGlobals : PluginGlobals
{
    public required string Query { get; init; }
}

public sealed class DrawThumbnailOverlayHookGlobals : PluginGlobals
{
    public required Issue Book { get; init; }
}

// ---- Plugin API 4.1 domain-event hooks (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5) ----
// All notification-only: a script's return value is ignored. They run in the background, so a slow
// plugin never blocks reading or scanning. Hooks about live items carry the entity; hooks whose subject
// may already be gone by the time the plugin runs carry ids and snapshots only.

/// <summary>
/// <see cref="PluginHooks.BookRead"/> - an item was read through to the end. Comics and manga are
/// <see cref="Issue"/>, novels are <see cref="Book"/> (no shared base), so exactly one of
/// <see cref="Issue"/>/<see cref="Book"/> is set, matching <see cref="ItemType"/> - and either may be
/// null if the item was deleted between the finish and the plugin running. Fires on every finish, so a
/// re-read counts; dedupe with <see cref="FinishedUtc"/> if you only want the first.
/// </summary>
public sealed class BookReadHookGlobals : PluginGlobals
{
    public required ReadingItemType ItemType { get; init; }
    public required int ItemId { get; init; }
    public int? SeriesId { get; init; }

    /// <summary>Pages read in the session up to the finish; null when unknown. An estimate for reflowed EPUBs.</summary>
    public int? PagesRead { get; init; }

    public required DateTime FinishedUtc { get; init; }
    public Issue? Issue { get; init; }
    public Book? Book { get; init; }
}

/// <summary>
/// <see cref="PluginHooks.LibraryScanCompleted"/> - a full comic/manga folder scan finished. Counts and ids
/// only, so a plugin never has to diff the whole library to learn what changed. Books are scanned by a
/// separate service and don't fire this. <see cref="UpdatedItemIds"/> is a reserved, currently
/// always-empty collection (the scanner doesn't track updates to existing issues), and
/// <see cref="UpdatedCount"/> is 0.
/// </summary>
public sealed class LibraryScanCompletedHookGlobals : PluginGlobals
{
    public required IReadOnlyList<string> FolderPaths { get; init; }
    public required int AddedCount { get; init; }
    public required int UpdatedCount { get; init; }
    public required int SeriesTouched { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IReadOnlyList<int> AddedItemIds { get; init; }
    public required IReadOnlyList<int> UpdatedItemIds { get; init; }
}

/// <summary>
/// <see cref="PluginHooks.MissingFileDetected"/> - a file was confirmed missing under the Library
/// Health threshold. Announced once, on the pass the count crosses the threshold (not on every later
/// pass, and not on a single failed check).
/// </summary>
public sealed class MissingFileDetectedHookGlobals : PluginGlobals
{
    public required ReadingItemType ItemType { get; init; }
    public required int ItemId { get; init; }
    public required string FilePath { get; init; }
    public required string Title { get; init; }
}

/// <summary>
/// <see cref="PluginHooks.ReadingListChanged"/> - one reading-list operation's net effect. Ids only: the
/// items may already be gone. <see cref="Kind"/> is a flags value (an arc refresh can add, remove and
/// reorder in one pass); creating, renaming or deleting a whole list is not an event.
/// </summary>
public sealed class ReadingListChangedHookGlobals : PluginGlobals
{
    public required int ListId { get; init; }
    public required string ListName { get; init; }
    public required ReadingListChangeKind Kind { get; init; }
    public required IReadOnlyList<int> AddedIssueIds { get; init; }
    public required IReadOnlyList<int> RemovedIssueIds { get; init; }
}
