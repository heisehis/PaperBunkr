using System.IO;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace Paperbunkr.App.Services;

/// <summary>A parsed <c>--open &lt;kind&gt;:&lt;id&gt;</c> CLI deep-link target (docs/superpowers/
/// specs/2026-08-30-app-shell-navigation-history-design.md).</summary>
public sealed record NavigationCliTarget(string Kind, int Id);

/// <summary>Parses the app's one CLI convention for external deep-linking - pure string handling, no
/// Avalonia dependency, so it's testable without touching <c>App.axaml.cs</c>. Any malformed or
/// unrecognized input is treated as "no deep link" (returns false) rather than throwing - a bad CLI
/// arg should never crash startup, it should just fall through to restore-on-launch.</summary>
public static class NavigationCliArgs
{
    private static readonly string[] KnownKinds = { "series", "issue", "book", "collection" };

    /// <summary>Looks for <c>--open &lt;kind&gt;:&lt;id&gt;</c> anywhere in <paramref name="args"/>.
    /// Returns <see langword="true"/> only when a recognized kind and a valid integer id are both
    /// present.</summary>
    public static bool TryParseOpenArg(string[] args, out NavigationCliTarget? target)
    {
        target = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--open")
            {
                continue;
            }

            string value = args[i + 1];
            int separatorIndex = value.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            {
                return false;
            }

            string kind = value[..separatorIndex];
            string idText = value[(separatorIndex + 1)..];

            if (!KnownKinds.Contains(kind) || !int.TryParse(idText, out int id))
            {
                return false;
            }

            target = new NavigationCliTarget(kind, id);
            return true;
        }

        return false;
    }

    /// <summary>Looks for a single bare file-path argument (the shape Windows' own file-association
    /// launch command line produces: <c>"...\Paperbunkr.exe" "%1"</c>) - docs/superpowers/specs/
    /// 2026-09-13-open-file-on-launch-design.md. Returns <see langword="true"/> only when
    /// <paramref name="args"/> has exactly one element, it isn't the <c>--open</c> flag itself, the
    /// path exists on disk, and its extension is one <see cref="Providers.Readers"/> supports - same
    /// extension check <see cref="LibraryFolderScanner"/>/<see cref="DragImportService"/> already
    /// gate imports on. A <c>--open kind:id</c> invocation is always two arguments so the length
    /// check alone already excludes it.</summary>
    public static bool TryParseFilePathArg(string[] args, out string? path)
    {
        path = null;

        if (args.Length != 1 || args[0] == "--open" || !File.Exists(args[0]))
        {
            return false;
        }

        var supportedExtensions = Providers.Readers.GetFileExtensions();
        string extension = Path.GetExtension(args[0]);
        if (!supportedExtensions.Any(e => string.Equals(e, extension, System.StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        path = args[0];
        return true;
    }
}
