using System.Text.Json.Serialization;

namespace Paperbunkr.Data.Gcd;

/// <summary>A Grand Comics Database series as the extract keeps it (docs/superpowers/specs/2026-09-27-gcd-data-design.md §1).</summary>
public sealed record GcdSeries(int Id, string Name, int? YearBegan, int? YearEnded, string? Publisher, string? Language, int IssueCount);

/// <summary>A GCD issue: <see cref="KeyDate"/> is GCD's sort date and <see cref="OnSaleDate"/> the on-sale date, both "yyyy-mm-dd" with 00 for unknown parts.</summary>
public sealed record GcdIssue(int Id, int SeriesId, string Number, string? KeyDate, string? OnSaleDate);

/// <summary>
/// A GCD series bond: <see cref="OriginId"/> (older) continues into <see cref="TargetId"/> (newer). <see cref="BondType"/> is GCD's own
/// name: minor_name_numbering_continues, major_name_numbering_continues, publisher_numbering_continues, subnumbering_continues,
/// merge_numbering_continues, merge, reboot.
/// </summary>
public sealed record GcdBond(int OriginId, int TargetId, int? OriginIssueId, int? TargetIssueId, string BondType);

/// <summary><c>gcd-data.json</c>: where the extract zip is and how to check it (§1-§2).</summary>
public sealed record GcdManifest(
    [property: JsonPropertyName("dumpDate")] string DumpDate,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion);

/// <summary>Where a series' GCD id came from.</summary>
public enum GcdMatchSourceKind
{
    /// <summary>Metron's own <c>gcd_id</c> for the series.</summary>
    Metron = 0,

    /// <summary>A unique name + start year + publisher match.</summary>
    Name = 1,
}

public static class GcdLinks
{
    public static string Series(int gcdSeriesId) => $"https://www.comics.org/series/{gcdSeriesId}/";

    public static string Issue(int gcdIssueId) => $"https://www.comics.org/issue/{gcdIssueId}/";

    public const string Attribution = "Grand Comics Database™ (https://www.comics.org)";

    public const string Licence = "CC BY-SA 4.0";

    public const string LicenceUrl = "https://creativecommons.org/licenses/by-sa/4.0/";

    /// <summary>"yyyy-mm-dd" (00 parts allowed) → <c>year*100+month</c>, month 0 when unknown; null when there's no year.</summary>
    public static int? DateKey(string? gcdDate)
    {
        if (string.IsNullOrWhiteSpace(gcdDate) || gcdDate.Length < 4 || !int.TryParse(gcdDate.AsSpan(0, 4), out int year) || year <= 0)
        {
            return null;
        }

        int month = gcdDate.Length >= 7 && int.TryParse(gcdDate.AsSpan(5, 2), out int m) && m is >= 1 and <= 12 ? m : 0;
        return (year * 100) + month;
    }
}
