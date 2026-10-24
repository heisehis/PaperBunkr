namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>How sure the engine is. Only <see cref="Verified"/> and <see cref="Strong"/> may be linked without the user. Labels, not probabilities.</summary>
public enum MatchClass
{
    /// <summary>A provider id the source supplied is carried by exactly one comic in the library.</summary>
    Verified,

    /// <summary>Series and number match exactly one comic and nothing disagrees.</summary>
    Strong,

    /// <summary>One plausible comic, but something disagrees (a different year or volume) or nothing confirms it.</summary>
    Possible,

    /// <summary>Several comics fit and the source gave nothing that tells them apart.</summary>
    Ambiguous,

    /// <summary>Provider ids point at different comics.</summary>
    Conflict,

    /// <summary>Nothing in the library fits.</summary>
    Unresolved,
}

public enum EvidenceKind
{
    Agrees,
    Unknown,
    Conflicts,
}

/// <summary>One fact the engine weighed for a candidate, e.g. (Agrees, "year") or (Unknown, "volume").</summary>
public sealed record MatchEvidence(EvidenceKind Kind, string What);

/// <summary>A comic in the library that could be the one asked for, with what speaks for and against it.</summary>
public sealed record MatchCandidate(LibraryIssue Issue, IReadOnlyList<MatchEvidence> Evidence)
{
    public bool HasConflict => Evidence.Any(e => e.Kind == EvidenceKind.Conflicts);

    public bool Agrees(string what) => Evidence.Any(e => e.Kind == EvidenceKind.Agrees && e.What == what);

    /// <summary>"same number · year agrees · volume unknown" - built from the evidence, never fixed text.</summary>
    public string Summary => string.Join(" · ", Evidence.Select(e => e.Kind switch
    {
        EvidenceKind.Agrees => e.What is "number" or "series" ? $"same {e.What}" : $"{e.What} agrees",
        EvidenceKind.Conflicts => $"different {e.What}",
        _ => $"{e.What} unknown",
    }));
}

/// <summary>What <see cref="MatchEngine.Match"/> concluded for one <see cref="ComicReference"/>. Candidates are ranked best first.</summary>
public sealed record MatchResult(ComicReference Reference, MatchClass Class, IReadOnlyList<MatchCandidate> Candidates, bool ById)
{
    /// <summary>The comic to link without asking, or null when the user has to decide (or nothing fits).</summary>
    public LibraryIssue? AutoLink => Class is MatchClass.Verified or MatchClass.Strong ? Candidates[0].Issue : null;

    /// <summary>The user has something to choose from.</summary>
    public bool NeedsChoice => Class is MatchClass.Possible or MatchClass.Ambiguous or MatchClass.Conflict;
}
