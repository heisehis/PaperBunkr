using Paperbunkr.Data.Entities;

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
