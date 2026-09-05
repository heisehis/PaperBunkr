namespace Paperbunkr.Plugins.Hooks;

/// <summary>Resolves which <see cref="PluginGlobals"/> subtype a hook's script compiles against (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §3).</summary>
public static class PluginGlobalsTypeMap
{
    private static readonly IReadOnlyDictionary<string, Type> Map = new Dictionary<string, Type>
    {
        [PluginHooks.Library] = typeof(BooksHookGlobals),
        [PluginHooks.Editor] = typeof(BooksHookGlobals),
        [PluginHooks.Books] = typeof(NovelBooksHookGlobals),
        [PluginHooks.NewBooks] = typeof(NewBooksHookGlobals),
        [PluginHooks.CreateBookList] = typeof(CreateBookListHookGlobals),
        [PluginHooks.ParseComicPath] = typeof(ParseComicPathHookGlobals),
        [PluginHooks.NetSearch] = typeof(NetSearchHookGlobals),
        [PluginHooks.Startup] = typeof(StartupHookGlobals),
        [PluginHooks.Shutdown] = typeof(ShutdownHookGlobals),
        [PluginHooks.BookOpened] = typeof(BookOpenedHookGlobals),
        [PluginHooks.ReaderResized] = typeof(ReaderResizedHookGlobals),
        [PluginHooks.ConfigScript] = typeof(ConfigScriptHookGlobals),
        [PluginHooks.ComicInfoHtml] = typeof(ComicInfoHookGlobals),
        [PluginHooks.ComicInfoUI] = typeof(ComicInfoHookGlobals),
        [PluginHooks.QuickOpenHtml] = typeof(QuickOpenHookGlobals),
        [PluginHooks.QuickOpenUI] = typeof(QuickOpenHookGlobals),
        [PluginHooks.DrawThumbnailOverlay] = typeof(DrawThumbnailOverlayHookGlobals),
    };

    public static Type Resolve(string hook)
    {
        if (!Map.TryGetValue(hook, out var type))
        {
            throw new ArgumentException($"Unknown plugin hook '{hook}'.", nameof(hook));
        }

        return type;
    }
}
