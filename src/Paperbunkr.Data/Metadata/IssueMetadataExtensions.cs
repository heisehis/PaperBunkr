using System.Linq;
using System.Text.RegularExpressions;
using cYo.Common.Text;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Derived/computed values for <see cref="Issue"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase1-canonical-metadata-design.md), resolved dynamically rather than stored - the short
/// "Architectural Rules" companion note this phase's design doc cites: "Derived properties (like
/// ReadPercentage or IsLinked) must be resolved dynamically via resolvers, not saved as database
/// columns." Extension methods rather than <see cref="Issue"/> properties, so the entity itself
/// stays free of computed noise EF never needs to know about.
/// </summary>
public static class IssueMetadataExtensions
{
    /// <summary>
    /// CE's real "has been read" threshold - <c>ComicBook.ReadPercentageAsRead = 95</c>
    /// (src/Paperbunkr.Engine/ComicBook.cs:73), used here instead of this phase's source doc's
    /// placeholder "suggested default: 90%". A constant for now, matching this project's established
    /// pattern of shipping a fixed default before a Preferences surface exists to edit it.
    /// </summary>
    public const double ReadThresholdPercent = 95;

    /// <summary>0-100, clamped. 0 when <see cref="Issue.PageCount"/> is null/0 (nothing to divide by) rather than throwing or returning NaN.</summary>
    public static double ReadPercentage(this Issue issue)
    {
        if (issue.PageCount is not > 0)
        {
            return 0;
        }

        double raw = 100.0 * (issue.LastPageRead ?? 0) / issue.PageCount.Value;
        return double.Clamp(raw, 0, 100);
    }

    public static bool HasBeenRead(this Issue issue) => issue.ReadPercentage() >= ReadThresholdPercent;

    public static bool IsUnread(this Issue issue) => issue.ReadPercentage() == 0;

    public static bool IsInProgress(this Issue issue)
    {
        double pct = issue.ReadPercentage();
        return pct > 0 && pct < ReadThresholdPercent;
    }

    // --- Metadata Proposal effective-value resolvers (docs/superpowers/specs/2026-08-17-metadata-
    // model-phase2a-metadata-proposals-design.md) - Paperbunkr's real replacement for CE's
    // Shadow*/Proposed* field pattern. Every resolver/read site below that used to read
    // Issue.Number/Volume/Number/Count/Year/Title/Format directly now goes through these instead,
    // so a filename-inferred value (currently living only in issue.MetadataProposals, not written
    // to the field itself - see LibraryFolderScanner) still shows up everywhere it used to. ---

    public static string? EffectiveTitle(this Issue issue) => issue.Title ?? AcceptedProposalValue(issue, MetadataProposalField.Title);

    public static string? EffectiveFormat(this Issue issue) => issue.Format ?? AcceptedProposalValue(issue, MetadataProposalField.Format);

    public static string? EffectiveVolume(this Issue issue) => issue.Volume ?? AcceptedProposalValue(issue, MetadataProposalField.Volume);

    public static string? EffectiveNumber(this Issue issue) => issue.Number ?? AcceptedProposalValue(issue, MetadataProposalField.Number);

    public static int? EffectiveCount(this Issue issue) => issue.Count ?? ParseIntOrNull(AcceptedProposalValue(issue, MetadataProposalField.Count));

    public static int? EffectiveYear(this Issue issue) => issue.Year ?? ParseIntOrNull(AcceptedProposalValue(issue, MetadataProposalField.Year));

