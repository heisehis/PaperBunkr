namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Libraries tab's Virtual Tags list.</summary>
public class VirtualTagSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }
}
