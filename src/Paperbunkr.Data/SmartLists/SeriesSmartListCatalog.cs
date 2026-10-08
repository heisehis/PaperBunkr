using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Field catalog for <see cref="SmartListTargetKind.Series"/> lists (docs/superpowers/specs/
/// 2026-08-30-smart-collections-design.md). A separate small catalog rather than tagging
/// <see cref="SmartListCatalog"/>'s existing <see cref="SmartListFieldDefinition"/> entries with a
/// target kind — that would mean touching all of that catalog's ~50 existing constructor call
/// sites for a field this catalog doesn't need. Covers the 8 fields already shared conceptually
/// with the Issue catalog (evaluated here straight off <see cref="Series"/>, not via an
/// <see cref="Issue"/> join) plus the two Series-only fields.
///
/// <b>Genre/Publisher deliberately read <see cref="Series.Genre"/>/<see cref="Series.Publisher"/>
/// directly</b> — the opposite choice from the Issue catalog's <c>i.JoinedGenre()</c>/<c>i.Publisher</c>,
/// which read the per-issue canonical fields. This is the approved design decision (not an
/// oversight): those Series-level columns are known-stale, populated once at CE-migration time —
/// a Series-targeted Genre/Publisher condition will under-match until that's revisited separately.
/// </summary>
internal static class SeriesSmartListCatalog
{
    public static readonly IReadOnlyDictionary<SmartListField, SmartListFieldDefinition> Definitions =
        new Dictionary<SmartListField, SmartListFieldDefinition>
        {
            [SmartListField.SeriesName] = new(SmartListField.SeriesName, "Name", SmartListDataType.Text),
            [SmartListField.SeriesSortName] = new(SmartListField.SeriesSortName, "Sort Name", SmartListDataType.Text),
            [SmartListField.Genre] = new(SmartListField.Genre, "Genre", SmartListDataType.Text),
            [SmartListField.Publisher] = new(SmartListField.Publisher, "Publisher", SmartListDataType.Text),
            [SmartListField.ContentType] = new(SmartListField.ContentType, "Content Type", SmartListDataType.Text),
            [SmartListField.ReadingMode] = new(SmartListField.ReadingMode, "Reading Mode", SmartListDataType.Text),
            [SmartListField.SeriesComplete] = new(SmartListField.SeriesComplete, "Series Complete", SmartListDataType.Toggle),
            [SmartListField.SeriesStatus] = new(SmartListField.SeriesStatus, "Status", SmartListDataType.Text),
            [SmartListField.ReadingStatus] = new(SmartListField.ReadingStatus, "Reading Status", SmartListDataType.Text),
            [SmartListField.Continuity] = new(SmartListField.Continuity, "Continuity", SmartListDataType.Text),

            // Smart features S1 (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.1): per-series reading progress. Not in CE.
            [SmartListField.UnreadCount] = new(SmartListField.UnreadCount, "Unread Issues", SmartListDataType.Number),
            [SmartListField.ReadCount] = new(SmartListField.ReadCount, "Read Issues", SmartListDataType.Number),
            [SmartListField.DaysSinceLastRead] = new(SmartListField.DaysSinceLastRead, "Days Since Last Read", SmartListDataType.Number),
        };

    /// <summary>The fields derived from the series' issues and reading events (they make the query builder load both).</summary>
    public static bool IsProgressField(SmartListField field) =>
        field is SmartListField.UnreadCount or SmartListField.ReadCount or SmartListField.DaysSinceLastRead;

    /// <summary>
    /// The value of a progress <paramref name="field"/>, or null when there is none (a never-opened series has no
    /// <see cref="SmartListField.DaysSinceLastRead"/>, and a null never matches a number condition).
    /// </summary>
    public static float? ProgressValue(SmartListField field, SeriesProgress progress, DateTime nowUtc) => field switch
    {
        SmartListField.UnreadCount => progress.UnreadCount,
        SmartListField.ReadCount => progress.ReadCount,
        SmartListField.DaysSinceLastRead => progress.DaysSinceLastRead(nowUtc),
        _ => null,
    };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Series, string>> TextSelectors =
        new Dictionary<SmartListField, Func<Series, string>>
        {
            [SmartListField.SeriesName] = s => s.Name,
            [SmartListField.SeriesSortName] = s => s.SortName ?? string.Empty,
            [SmartListField.Genre] = s => s.Genre ?? string.Empty,
            [SmartListField.Publisher] = s => s.Publisher ?? string.Empty,
            [SmartListField.ContentType] = s => s.ContentType.ToString(),
            [SmartListField.ReadingMode] = s => s.ReadingMode.ToString(),
            [SmartListField.SeriesStatus] = s => s.Status.ToString(),
            [SmartListField.ReadingStatus] = s => s.ReadingStatus.ToString(),
            // Same join convention as the Issue catalog's Continuity selector.
            [SmartListField.Continuity] = s => string.Join("; ", s.ContinuityMemberships.Select(m => m.Continuity.Name)),
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Series, bool>> ToggleSelectors =
        new Dictionary<SmartListField, Func<Series, bool>>
        {
            [SmartListField.SeriesComplete] = s => s.IsComplete,
        };
}
