namespace Paperbunkr.Data.Entities;

/// <summary>One entry in a <see cref="ReadingList"/>, always a real <see cref="Issue"/> reference — see <see cref="ReadingList"/>'s remarks.</summary>
public class ReadingListItem
{
    public int Id { get; set; }

    public int ReadingListId { get; set; }

    public ReadingList? ReadingList { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>Sub-arc header shown above a run of items in the UI. Paperbunkr-original — no CE or CBLManager precedent; null renders ungrouped.</summary>
    public string? GroupLabel { get; set; }

    public int SortOrder { get; set; }
}