    /// <summary>
    /// No DB query - reads the already-loaded <see cref="Issue.MetadataProposals"/> collection,
    /// same established pattern as every other resolver in this file. Callers are responsible for
    /// <c>.Include(i => i.MetadataProposals)</c> where needed, same as <c>.Include(i => i.Bookmarks)</c>
    /// already is. Only <see cref="MetadataProposalStatus.Accepted"/> proposals contribute - a
    /// <see cref="MetadataProposalStatus.Pending"/> one (under the <see cref="MetadataResolutionPolicy.Prompt"/>
    /// policy) contributes nothing until a human accepts it in the Needs Review queue.
    /// </summary>
    private static string? AcceptedProposalValue(Issue issue, MetadataProposalField field) =>
        issue.MetadataProposals
            .Where(p => p.Field == field && p.Status == MetadataProposalStatus.Accepted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault()?.ProposedValue;

    /// <summary>A malformed/unparseable proposal value resolves to null rather than throwing - a corrupt proposal must never crash a read path.</summary>
    private static int? ParseIntOrNull(string? value) => int.TryParse(value, out int n) ? n : null;

    /// <summary>
    /// Numeric portion of <see cref="Issue.Number"/> for sorting, reusing the already-ported CE
    /// parser (<see cref="TextNumberFloat"/> - handles ½/¼/¾ glyphs, negatives, decimals) rather than
    /// writing a new one. Null when the value isn't purely numeric - callers fall back to natural
    /// string sort on the raw value, per this phase's design doc §10. Reads <see cref="EffectiveNumber"/>,
    /// not the raw field, so a filename-inferred value still sorts correctly.
    /// </summary>
    public static float? NumberSortKey(this Issue issue) => SortKey(issue.EffectiveNumber());

    /// <summary>Same treatment as <see cref="NumberSortKey"/>, for <see cref="Issue.Volume"/>.</summary>
    public static float? VolumeSortKey(this Issue issue) => SortKey(issue.EffectiveVolume());

    private static float? SortKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // TextNumberFloat's actually-invoked parse path (StringUtility.TryParse, a regex numeric-
        // prefix match) doesn't handle ½/¼/¾ glyphs at all - confirmed by a failing test against
        // real behavior, not assumed from the class's unused Parse/ParseExpression static methods
        // (which do handle them, but OnParseText never calls them). Added explicitly here so the
        // ½/¼/¾ handling this phase's design doc actually promises is real, not just unused CE code.
        float fraction = FractionGlyphValue(value);
        var parsed = new TextNumberFloat(value);
        if (parsed.IsNumber)
        {
            return parsed.Number + fraction;
        }

        return fraction > 0 ? fraction : null;
    }

    private static float FractionGlyphValue(string value)
    {
        if (value.Contains('¼'))
        {
            return 0.25f;
        }

        if (value.Contains('½'))
        {
            return 0.5f;
        }

        return value.Contains('¾') ? 0.75f : 0f;
    }

    private static readonly Regex AlphaNumericPattern = new(@"^\d+[A-Za-z]+$", RegexOptions.Compiled);

    /// <summary>
    /// Every branch corresponds to an example literally listed in this phase's design doc's own
    /// test-case set (§71: 1, 2, 10, 10.5, ½, 0, -1, Annual 1, Special, 1A, 1B). Order matters and
    /// differs from the doc's own listed order: AlphaNumeric must be checked before Numeric, since
    /// <see cref="TextNumberFloat"/>'s real parse path extracts a leading numeric *prefix* rather
    /// than requiring the whole string to be numeric (confirmed via a failing test against real
    /// behavior, not assumed) - "1A" would otherwise be misclassified as Numeric.
    /// </summary>
    public static IssueNumberType NumberType(this Issue issue)
    {
        string? value = issue.EffectiveNumber();
        if (string.IsNullOrWhiteSpace(value))
        {
            return IssueNumberType.Unknown;
        }

        if (value.Contains('½') || value.Contains('¼') || value.Contains('¾'))
        {
            return IssueNumberType.Fraction;
        }

        if (value.Contains("annual", System.StringComparison.OrdinalIgnoreCase))
        {
            return IssueNumberType.Annual;
        }

        if (value.Contains("special", System.StringComparison.OrdinalIgnoreCase))
        {
            return IssueNumberType.Special;
        }

        // Checked before IsNumber below - TextNumberFloat's real parse path extracts a leading
        // numeric *prefix* rather than requiring the whole string to be numeric (confirmed via a
        // failing test against real behavior), so "1A" would otherwise land in Numeric instead of
        // here.
        if (AlphaNumericPattern.IsMatch(value))
        {
            return IssueNumberType.AlphaNumeric;
        }

        if (new TextNumberFloat(value).IsNumber)
        {
            return IssueNumberType.Numeric;
        }

        return IssueNumberType.Text;
    }

