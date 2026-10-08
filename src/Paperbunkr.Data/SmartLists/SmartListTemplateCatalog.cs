using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>One rule of a <see cref="SmartListTemplate"/> - the same shape as a <see cref="SmartListCondition"/>, minus the ids.</summary>
public sealed record SmartListTemplateRule(SmartListField Field, SmartListOperator Operator, string Value, string? Value2 = null, bool Not = false);

/// <summary>
/// A ready-made smart list the gallery offers (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.2). Picking one
/// makes an ordinary editable <see cref="SmartList"/> via <see cref="SmartListTemplateCatalog.Instantiate"/>; nothing links the
/// list back to its template afterwards.
/// </summary>
public sealed record SmartListTemplate(
    string Id,
    string Name,
    SmartListTargetKind Kind,
    string Description,
    SmartListGroupMode Mode,
    IReadOnlyList<SmartListTemplateRule> Rules);

/// <summary>
/// The code-defined template list behind the "New Smart List" gallery. A deliberate deviation from CE, which seeds a handful of
/// default lists (already seeded here as the built-in system lists) and has no template picker. Every rule uses only fields that
/// exist in its kind's catalog - <c>SmartListTemplateCatalogTests</c> guards that.
/// </summary>
public static class SmartListTemplateCatalog
{
    private static SmartListTemplateRule Rule(SmartListField field, SmartListOperator op, string value, string? value2 = null, bool not = false) =>
        new(field, op, value, value2, not);

    public static readonly IReadOnlyList<SmartListTemplate> All =
    [
        new("issue.unreadManga", "Unread manga", SmartListTargetKind.Issue, "Manga you have not started.", SmartListGroupMode.And,
        [
            Rule(SmartListField.ContentType, SmartListOperator.Is, nameof(ContentType.Manga)),
            Rule(SmartListField.ReadPercentage, SmartListOperator.LessThan, "10"),
        ]),
        new("issue.recentOngoing", "Recently added, ongoing", SmartListTargetKind.Issue, "New arrivals from series that are still running.", SmartListGroupMode.And,
        [
            Rule(SmartListField.Added, SmartListOperator.WithinLastDays, "30"),
            Rule(SmartListField.SeriesStatus, SmartListOperator.Is, nameof(SeriesStatus.Ongoing)),
        ]),
        new("issue.needsReview", "Needs-review flagged", SmartListTargetKind.Issue, "Issues with metadata proposals waiting for you.", SmartListGroupMode.And,
        [
            Rule(SmartListField.HasPendingProposal, SmartListOperator.Is, "true"),
        ]),
        new("issue.ratedUnread", "Highly rated, unread", SmartListTargetKind.Issue, "Your best-rated books you have not opened.", SmartListGroupMode.And,
        [
            Rule(SmartListField.Rating, SmartListOperator.GreaterThan, "3"),
            Rule(SmartListField.ReadPercentage, SmartListOperator.LessThan, "10"),
        ]),
        new("issue.missingMetadata", "Missing metadata", SmartListTargetKind.Issue, "No summary, no year or no publisher.", SmartListGroupMode.Or,
        [
            Rule(SmartListField.Summary, SmartListOperator.Is, string.Empty),
            Rule(SmartListField.Year, SmartListOperator.LessThan, "0"),
            Rule(SmartListField.Publisher, SmartListOperator.Is, string.Empty),
        ]),
        new("issue.neverOpened", "Never opened (90+ days)", SmartListTargetKind.Issue, "Added over 90 days ago and still unread.", SmartListGroupMode.And,
        [
            Rule(SmartListField.ReadPercentage, SmartListOperator.Is, "0"),
            Rule(SmartListField.Added, SmartListOperator.WithinLastDays, "90", not: true),
        ]),

        new("series.behind", "Behind on ongoing series", SmartListTargetKind.Series, "Running series with 3 or more unread issues that you have not touched in a month.", SmartListGroupMode.And,
        [
            Rule(SmartListField.SeriesStatus, SmartListOperator.Is, nameof(SeriesStatus.Ongoing)),
            Rule(SmartListField.UnreadCount, SmartListOperator.GreaterThan, "2"),
            Rule(SmartListField.DaysSinceLastRead, SmartListOperator.GreaterThan, "30"),
        ]),
        new("series.midRun", "Mid-run", SmartListTargetKind.Series, "Series you are reading, with issues still to go.", SmartListGroupMode.And,
        [
            Rule(SmartListField.ReadingStatus, SmartListOperator.Is, nameof(ReadingStatus.Reading)),
            Rule(SmartListField.ReadCount, SmartListOperator.GreaterThan, "0"),
            Rule(SmartListField.UnreadCount, SmartListOperator.GreaterThan, "0"),
        ]),
        new("series.almostFinished", "Almost finished", SmartListTargetKind.Series, "One to three issues left.", SmartListGroupMode.And,
        [
            Rule(SmartListField.ReadCount, SmartListOperator.GreaterThan, "0"),
            Rule(SmartListField.UnreadCount, SmartListOperator.InRange, "1", "3"),
        ]),
        new("series.stalled", "Stalled", SmartListTargetKind.Series, "Started, not finished, and idle for three weeks or more.", SmartListGroupMode.And,
        [
            Rule(SmartListField.UnreadCount, SmartListOperator.GreaterThan, "0"),
            Rule(SmartListField.DaysSinceLastRead, SmartListOperator.GreaterThan, "21"),
        ]),

        new("novel.inProgress", "In progress", SmartListTargetKind.Novel, "Novels you have opened and not finished.", SmartListGroupMode.And,
        [
            Rule(SmartListField.NovelFinished, SmartListOperator.Is, "false"),
            Rule(SmartListField.NovelOpened, SmartListOperator.IsAfter, "1900-01-01"),
        ]),
    ];

    public static IReadOnlyList<SmartListTemplate> For(SmartListTargetKind kind) => All.Where(t => t.Kind == kind).ToList();

    /// <summary>The field definitions a list of <paramref name="kind"/> can filter on.</summary>
    internal static IReadOnlyDictionary<SmartListField, SmartListFieldDefinition> DefinitionsFor(SmartListTargetKind kind) => kind switch
    {
        SmartListTargetKind.Series => SeriesSmartListCatalog.Definitions,
        SmartListTargetKind.Novel => NovelSmartListCatalog.Definitions,
        _ => SmartListCatalog.Definitions,
    };

    /// <summary>A new, unsaved, editable list carrying the template's rules. The caller sets <see cref="SmartList.SortOrder"/> and saves it.</summary>
    public static SmartList Instantiate(SmartListTemplate template)
    {
        var group = new SmartListConditionGroup { Mode = template.Mode };
        int order = 0;
        foreach (var rule in template.Rules)
        {
            group.Conditions.Add(new SmartListCondition
            {
                Field = rule.Field,
                Operator = rule.Operator,
                Value = rule.Value,
                Value2 = rule.Value2,
                Not = rule.Not,
                SortOrder = order++,
            });
        }

        return new SmartList { Name = template.Name, IsSystem = false, TargetKind = template.Kind, RootGroup = group };
    }
}
