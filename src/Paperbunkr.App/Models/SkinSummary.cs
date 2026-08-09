namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Appearance tab's skin list — key, display name, and whether it's the currently active skin.</summary>
public class SkinSummary
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
