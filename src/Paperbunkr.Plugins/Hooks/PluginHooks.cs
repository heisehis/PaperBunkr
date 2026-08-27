namespace Paperbunkr.Plugins.Hooks;

/// <summary>
/// The 17 hook-name constants, ported from ComicRackCE's <c>PluginEngine.ScriptType*</c> constants
/// (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §3/§5). <see cref="ValidHooks"/> maps
/// each to a human-readable group description, feeding the Plugin screen's grouping/labels.
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
    };
}
