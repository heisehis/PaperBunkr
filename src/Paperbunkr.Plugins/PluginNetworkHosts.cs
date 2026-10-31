namespace Paperbunkr.Plugins;

/// <summary>
/// A plugin's declared network hosts (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §F):
/// <c>&lt;Network&gt;&lt;Host name="api.example.com"/&gt;&lt;Host name="*.example.com"/&gt;&lt;/Network&gt;</c>. A declaration, shown to
/// the user at install and on the plugin's detail, and enforced only for <c>Environment.Http</c> - a raw <c>HttpClient</c> isn't
/// policed (the same "accidental overreach, not adversarial isolation" bar as the rest of the plugin sandbox).
/// </summary>
public static class PluginNetworkHosts
{
    /// <summary>The declared hosts, lower-cased; null with no error when none are declared; null with an error for a bad entry.</summary>
    public static IReadOnlyList<string>? Parse(PluginManifest manifest, out string? error)
    {
        error = null;
        if (manifest.Network is not { Count: > 0 } entries)
        {
            return null;
        }

        var hosts = new List<string>();
        foreach (NetworkHostManifestEntry entry in entries)
        {
            string name = entry.Name?.Trim().ToLowerInvariant() ?? string.Empty;
            string bare = name.StartsWith("*.", StringComparison.Ordinal) ? name[2..] : name;
            if (bare.Length == 0 || bare.Contains('*') || bare.Contains('/') || bare.Contains(':') || Uri.CheckHostName(bare) == UriHostNameType.Unknown)
            {
                error = $"'{entry.Name}' isn't a host name (write api.example.com or *.example.com - no scheme, path or port)";
                return null;
            }

            if (!hosts.Contains(name))
            {
                hosts.Add(name);
            }
        }

        return hosts;
    }

    /// <summary>True when <paramref name="host"/> is declared: an exact match, or under a <c>*.domain</c> entry (which also covers the bare domain).</summary>
    public static bool Allows(IReadOnlyList<string> declared, string host)
    {
        string h = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (string entry in declared)
        {
            if (entry.StartsWith("*.", StringComparison.Ordinal))
            {
                string domain = entry[2..];
                if (h == domain || h.EndsWith("." + domain, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (h == entry)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One line for the install confirm and the detail pane.</summary>
    public static string Describe(IReadOnlyList<string>? declared) =>
        declared is { Count: > 0 } ? "Declared network access: " + string.Join(", ", declared) : "No network access declared";
}
