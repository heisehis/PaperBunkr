namespace ClusterLibraryManager.ComicVine;

/// <summary>
/// Static imprint -&gt; parent-publisher map, ported from CE's <c>cvimprints.py</c> (design doc §3).
/// CE's own docstring is explicit: "Both the passed in and returned strings for these methods must
/// EXACTLY match their corresponding values in the ComicVine database (i.e. case, punctuation,
/// etc.)" - exact-match only, no normalization.
///
/// <b>Only a verified subset is ported here.</b> CE's real table has ~70 entries; the extracted CE
/// plugin source (the user-supplied <c>.crplugin</c> bundles) that this whole design was grilled
/// against is no longer available on disk in this session to transcribe the rest safely, and
/// fabricating additional entries would violate this project's own standing rule (verify against CE
/// source, don't guess). The 8 entries below are exactly the ones directly quoted and verified during
/// the original design pass (docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md §3).
/// <b>Follow-up work, not done here:</b> re-extract the full map from <c>cvimprints.py</c> directly
/// once the source is available again, rather than guessing at plausible-sounding entries.
///
/// The user-editable override list (CE's advanced <c>IMPRINT=X--&gt;Y</c> settings lines) is a
/// separate mechanism - see <see cref="FindParentPublisher"/>'s <paramref name="userOverrides"/>
/// parameter, checked first so a user's own mapping always wins over this static table.
/// </summary>
public static class ComicVineImprints
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["Vertigo"] = "DC Comics",
        ["Wildstorm"] = "DC Comics",
        ["CMX"] = "DC Comics",
        ["2000AD"] = "DC Comics",
        ["Icon Comics"] = "Marvel",
        ["Dark Horse Manga"] = "Dark Horse Comics",
        ["Top Cow"] = "Image Comics",
        ["Vertical"] = "Kodansha",
    };

    /// <summary>
    /// Resolves <paramref name="imprint"/> to its parent publisher: checks
    /// <paramref name="userOverrides"/> first (a user's own mapping always wins), then the static
    /// table above, and returns <paramref name="imprint"/> unchanged if neither has an exact match -
    /// matching CE's own fallback behavior exactly.
    /// </summary>
    public static string FindParentPublisher(string imprint, IReadOnlyDictionary<string, string>? userOverrides = null)
    {
        string trimmed = imprint.Trim();

        if (userOverrides is not null && userOverrides.TryGetValue(trimmed, out string? overridden))
        {
            return overridden;
        }

        return Map.TryGetValue(trimmed, out string? parent) ? parent : imprint;
    }
}
