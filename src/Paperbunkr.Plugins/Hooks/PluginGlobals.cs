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

/// <summary>Shared by <see cref="PluginHooks.Library"/>, <see cref="PluginHooks.Editor"/> and <see cref="PluginHooks.Books"/> - all three are the same "operate on selected books" command family, just surfaced at different sites.</summary>
public sealed class BooksHookGlobals : PluginGlobals
{
    public required IReadOnlyList<Issue> Books { get; init; }
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

public sealed class NetSearchHookGlobals : PluginGlobals
{
    public required string Query { get; init; }
}

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
