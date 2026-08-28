using System.Collections.Generic;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The five comic ages ComicRack CE ships as <c>[Book Ages]</c> defaults
/// (<c>_reference/ComicRackCE/ComicRack/Output/DefaultLists.txt</c>) - docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4g-age-progression-design.md.
/// </summary>
public enum ComicAge
{
    Platinum,
    Golden,
    Silver,
    Bronze,
    Modern,
}

/// <summary>
/// One <see cref="ComicAge"/>'s display name, CE boundary years, and (display-only) the commonly
/// cited scholarly range where it diverges from CE's own. <see cref="CommonlyCitedRange"/> never
/// drives classification logic - it exists purely so era pickers / progression-bar tooltips can
/// surface both conventions rather than silently picking one.
/// </summary>
public sealed record ComicAgeInfo(string DisplayName, int CeStartYear, int? CeEndYear, string? CommonlyCitedRange)
{
    /// <summary>The exact string CE ships in its <c>[Book Ages]</c> default list, e.g. <c>"Golden (1938-55)"</c> - what a user Accept writes into <see cref="Issue.BookAge"/> so it round-trips with CE-migrated data.</summary>
    public string CeListLabel { get; init; } = DisplayName;
}

/// <summary>
/// Field-descriptor-dictionary for <see cref="ComicAge"/> (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md), consistent with <see cref="RelationTypeCatalog"/>
/// / <see cref="FormatSignalCatalog"/>. CE's five-stage list is what actually classifies an issue;
/// Wikipedia's commonly-cited boundaries are incorporated as display context only.
/// </summary>
public static class ComicAgeCatalog
{
    public static readonly IReadOnlyDictionary<ComicAge, ComicAgeInfo> All = new Dictionary<ComicAge, ComicAgeInfo>
    {
        [ComicAge.Platinum] = new("Platinum Age", 1897, 1937, "sometimes dated 1897-1938") { CeListLabel = "Platinum (1897-1937)" },
        [ComicAge.Golden] = new("Golden Age", 1938, 1955, "commonly cited as 1938-1956") { CeListLabel = "Golden (1938-55)" },
        [ComicAge.Silver] = new("Silver Age", 1956, 1969, "commonly cited as 1956-1970") { CeListLabel = "Silver (1956-69)" },
        [ComicAge.Bronze] = new("Bronze Age", 1970, 1979, "commonly cited elsewhere as 1970-1985") { CeListLabel = "Bronze (1970-79)" },
        [ComicAge.Modern] = new("Modern Age", 1980, null, null) { CeListLabel = "Modern (1980-Now)" },
    };

    /// <summary>CE boundaries. Returns <see langword="null"/> for a year before the Platinum Age starts.</summary>
    public static ComicAge? FromYear(int year) => year switch
    {
        >= 1980 => ComicAge.Modern,
        >= 1970 => ComicAge.Bronze,
        >= 1956 => ComicAge.Silver,
        >= 1938 => ComicAge.Golden,
        >= 1897 => ComicAge.Platinum,
        _ => null,
    };
}
