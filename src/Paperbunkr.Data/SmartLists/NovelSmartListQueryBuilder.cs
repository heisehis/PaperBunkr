using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Evaluates a <see cref="SmartListTargetKind.Novel"/> list's condition tree against
/// <see cref="Book"/> rows (docs/superpowers/specs/2026-08-30-smart-collections-design.md). Mirrors
/// <see cref="SmartListQueryBuilder"/>/<see cref="SeriesSmartListQueryBuilder"/>'s shape — see
/// <see cref="SeriesSmartListQueryBuilder"/>'s doc comment for why the group-combination logic is
/// copied per kind while leaf evaluation is shared.
/// </summary>
internal static class NovelSmartListQueryBuilder
{
    public sealed class NovelSnapshot
    {
        public required IReadOnlyList<Book> Books { get; init; }
    }

    public static NovelSnapshot LoadSnapshot(PaperbunkrDbContext ctx) =>
        new() { Books = ctx.Books.Include(b => b.BookSeries).AsSplitQuery().ToList() };

    public static List<Book> Evaluate(NovelSnapshot snapshot, SmartList list) =>
        snapshot.Books.Where(b => EvaluateGroup(b, list.RootGroup)).ToList();

    public static List<Book> Build(PaperbunkrDbContext ctx, SmartList list) =>
        Evaluate(LoadSnapshot(ctx), list);

    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => Build(ctx, list).Count;

    private static bool EvaluateGroup(Book book, SmartListConditionGroup group)
    {
        IEnumerable<bool> results = group.Conditions
            .OrderBy(c => c.SortOrder)
            .Select(c => EvaluateCondition(book, c) ^ c.Not)
            .Concat(group.ChildGroups
                .OrderBy(g => g.SortOrder)
                .Select(g => EvaluateGroup(book, g)));

        return group.Mode == SmartListGroupMode.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateCondition(Book book, SmartListCondition condition)
    {
        var definition = NovelSmartListCatalog.Definitions[condition.Field];
        return definition.DataType switch
        {
            SmartListDataType.Text => SmartListLeafEvaluator.EvaluateText(NovelSmartListCatalog.TextSelectors[condition.Field](book), condition),
            SmartListDataType.Number => SmartListLeafEvaluator.EvaluateNumber(NovelSmartListCatalog.NumberSelectors[condition.Field](book), condition),
            SmartListDataType.Toggle => SmartListLeafEvaluator.EvaluateToggle(NovelSmartListCatalog.ToggleSelectors[condition.Field](book), condition),
            SmartListDataType.Date => SmartListLeafEvaluator.EvaluateDate(NovelSmartListCatalog.DateSelectors[condition.Field](book), condition),
            _ => false,
        };
    }
}
