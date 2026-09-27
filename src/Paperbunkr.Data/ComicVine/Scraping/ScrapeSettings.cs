using System.Linq;
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

    /// <summary>Which source scrapes use unless the match dialog switches one run to the other. Scheduled (unattended) scrapes always use this one.</summary>
    public ComicProvider DefaultProvider { get; set; } = ComicProvider.ComicVine;

    /// <summary>
    /// CE's <c>update_rating_b</c> defaults <b>off</b> (configuration.py:62) - it costs an extra API
    /// call CE never made by default. Everything else defaults on. Fixed docs/superpowers/specs/
    /// 2026-09-24-comicvine-scraper-fidelity-design.md §1.3 - this previously defaulted on along with
    /// every other field.
    /// </summary>
    public HashSet<ScrapeField> EnabledScrapeFields { get; set; } =
        new(Enum.GetValues<ScrapeField>().Where(f => f != ScrapeField.CommunityRating));

    /// <summary>Which generation of the field list <see cref="EnabledScrapeFields"/> was saved against; 0 for a row saved before this existed. See <see cref="Load"/>.</summary>
    public int FieldSetVersion { get; set; }

    private const int CurrentFieldSetVersion = 1;

    private static readonly ScrapeField[] FieldsAddedAfterVersion0 =
        [ScrapeField.Count, ScrapeField.MainCharacterOrTeam, ScrapeField.Concepts, ScrapeField.SeriesGroup, ScrapeField.StoryArcOrder];

    /// <summary>Checked before the static imprint table, so a user's own mapping always wins (CE's <c>IMPRINT=X--&gt;Y</c> lines).</summary>
    public Dictionary<string, string> ImprintOverrides { get; set; } = new();

    /// <summary>CE's <c>convert_imprints_b</c> (default true): resolve a recognized imprint to its parent publisher. When false, the raw imprint name is written to Publisher unchanged instead, and Imprint is left alone - CE's actual off-behavior (comicbook.py:464-466), not just "do nothing".</summary>
    public bool ConvertImprints { get; set; } = true;

    /// <summary>CE's <c>force_series_art_b</c>: always show the series/volume cover in the review dialog rather than falling back to the specific issue's own cover.</summary>
    public bool ForceSeriesArt { get; set; } = true;

    /// <summary>CE's <c>show_covers_b</c>: show cover thumbnails in the review dialogs. False hides them (a real "scrape faster on a slow connection" toggle CE had).</summary>
    public bool ShowCovers { get; set; } = true;

    /// <summary>CE's <c>scrape_delay_n</c> (default 1000ms, clamp 2000-3,600,000ms per CE's own 2-3600 <i>second</i> range): a proactive pause between books in a batch, separate from the per-HTTP-request rate limiter.</summary>
    public int ScrapeDelayMs { get; set; } = 1000;

    /// <summary>CE's <c>publisher_aliases_sm</c> (<c>PUBLISHER_ALIAS=X--&gt;Y</c>): applied to both the resolved publisher and imprint strings after imprint resolution - distinct from <see cref="ImprintOverrides"/>.</summary>
    public Dictionary<string, string> PublisherAliases { get; set; } = new();

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
            var loaded = JsonSerializer.Deserialize<ScrapeSettings>(row.Json, Json) ?? new ScrapeSettings();
            if (loaded.FieldSetVersion < CurrentFieldSetVersion)
            {
                // A saved set only lists the fields that existed when it was saved, so a field added
                // later would silently stay off for every existing user. Turn each on once, by default
                // (they can still switch any off - Save stamps the current version).
                foreach (var field in FieldsAddedAfterVersion0)
                {
                    loaded.EnabledScrapeFields.Add(field);
                }

                loaded.FieldSetVersion = CurrentFieldSetVersion;
            }

            return loaded;
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

        FieldSetVersion = CurrentFieldSetVersion;
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
