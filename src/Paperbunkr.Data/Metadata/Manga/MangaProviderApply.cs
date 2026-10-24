using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>What a link or refresh may do beyond the defaults.</summary>
/// <param name="IncludeSpoilerTags">Also import the tags the provider marks as spoilers (off by default).</param>
public sealed record MangaApplyOptions(bool IncludeSpoilerTags = false);

/// <summary>
/// What a refresh did, in plain words, so nothing happens silently: what it applied, what it left for the user to accept (a candidate), and what it kept because the series already holds it
/// or the user set it. Also the chapters it changed, so the caller can re-derive credit indexes and queue file writes.
/// </summary>
public sealed class MangaApplyResult
{
    public IReadOnlyList<int> ChangedIssueIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> CreditChangedIssueIds { get; init; } = Array.Empty<int>();

    public List<string> Applied { get; } = new();

    public List<string> Offered { get; } = new();

    public List<string> Kept { get; } = new();

    public static MangaApplyResult None => new();

    public string Describe()
    {
        var parts = new List<string>();
        if (Applied.Count > 0) parts.Add("Applied: " + string.Join("; ", Applied) + ".");
        if (Offered.Count > 0) parts.Add("Offered to accept: " + string.Join("; ", Offered) + ".");
        if (Kept.Count > 0) parts.Add("Kept: " + string.Join("; ", Kept) + ".");
        return parts.Count == 0 ? "Nothing needed changing." : string.Join(" ", parts);
    }
}

/// <summary>
/// What a provider link or refresh does to a local manga-family series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §14). Three tiers:
/// <list type="bullet">
/// <item><b>Always applied</b> unless the field is locked: status, the provider's rating (as an external rating), every alternate title, and tags with the weight the provider gives them.</item>
/// <item><b>Filled only when blank</b>, otherwise offered as a pending candidate (a locked field is always offered, never filled): summary, authors (Writer), artists (Penciller), the original publisher, age rating.</item>
/// <item><b>Only ever suggested</b>: the reading mode. Cover and content type are not touched here at all.</item>
/// </list>
/// "Blank" means the series and its chapters hold nothing for the field: for a series the user has not confirmed in Edit Series yet, what the chapters already say counts as what the series holds
/// (the same reading Edit Series would propose), so a refresh fills real gaps but never talks over an existing value. A candidate the user already dismissed is not offered again. Tags are added and their
/// unset weights filled; a tag the provider no longer lists is never removed and a weight someone set is never replaced.
/// Everything lands on the series first and then passes down to the chapters without an override, exactly as Edit Series does.
/// </summary>
public static class MangaProviderApply
{
    /// <summary>A local manga, manhwa or manhua series, confirmed in Edit Series or not. Comics, unknown-type series and another library's series are not.</summary>
    public static bool IsLocalManga(Series series) =>
        series.RemoteSourceId is null && MangaInheritedFields.IsMangaFamily(series.ContentType);

    public static bool IsConfirmedManga(Series series) => IsLocalManga(series) && series.CanonicalReviewVersion > 0;

