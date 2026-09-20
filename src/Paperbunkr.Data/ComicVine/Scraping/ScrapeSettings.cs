using System.Text.Json;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// The scraper's settings (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 4.1), ported from the Cluster Library Manager's
/// <c>PluginSettings</c>. The API key is not here: it lives in Preferences → Connections like every other key. Each option keeps CE's own name and default as
/// documented on the plugin it came from.
/// </summary>
public sealed class ScrapeSettings
{
    /// <summary>CE's <c>ow_existing_b</c>: overwrite fields that already have a value.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>CE's <c>ignore_blanks_b</c>: don't replace an existing value with a blank one.</summary>
    public bool IgnoreBlankValues { get; set; }

    /// <summary>CE's <c>autochoose_series_b</c>: apply the top-scored match without showing the review dialog.</summary>
    public bool AutoChooseTopMatch { get; set; }

    /// <summary>CE's <c>confirm_issue_b</c>: after the series is chosen, show the issue-match dialog instead of silently applying whatever number matching finds.</summary>
    public bool ConfirmIssueMatch { get; set; } = true;

    public HashSet<ScrapeField> EnabledScrapeFields { get; set; } = new(Enum.GetValues<ScrapeField>());

    /// <summary>Checked before the static imprint table, so a user's own mapping always wins (CE's <c>IMPRINT=X--&gt;Y</c> lines).</summary>
    public Dictionary<string, string> ImprintOverrides { get; set; } = new();

    /// <summary>CE's <c>IGNORE_BEFORE_YEAR</c>; null is the same as CE's default (nothing is excluded).</summary>
    public int? IgnoreVolumesBeforeYear { get; set; }

    /// <summary>CE's <c>IGNORE_AFTER_YEAR</c>; null is the same as CE's default.</summary>
    public int? IgnoreVolumesAfterYear { get; set; }

    /// <summary>CE's <c>MAX_SEARCH_RESULTS</c> (default 100): how many search results are scored per book.</summary>
    public int? MaxSearchResults { get; set; } = 100;

    /// <summary>CE's <c>NEVER_IGNORE_THRESHOLD</c>: a volume with at least this many issues bypasses both the year and publisher filters.</summary>
    public int? NeverIgnoreThreshold { get; set; }

    /// <summary>CE's <c>IGNORE_PUBLISHER</c> lines: candidates from these publishers are dropped before scoring (case-insensitive, trimmed).</summary>
    public HashSet<string> IgnoredPublishers { get; set; } = new();

    /// <summary>CE's <c>IGNORE_SEARCHTERM</c> lines: each word is stripped (whole word, case-insensitive) from the query sent to ComicVine.</summary>
    public HashSet<string> IgnoredSearchTerms { get; set; } = new();

    /// <summary>The write policy this configuration implies for <see cref="IssueDetailsApplier"/>.</summary>
    public ScrapeFieldPolicy ToPolicy() => new()
    {
        Enabled = new HashSet<ScrapeField>(EnabledScrapeFields),
        OverwriteExisting = OverwriteExisting,
        IgnoreBlankValues = IgnoreBlankValues,
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Loads the singleton row, or the defaults when there is none (or it can't be read: a bad row must never block scraping).</summary>
    public static ScrapeSettings Load(PaperbunkrDbContext context)
    {
        var row = context.ScrapeSettingsRows.FirstOrDefault(r => r.Id == 1);
        if (row is null || string.IsNullOrWhiteSpace(row.Json))
        {
            return new ScrapeSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ScrapeSettings>(row.Json, Json) ?? new ScrapeSettings();
        }
        catch (JsonException)
        {
            return new ScrapeSettings();
        }
    }

    public void Save(PaperbunkrDbContext context)
    {
        var row = context.ScrapeSettingsRows.FirstOrDefault(r => r.Id == 1);
        if (row is null)
        {
            row = new ScrapeSettingsRow { Id = 1 };
            context.ScrapeSettingsRows.Add(row);
        }

        row.Json = JsonSerializer.Serialize(this, Json);
        context.SaveChanges();
    }
}

/// <summary>Storage for <see cref="ScrapeSettings"/>: one row, the settings as JSON (a set and two dictionaries don't map to columns usefully).</summary>
public class ScrapeSettingsRow
{
    public int Id { get; set; }

    public string Json { get; set; } = string.Empty;
}
