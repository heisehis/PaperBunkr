using System.Linq;

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
}