    /// <summary>Combines <see cref="Issue.Year"/>/<see cref="Issue.Month"/>/<see cref="Issue.Day"/> - never fabricates a month/day that isn't actually known (defaults to the 1st when only a coarser precision is set, purely so the value is a valid <see cref="System.DateTime"/> - see <see cref="PublicationDatePrecision"/> for what's actually known).</summary>
    public static System.DateTime? PublishedDate(this Issue issue)
    {
        if (issue.EffectiveYear() is not int year || year <= 0)
        {
            return null;
        }

        int month = issue.Month is > 0 ? issue.Month.Value : 1;
        int day = issue.Day is > 0 ? issue.Day.Value : 1;
        try
        {
            return new System.DateTime(year, month, day);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Named <c>DatePrecision</c> rather than <c>PublicationDatePrecision</c> (matching this phase's
    /// design doc §12 concept) to avoid an extension-method-shares-its-return-type's-name collision
    /// with the <see cref="Metadata.PublicationDatePrecision"/> enum in this same namespace.
    /// </summary>
    public static PublicationDatePrecision DatePrecision(this Issue issue)
    {
        if (issue.EffectiveYear() is not > 0)
        {
            return PublicationDatePrecision.Unknown;
        }

        if (issue.Day is > 0)
        {
            return PublicationDatePrecision.Day;
        }

        return issue.Month is > 0 ? PublicationDatePrecision.Month : PublicationDatePrecision.Year;
    }

    public static string? FileName(this Issue issue) =>
        string.IsNullOrEmpty(issue.FilePath) ? null : System.IO.Path.GetFileName(issue.FilePath);

    public static string? FileDirectory(this Issue issue) =>
        string.IsNullOrEmpty(issue.FilePath) ? null : System.IO.Path.GetDirectoryName(issue.FilePath);

    /// <summary>Extension-derived, e.g. "cbz" - may not match the container's real format, see <see cref="SniffActualFileFormatAsync"/>.</summary>
    public static string? FileFormat(this Issue issue)
    {
        if (string.IsNullOrEmpty(issue.FilePath))
        {
            return null;
        }

        string ext = System.IO.Path.GetExtension(issue.FilePath).TrimStart('.');
        return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
    }

    public static bool IsLinked(this Issue issue) => !string.IsNullOrEmpty(issue.FilePath);

    /// <summary>
    /// Container-sniffed format, e.g. catching a mislabeled .cbr that's really a zip. Deliberately
    /// separate from the always-cheap properties above - requires opening the file, so callers must
    /// explicitly ask for it (e.g. a future "verify library" tool), never a hot path like Library
    /// grid rendering. Returns null if the file doesn't exist, isn't readable, or its signature isn't
    /// recognized.
    /// </summary>
    public static async System.Threading.Tasks.Task<string?> SniffActualFileFormatAsync(this Issue issue)
    {
        if (string.IsNullOrEmpty(issue.FilePath) || !System.IO.File.Exists(issue.FilePath))
        {
            return null;
        }

        byte[] header = new byte[8];
        await using var stream = System.IO.File.OpenRead(issue.FilePath);
        int read = await stream.ReadAsync(header.AsMemory(0, header.Length));
        if (read < 4)
        {
            return null;
        }

        // ZIP (cbz): "PK\x03\x04". RAR (cbr): "Rar!\x1A\x07". 7z (cb7): "7z\xBC\xAF\x27\x1C".
        if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
        {
            return "zip";
        }

        if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
        {
            return "rar";
        }

        if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
        {
            return "7z";
        }

        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
        {
            return "pdf";
        }

        return null;
    }
}
