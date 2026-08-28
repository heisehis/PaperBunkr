using System;
using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>How strong a signal an <see cref="Issue.Format"/> value is that the issue is part of something bigger than its own series.</summary>
public enum FormatSignalStrength
{
    None,
    Weak,
    Strong,
}

/// <summary>One <see cref="Issue.Format"/> value's event-signal classification (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md).</summary>
public sealed record FormatSignalInfo(FormatSignalStrength Strength, EventMembershipRole? SuggestedRole);

/// <summary>
/// Classifies ComicRack CE's shipped <c>[Book Formats]</c> default vocabulary
/// (<c>_reference/ComicRackCE/ComicRack/Output/DefaultLists.txt</c>) by how strong an event signal
/// each value is (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-
/// suggestions-design.md). Field-descriptor-dictionary shape, matching <see cref="RelationTypeCatalog"/>.
///
/// The full 16-value CE list is imported as the Format field's autocomplete list (parity, cheap),
/// but only the subset below is a genuine "this issue is part of something bigger" signal -
/// <c>Trade Paper Back</c>/<c>Hardcover</c>/<c>Web Comic</c>/<c>Black &amp; White</c>/
/// <c>Director's Cut</c>/<c>Sketch</c> describe packaging/edition and carry no event signal at all,
/// so they're deliberately absent here and resolve to <see cref="FormatSignalStrength.None"/>.
/// </summary>
public static class FormatSignalCatalog
{
    /// <summary>CE's full shipped <c>[Book Formats]</c> default list - the Format field's autocomplete vocabulary (parity).</summary>
    public static readonly IReadOnlyList<string> CeDefaultFormats = new[]
    {
        "1/2", "Annual", "Black & White", "Director's Cut", "Epilogue", "Giant", "King", "Minus 1",
        "Sketch", "Special", "Preview", "Prologue", "Trade Paper Back", "One Shot", "Web Comic", "Hardcover",
    };

    public static readonly IReadOnlyDictionary<string, FormatSignalInfo> Defaults = new Dictionary<string, FormatSignalInfo>(StringComparer.OrdinalIgnoreCase)
    {
        ["Prologue"] = new(FormatSignalStrength.Strong, EventMembershipRole.Prologue),
        ["Epilogue"] = new(FormatSignalStrength.Strong, EventMembershipRole.Epilogue),
        ["Annual"] = new(FormatSignalStrength.Strong, null),
        ["Special"] = new(FormatSignalStrength.Strong, null),
        ["One Shot"] = new(FormatSignalStrength.Strong, null),
        // Minus 1 -> Prologue role, not a coincidence: Marvel's 1997 "-1" event was a company-wide
        // prequel crossover built entirely around that Format value.
        ["Minus 1"] = new(FormatSignalStrength.Weak, EventMembershipRole.Prologue),
        ["Giant"] = new(FormatSignalStrength.Weak, null),
        ["King"] = new(FormatSignalStrength.Weak, null),
        ["1/2"] = new(FormatSignalStrength.Weak, null),
        ["Preview"] = new(FormatSignalStrength.Weak, null),
    };

    private static readonly FormatSignalInfo NoSignal = new(FormatSignalStrength.None, null);

    /// <summary>
    /// A value not in <see cref="Defaults"/> (including any custom value a user typed that isn't
    /// part of CE's default list at all) resolves to <see cref="FormatSignalStrength.None"/>.
    /// Lookup is case-insensitive.
    /// </summary>
    public static FormatSignalInfo Resolve(string? format) =>
        string.IsNullOrWhiteSpace(format) ? NoSignal : Defaults.GetValueOrDefault(format, NoSignal);
}
