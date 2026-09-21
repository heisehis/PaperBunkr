namespace Paperbunkr.Plugins.Hooks;

/// <summary>
/// The hook-name constants: 17 ported from ComicRackCE's <c>PluginEngine.ScriptType*</c> constants
/// (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §3/§5) plus the four Paperbunkr-only
/// domain-event hooks Plugin API 4.1 added (<see cref="DomainEventHooks"/>). <see cref="ValidHooks"/>
/// maps each to a human-readable group description, feeding the Plugin screen's grouping/labels.
/// </summary>
public static class PluginHooks
{
    public const string CreateBookList = "CreateBookList";
    public const string ParseComicPath = "ParseComicPath";
    public const string Library = "Library";
    public const string Editor = "Editor";
    public const string Books = "Books";
    public const string NewBooks = "NewBooks";
    public const string BookOpened = "BookOpened";
    public const string ReaderResized = "ReaderResized";
    public const string NetSearch = "NetSearch";
    public const string Startup = "Startup";
    public const string Shutdown = "Shutdown";
    public const string ConfigScript = "ConfigScript";
    public const string ComicInfoHtml = "ComicInfoHtml";
    public const string ComicInfoUI = "ComicInfoUI";
    public const string QuickOpenHtml = "QuickOpenHtml";
    public const string QuickOpenUI = "QuickOpenUI";
    public const string DrawThumbnailOverlay = "DrawThumbnailOverlay";

    // Paperbunkr-only domain-event hooks (Plugin API 4.1, docs/superpowers/specs/2026-09-20-plugin-api-
    // 4-1-design.md §5) - not in CE. All notification-only: the return value is ignored.

    /// <summary>A comic or novel was read through to the end. Fires on every finish, so a re-read fires again.</summary>
    public const string BookRead = "BookRead";

    /// <summary>A full comic/manga library folder scan completed (Books are scanned by a separate service and don't fire this).</summary>
    public const string LibraryScanCompleted = "LibraryScanCompleted";

    /// <summary>A file was confirmed missing under the Library Health threshold (announced once, on the pass it crosses it).</summary>
    public const string MissingFileDetected = "MissingFileDetected";

    /// <summary>A reading list's membership or order changed (one event per operation).</summary>
    public const string ReadingListChanged = "ReadingListChanged";

    /// <summary>The four hooks added in Plugin API 4.1 - the only ones that aren't in the 4.0 baseline.</summary>
    public static readonly IReadOnlyList<string> DomainEventHooks = new[] { BookRead, LibraryScanCompleted, MissingFileDetected, ReadingListChanged };

    private const string DescBookRead = "Actions when a Book is finished";
    private const string DescLibraryScan = "Actions when a Library scan completes";
    private const string DescMissingFile = "Actions when a file is confirmed missing";
    private const string DescReadingList = "Actions when a Reading List changes";

    private const string DescEditBooks = "Edit/Update Books Commands";
    private const string DescNewBooks = "Create New Books Commands";
    private const string DescParsePath = "Book Path Parsers";
    private const string DescBookOpened = "Actions when Books are opened";
    private const string DescReaderResized = "Actions when Reader is resized";
    private const string DescSearch = "Additional Search Providers";
    private const string DescInfo = "Book Information Panels";
    private const string DescStartup = "Actions when Paperbunkr starts";
    private const string DescShutdown = "Actions when Paperbunkr shuts down";
    private const string DescQuickOpen = "Quick Open Panels";
    private const string DescThumbOverlay = "Custom Book Thumbnail Overlays";

    public static readonly IReadOnlyDictionary<string, string> ValidHooks = new Dictionary<string, string>
    {
        [CreateBookList] = DescEditBooks,
        [ParseComicPath] = DescParsePath,
        [Library] = DescEditBooks,
        [Editor] = DescEditBooks,
        [Books] = DescEditBooks,
        [NewBooks] = DescNewBooks,
        [BookOpened] = DescBookOpened,
        [ReaderResized] = DescReaderResized,
        [NetSearch] = DescSearch,
        [Startup] = DescStartup,
        [Shutdown] = DescShutdown,
        [ConfigScript] = string.Empty,
        [ComicInfoHtml] = DescInfo,
        [ComicInfoUI] = DescInfo,
        [QuickOpenHtml] = DescQuickOpen,
        [QuickOpenUI] = DescQuickOpen,
        [DrawThumbnailOverlay] = DescThumbOverlay,
        [BookRead] = DescBookRead,
        [LibraryScanCompleted] = DescLibraryScan,
        [MissingFileDetected] = DescMissingFile,
        [ReadingListChanged] = DescReadingList,
    };

    /// <summary>
    /// The plugin API minor each hook first shipped in (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
    /// design.md §3.3). Metadata only - it feeds failure-hint text and docs and is not enforced: a
    /// command bound to a hook this host doesn't know simply never fires. Kept beside
    /// <see cref="ValidHooks"/> rather than folded into it so <see cref="ValidHooks"/>'s shape is unchanged.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Version> HookSince = ValidHooks.Keys
        .ToDictionary(hook => hook, hook => DomainEventHooks.Contains(hook) ? new Version(4, 1) : new Version(4, 0));

    /// <summary>The API version <paramref name="hook"/> first shipped in, or null for a hook name this host doesn't know.</summary>
    public static Version? Since(string hook) => HookSince.GetValueOrDefault(hook);
}
