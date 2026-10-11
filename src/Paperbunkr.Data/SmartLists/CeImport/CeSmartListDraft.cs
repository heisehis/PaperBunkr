using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists.CeImport;

/// <summary>What dropping an unsupported condition did to the list: an AND with a condition missing matches more, an OR matches fewer.</summary>
public enum CeWarningEffect
{
    Widened,
    Narrowed,

    /// <summary>The condition was kept but works over a different set of books than it did in ComicRack, so the list may match different books either way.</summary>
    Approximated,
}

/// <summary>One condition (or group) of a ComicRack smart list that could not be imported.</summary>
public sealed record CeImportWarning(string ListName, string ConditionType, string Detail, CeWarningEffect Effect)
{
    public override string ToString() => $"{ListName}: {ConditionType} {Detail} ({Effect switch { CeWarningEffect.Widened => "list may match more", CeWarningEffect.Narrowed => "list may match fewer", _ => "list may differ" }})";
}

/// <summary>
/// A ComicRack smart list read from a <c>.cbl</c>, not yet saved: <see cref="List"/> is a detached entity graph (<c>TargetKind = Issue</c>), <see cref="Warnings"/>
/// names everything that was dropped, and <see cref="IsEmpty"/> is true when the file had matchers but none could be kept (importing that would give a list that
/// matches the whole library).
/// </summary>
public sealed class CeSmartListDraft
{
    public required string Name { get; init; }

    public required SmartList List { get; init; }

    public required IReadOnlyList<CeImportWarning> Warnings { get; init; }

    public bool IsEmpty { get; init; }
}
