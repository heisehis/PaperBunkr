using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Kind-dispatching entry point for callers that don't know a <see cref="SmartList"/>'s
/// <see cref="SmartListTargetKind"/> ahead of time (docs/superpowers/specs/2026-08-30-smart-
/// collections-design.md) — today, just the Smart Lists screen's sidebar match-count display, which
/// iterates whatever list is currently selected regardless of kind. A caller that already knows the
/// kind statically (<c>CollectionResolver</c>, reading its own typed FK slots) calls the matching
/// builder directly instead of going through here.
/// </summary>
public static class SmartListEvaluation
{
    public static int MatchCount(PaperbunkrDbContext ctx, SmartList list) => list.TargetKind switch
    {
        SmartListTargetKind.Series => SeriesSmartListQueryBuilder.MatchCount(ctx, list),
        SmartListTargetKind.Novel => NovelSmartListQueryBuilder.MatchCount(ctx, list),
        _ => SmartListQueryBuilder.MatchCount(ctx, list),
    };
}
