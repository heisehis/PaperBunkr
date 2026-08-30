using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Classifies <see cref="Entities.Issue.Format"/> values that pull an issue out of a series' main
/// numbered flow into the Detail screen's Specials tab (docs/superpowers/specs/2026-08-28-series-
/// detail-specials-tab-design.md) - a deliberate Kavita-inspired deviation, not CE parity (CE has
/// no Special/IsSpecial concept anywhere in <c>ComicBook.cs</c>/<c>ComicInfo.cs</c>). Distinct from
/// <see cref="FormatSignalCatalog"/>, which classifies a different axis (event-suggestion strength)
/// - <c>Trade Paper Back</c>/<c>Director's Cut</c> are special-triggering here but carry no event
/// signal there, and <c>Giant</c>/<c>King</c>/<c>1/2</c>/<c>Preview</c> are the reverse.
/// </summary>
public static class SpecialFormatCatalog
{
    /// <summary>
    /// The subset of CE's shipped Format vocabulary (<see cref="FormatSignalCatalog.CeDefaultFormats"/>)
    /// that also appears, under an equivalent name, in Kavita's real special-triggering Format list
    /// (wiki.kavitareader.com/guides/metadata/comics). CE's other 9 values (1/2, Black &amp; White,
    /// Giant, King, Minus 1, Sketch, Preview, Web Comic, Hardcover) are real CE values but NOT
    /// special-triggering under Kavita's own logic, so they're deliberately absent.
    /// </summary>
    private static readonly string[] CeOverlap =
    {
        "Special", "Director's Cut", "Annual", "Epilogue", "One Shot", "Prologue", "Trade Paper Back",
    };

    /// <summary>
    /// Values Kavita treats as special-triggering that CE's <c>DefaultLists.txt</c> does NOT ship at
    /// all - a deliberate addition to Paperbunkr's Format vocabulary, not a CE port.
    /// </summary>
    internal static readonly string[] KavitaOnlyAdditions =
    {
        "Reference", "Box Set", "Anthology", "Omnibus", "Compendium", "Absolute",
        "Graphic Novel", "GN", "FCBD", "Giant Size",
    };

    public static readonly IReadOnlySet<string> Values =
        new HashSet<string>(CeOverlap.Concat(KavitaOnlyAdditions), StringComparer.OrdinalIgnoreCase);

    public static bool IsSpecial(string? format) =>
        !string.IsNullOrWhiteSpace(format) && Values.Contains(format);
}