    /// <summary>
    /// Applies <paramref name="metadata"/> to <paramref name="series"/>. <see cref="Series.Credits"/>, <see cref="Series.SeriesTags"/> and <see cref="Series.Titles"/> must be loaded. Staged on
    /// <paramref name="context"/>; the caller saves, then re-derives the creator index for the returned credit chapters.
    /// </summary>
    public static MangaApplyResult Apply(PaperbunkrDbContext context, Series series, ExternalMediaMetadata metadata, ExternalMetadataProvider provider, MangaApplyOptions? options = null)
    {
        options ??= new MangaApplyOptions();
        var report = new MangaApplyResult();
        bool confirmed = series.CanonicalReviewVersion > 0;
        var locked = MangaFieldLocks.Parse(series.LockedFields);
        var changedKeys = new List<string>();

        // What the series holds: its own values once confirmed, otherwise what its chapters already say (read, not written).
        MangaFieldValues values;
        if (confirmed)
        {
            values = MangaInheritedFields.ReadFromSeries(series);
        }
        else
        {
            var chapters = context.Issues.Include(i => i.Tags).Where(i => i.SeriesId == series.Id).AsSplitQuery().ToList();
            values = SeriesCanonicalSeeder.Seed(series, chapters).Values;
        }

        // ---- Always applied (unless locked) ----
        ApplyStatus(context, series, metadata, provider, locked, report);
        ApplyRating(context, series, metadata, provider, report);
        AddAlternateTitles(series, metadata, report);
        ApplyWeightedTags(values, metadata, locked, options, changedKeys, report);

        // ---- Blank is filled, otherwise a candidate ----
        bool summaryChanged = ApplySummary(context, series, metadata, provider, locked, report);

        var authors = metadata.Authors;
        var artists = metadata.Artists;
        if (authors is null && artists is null && !string.IsNullOrWhiteSpace(metadata.Creator))
        {
            // A provider that only gives one joined list (AniList, MangaDex): the roles are the user's call.
            Offer(context, series, MetadataProposalField.Creator, CurrentCredits(values), metadata.Creator, provider, report, "authors and artists");
        }

        FillOrOffer(context, series, values, provider, locked, changedKeys, "Writer", MetadataProposalField.Writer, authors, report);
        FillOrOffer(context, series, values, provider, locked, changedKeys, "Penciller", MetadataProposalField.Penciller, artists, report);

        string? original = metadata.Publishers?.FirstOrDefault(p => string.Equals(p.Type, "Original", StringComparison.OrdinalIgnoreCase))?.Name;
        FillOrOfferText(context, series, values, provider, locked, changedKeys, MangaInheritedFields.Publisher, MetadataProposalField.Publisher, original, report, "publisher");
        FillOrOfferText(context, series, values, provider, locked, changedKeys, MangaInheritedFields.AgeRating, MetadataProposalField.AgeRating, AgeRatingFrom(metadata.ContentRating), report, "age rating");

        // ---- Only ever suggested ----
        if (SuggestedReadingMode(metadata) is ReadingMode suggested && suggested != series.ReadingMode)
        {
            Offer(context, series, MetadataProposalField.ReadingMode, series.ReadingMode.ToString(), suggested.ToString(), provider, report, "reading mode");
        }

        MangaInheritedFields.WriteToSeries(series, values, changedKeys);
        var passedDown = FanOut(context, series, values, changedKeys, additiveTags: !confirmed);
        if (summaryChanged)
        {
            MarkSummaryDependentDirty(context, series);
        }

        var result = new MangaApplyResult { ChangedIssueIds = passedDown.ChangedIssueIds, CreditChangedIssueIds = passedDown.CreditChangedIssueIds };
        result.Applied.AddRange(report.Applied);
        result.Offered.AddRange(report.Offered);
        result.Kept.AddRange(report.Kept);
        return result;
    }

    // ---------- always applied ----------

    private static void ApplyStatus(PaperbunkrDbContext context, Series series, ExternalMediaMetadata metadata, ExternalMetadataProvider provider, HashSet<string> locked, MangaApplyResult report)
    {
        if (string.IsNullOrWhiteSpace(metadata.Status))
        {
            return;
        }

        var status = SeriesStatusNormalizer.Normalize(metadata.Status);
        if (status == SeriesStatus.Unknown || status == series.Status)
        {
            return;
        }

        if (locked.Contains(MangaFieldLocks.StatusKey))
        {
            report.Kept.Add($"status ({series.Status}, set by you)");
            return;
        }

        Record(context, series, MetadataProposalField.Status, series.Status.ToString(), status.ToString(), provider);
        series.Status = status;
        report.Applied.Add($"status {status}");
    }

