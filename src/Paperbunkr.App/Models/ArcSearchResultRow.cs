using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Models;

/// <summary>Wraps one <see cref="ArcSearchResult"/> for display in the arc-search results list (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §5).</summary>
public sealed class ArcSearchResultRow
{
    public ArcSearchResultRow(ArcSearchResult result)
    {
        Result = result;
    }

    public ArcSearchResult Result { get; }

    public string Name => Result.Name;

    public string Summary
    {
        get
        {
            string publisher = string.IsNullOrEmpty(Result.Publisher) ? string.Empty : $" ({Result.Publisher})";
            return $"{publisher.Trim()} {Result.IssueCount} issue(s)".Trim();
        }
    }
}