    /// <summary>The provider's own 0-100 score, kept as one external rating per provider (never mixed into the user's rating).</summary>
    private static void ApplyRating(PaperbunkrDbContext context, Series series, ExternalMediaMetadata metadata, ExternalMetadataProvider provider, MangaApplyResult report)
    {
        if (metadata.Rating is not double score || score <= 0)
        {
            return;
        }

        var existing = context.ExternalRatings.FirstOrDefault(r => r.SeriesId == series.Id && r.Provider == provider);
        if (existing is null)
        {
            context.ExternalRatings.Add(new ExternalRating { SeriesId = series.Id, Provider = provider, Value = (decimal)Math.Round(score, 2), Scale = 100, RetrievedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Value = (decimal)Math.Round(score, 2);
            existing.Scale = 100;
            existing.RetrievedAt = DateTime.UtcNow;
        }

        report.Applied.Add($"rating {Math.Round(score, 1)}/100");
    }

    private static void AddAlternateTitles(Series series, ExternalMediaMetadata metadata, MangaApplyResult report)
    {
        int added = 0;
        foreach (string title in metadata.AlternateTitles ?? Array.Empty<string>())
        {
            bool known = string.Equals(series.Name, title, StringComparison.OrdinalIgnoreCase)
                || series.Titles.Any(t => string.Equals(t.Value, title, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                series.Titles.Add(new SeriesTitle { SeriesId = series.Id, Value = title, Type = SeriesTitleType.Localized });
                added++;
            }
        }

        if (added > 0)
        {
            report.Applied.Add($"{added} alternate title{(added == 1 ? "" : "s")}");
        }
    }

    /// <summary>
    /// Genres and tags with the provider's weights. Genres come from the provider's genre-flagged tags (which carry a weight) plus any flat genre name it lists without one (unset).
    /// Additive only: new tags are added, a tag's unset weight is filled, nothing is removed and no weight that is already set is replaced. A locked list is left entirely alone.
    /// </summary>
    private static void ApplyWeightedTags(MangaFieldValues values, ExternalMediaMetadata metadata, HashSet<string> locked, MangaApplyOptions options, List<string> changedKeys, MangaApplyResult report)
    {
        var all = metadata.WeightedTags ?? Array.Empty<ProviderTag>();
        var tags = all.Where(t => options.IncludeSpoilerTags || !t.IsSpoiler).ToList();
        int spoilersSkipped = all.Count - tags.Count;

        if (locked.Contains(MangaInheritedFields.Genre))
        {
            report.Kept.Add("genres (set by you)");
        }
        else
        {
            var incoming = tags.Where(t => t.IsGenre).Select(t => new MangaTagValue(t.Name, "Genre", t.Weight)).ToList();
            foreach (string flat in metadata.GenreTags ?? Array.Empty<string>())
            {
                if (!incoming.Any(t => string.Equals(t.Value, flat, StringComparison.OrdinalIgnoreCase)))
                {
                    incoming.Add(new MangaTagValue(Capitalize(flat), "Genre", IssueTagWeight.Unset));
                }
            }

            var (added, weighted) = Merge(values.Genres, incoming);
            if (added + weighted > 0)
            {
                changedKeys.Add(MangaInheritedFields.Genre);
                report.Applied.Add($"{Describe(added, weighted, "genre")}");
            }
        }

        if (locked.Contains(MangaInheritedFields.Tags))
        {
            report.Kept.Add("tags (set by you)");
        }
        else
        {
            var incoming = tags.Where(t => !t.IsGenre).Select(t => new MangaTagValue(t.Name, t.Category, t.Weight)).ToList();
            if (incoming.Count == 0 && metadata.OtherTags is { Count: > 0 } plain)
            {
                incoming = plain.Select(t => new MangaTagValue(t.Value, t.Category, IssueTagWeight.Unset)).ToList();
            }

            var (added, weighted) = Merge(values.Tags, incoming);
            if (added + weighted > 0)
            {
                changedKeys.Add(MangaInheritedFields.Tags);
                report.Applied.Add($"{Describe(added, weighted, "tag")}");
            }
        }

        if (spoilersSkipped > 0)
        {
            report.Kept.Add($"{spoilersSkipped} spoiler tag{(spoilersSkipped == 1 ? "" : "s")} not imported");
        }
    }

    private static string Describe(int added, int weighted, string noun)
    {
        var parts = new List<string>();
        if (added > 0) parts.Add($"{added} new {noun}{(added == 1 ? "" : "s")}");
        if (weighted > 0) parts.Add($"{weighted} weight{(weighted == 1 ? "" : "s")} filled");
        return string.Join(", ", parts);
    }

    /// <summary>Adds <paramref name="incoming"/> to <paramref name="target"/> and fills an unset weight. Returns how many were added and how many weights were filled.</summary>
    private static (int Added, int Weighted) Merge(List<MangaTagValue> target, IEnumerable<MangaTagValue> incoming)
    {
        int added = 0, weighted = 0;
        foreach (var tag in incoming)
        {
            int index = target.FindIndex(t => string.Equals(t.Value, tag.Value, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                target.Add(tag);
                added++;
            }
            else if (target[index].Weight == IssueTagWeight.Unset && tag.Weight != IssueTagWeight.Unset)
            {
                target[index] = target[index] with { Weight = tag.Weight };
                weighted++;
            }
        }

        return (added, weighted);
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    // ---------- blank is filled, otherwise a candidate ----------

    private static bool ApplySummary(PaperbunkrDbContext context, Series series, ExternalMediaMetadata metadata, ExternalMetadataProvider provider, HashSet<string> locked, MangaApplyResult report)
    {
        if (string.IsNullOrWhiteSpace(metadata.Description))
        {
            return false;
        }

        bool isLocked = locked.Contains(MangaFieldLocks.SummaryKey);
        if (string.IsNullOrWhiteSpace(series.Summary) && !isLocked)
        {
            Record(context, series, MetadataProposalField.Summary, series.Summary, metadata.Description, provider);
            series.Summary = metadata.Description;
            report.Applied.Add("summary");
            return true;
        }

        Offer(context, series, MetadataProposalField.Summary, series.Summary, metadata.Description, provider, report, "summary", isLocked);
        return false;
    }

    private static void FillOrOffer(PaperbunkrDbContext context, Series series, MangaFieldValues values, ExternalMetadataProvider provider, HashSet<string> locked, List<string> changedKeys,
        string key, MetadataProposalField proposalField, IReadOnlyList<string>? names, MangaApplyResult report)
    {
        if (names is not { Count: > 0 })
        {
            return;
        }

        string label = key == "Writer" ? "authors (Writer)" : "artists (Penciller)";
        var current = values.Credits.GetValueOrDefault(key) ?? new List<string>();
        bool isLocked = locked.Contains(key);
        if (current.Count == 0 && !isLocked)
        {
            values.Credits[key] = names.ToList();
            changedKeys.Add(key);
            Record(context, series, proposalField, null, string.Join(", ", names), provider);
            report.Applied.Add($"{label} {string.Join(", ", names)}");
            return;
        }

        var field = MangaInheritedFields.Find(key)!;
        var offered = new MangaFieldValues();
        offered.Credits[key] = names.ToList();
        if (MangaInheritedFields.ComparableSeriesValue(offered, field) != MangaInheritedFields.ComparableSeriesValue(values, field))
        {
            Offer(context, series, proposalField, string.Join(", ", current), string.Join(", ", names), provider, report, label, isLocked);
        }
    }

    private static void FillOrOfferText(PaperbunkrDbContext context, Series series, MangaFieldValues values, ExternalMetadataProvider provider, HashSet<string> locked, List<string> changedKeys,
        string key, MetadataProposalField proposalField, string? proposed, MangaApplyResult report, string label)
    {
        proposed = MangaInheritedFields.NullIfBlank(proposed);
        if (proposed is null)
        {
            return;
        }

        string? current = MangaInheritedFields.NullIfBlank(values.Text.GetValueOrDefault(key));
        bool isLocked = locked.Contains(key);
        if (current is null && !isLocked)
        {
            values.Text[key] = proposed;
            changedKeys.Add(key);
            Record(context, series, proposalField, null, proposed, provider);
            report.Applied.Add($"{label} {proposed}");
            return;
        }

        if (!string.Equals(current, proposed, StringComparison.OrdinalIgnoreCase))
        {
            Offer(context, series, proposalField, current, proposed, provider, report, label, isLocked);
        }
    }

    // ---------- mapping ----------

    /// <summary>MangaBaka's content rating as the age-rating words the app uses. "safe" is not an age, so it fills nothing.</summary>
    public static string? AgeRatingFrom(string? contentRating) => contentRating?.Trim().ToLowerInvariant() switch
    {
        "suggestive" => "Teen",
        "erotica" => "Mature 17+",
        "pornographic" => "X18+",
        "adult" => "Adults Only 18+",
        _ => null,
    };

    /// <summary>A reading mode the provider's data points to, or null when it does not say. Korean-origin series are scrolling strips; Japanese manga reads right to left.</summary>
    public static ReadingMode? SuggestedReadingMode(ExternalMediaMetadata metadata)
    {
        string? language = metadata.OriginalLanguage?.Trim().ToLowerInvariant();
        string? type = metadata.PublicationFormat?.Trim().ToLowerInvariant();
        if (type == "manhwa" || language == "ko")
        {
            return ReadingMode.Webtoon;
        }

        if (type == "manga" && (language is null or "ja" or "ja-latn"))
        {
            return ReadingMode.RightToLeft;
        }

        return null;
    }

    // ---------- proposals ----------

    private static string CurrentCredits(MangaFieldValues values) =>
        string.Join("; ", MangaInheritedFields.CreditRoles
            .Where(role => (values.Credits.GetValueOrDefault(role)?.Count ?? 0) > 0)
            .Select(role => $"{role}: {string.Join(", ", values.Credits[role])}"));

    /// <summary>An answered question, recorded for the history: the provider's value was applied.</summary>
    private static void Record(PaperbunkrDbContext context, Series series, MetadataProposalField field, string? current, string? proposed, ExternalMetadataProvider provider) =>
        context.MetadataProposals.Add(new MetadataProposal
        {
            SeriesId = series.Id,
            Field = field,
            CurrentValue = current,
            ProposedValue = proposed,
            Source = MetadataProposalSource.MetadataProvider,
            ProviderKey = provider,
            Confidence = 1.0m,
            Status = MetadataProposalStatus.Accepted,
            ResolvedAt = DateTime.UtcNow,
        });

    /// <summary>A pending candidate for the user. Not stored when it equals what the series has, or when the same one is already waiting or was dismissed.</summary>
    private static void Offer(PaperbunkrDbContext context, Series series, MetadataProposalField field, string? current, string? proposed, ExternalMetadataProvider provider, MangaApplyResult report, string label, bool isLocked = false)
    {
        if (string.IsNullOrWhiteSpace(proposed) || string.Equals(current?.Trim(), proposed.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool dismissed = context.MetadataProposals.Any(p =>
            p.SeriesId == series.Id && p.Field == field && p.ProviderKey == provider && p.ProposedValue == proposed && p.Status == MetadataProposalStatus.Rejected);
        if (dismissed)
        {
            return;
        }

        bool waiting = context.MetadataProposals.Any(p =>
            p.SeriesId == series.Id && p.Field == field && p.ProviderKey == provider && p.ProposedValue == proposed && p.Status == MetadataProposalStatus.Pending);
        if (!waiting)
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                SeriesId = series.Id,
                Field = field,
                CurrentValue = current,
                ProposedValue = proposed,
                Source = MetadataProposalSource.MetadataProvider,
                ProviderKey = provider,
                Confidence = 1.0m,
                Status = MetadataProposalStatus.Pending,
            });
        }

        report.Offered.Add(isLocked ? $"{label} (you set this, so it is only offered)" : label);
    }

    // ---------- passing values down ----------

    /// <summary>
    /// Writes <paramref name="keys"/> from <paramref name="values"/> onto every chapter without an override for that field, taking the one-time snapshot of a chapter before its first
    /// change and marking the chapter as owing a file write when the series syncs. For a series the user has not confirmed (<paramref name="additiveTags"/>), tags are only added to each
    /// chapter (and unset weights filled) so chapters that disagree keep what they have. Staged on the context; the caller saves.
    /// </summary>
    private static MangaApplyResult FanOut(PaperbunkrDbContext context, Series series, MangaFieldValues values, IReadOnlyCollection<string> keys, bool additiveTags)
    {
        if (keys.Count == 0)
        {
            return MangaApplyResult.None;
        }

        var issues = context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).Where(i => i.SeriesId == series.Id).AsSplitQuery().ToList();
        var snapshotted = context.IssueMetadataSnapshots.Where(s => s.Issue!.SeriesId == series.Id).Select(s => s.IssueId).ToHashSet();
        var changed = new List<int>();
        var credit = new List<int>();

        foreach (var issue in issues)
        {
            bool written = false;
            foreach (string key in keys.Where(k => !issue.FieldOverrides.Any(o => o.FieldKey == k)))
            {
                var field = MangaInheritedFields.Find(key)!;
                bool isTag = field.Kind == MangaFieldKind.Tags;
                if (isTag ? TagsMatch(issue, field, values, additiveTags) : MangaInheritedFields.ComparableIssueValue(issue, field) == MangaInheritedFields.ComparableSeriesValue(values, field))
                {
                    continue;
                }

                if (snapshotted.Add(issue.Id))
                {
                    context.IssueMetadataSnapshots.Add(new IssueMetadataSnapshot { IssueId = issue.Id, FieldsJson = MangaIssueSnapshots.Serialize(issue), TakenUtc = DateTime.UtcNow });
                }

                if (isTag && additiveTags)
                {
                    AddTags(issue, field, values);
                }
                else
                {
                    MangaInheritedFields.WriteToIssue(issue, field, values);
                }

                written = true;
                if (field.Kind == MangaFieldKind.Credit)
                {
                    credit.Add(issue.Id);
                }
            }

            if (written)
            {
                changed.Add(issue.Id);
                if (series.EmbeddedSyncEnabled)
                {
                    issue.EmbeddedDirty = true;
                }
            }
        }

        return new MangaApplyResult { ChangedIssueIds = changed, CreditChangedIssueIds = credit.Distinct().ToList() };
    }

    /// <summary>For a confirmed series the chapter must equal the series list exactly (name, category and weight); for an unconfirmed one it only needs to already contain every tag with its weight.</summary>
    private static bool TagsMatch(Issue issue, MangaField field, MangaFieldValues values, bool additive)
    {
        var wanted = field.TagField == IssueTagField.Genre ? values.Genres : values.Tags;
        var have = issue.Tags.Where(t => t.Field == field.TagField).ToList();
        bool containsAll = wanted.All(w => have.Any(h => string.Equals(h.Value, w.Value, StringComparison.OrdinalIgnoreCase) && h.Weight == w.Weight
            && (additive || string.Equals(h.Category ?? string.Empty, w.Category ?? string.Empty, StringComparison.Ordinal))));
        return additive ? containsAll : containsAll && have.Count == wanted.Count;
    }

    /// <summary>Adds the series' tags a chapter lacks and fills a chapter tag's unset weight, leaving everything else on the chapter as it is.</summary>
    private static void AddTags(Issue issue, MangaField field, MangaFieldValues values)
    {
        foreach (var tag in field.TagField == IssueTagField.Genre ? values.Genres : values.Tags)
        {
            var existing = issue.Tags.FirstOrDefault(t => t.Field == field.TagField && string.Equals(t.Value, tag.Value, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                issue.Tags.Add(new IssueTag { IssueId = issue.Id, Field = field.TagField!.Value, Value = tag.Value, Category = tag.Category, Weight = tag.Weight });
            }
            else if (existing.Weight == IssueTagWeight.Unset && tag.Weight != IssueTagWeight.Unset)
            {
                existing.Weight = tag.Weight;
            }
        }
    }

    /// <summary>A provider summary changes what a chapter with no synopsis of its own exports, so those files owe a write. Staged on the context; the caller saves.</summary>
    public static void MarkSummaryDependentDirty(PaperbunkrDbContext context, Series series)
    {
        if (!series.EmbeddedSyncEnabled)
        {
            return;
        }

        foreach (var issue in context.Issues.Where(i => i.SeriesId == series.Id && (i.Summary == null || i.Summary == "")))
        {
            issue.EmbeddedDirty = true;
        }
    }
}
