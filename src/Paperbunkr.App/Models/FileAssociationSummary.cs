namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Advanced tab's File Association list.</summary>
public class FileAssociationSummary
{
    public string Name { get; init; } = string.Empty;

    public string ExtensionList { get; init; } = string.Empty;

    public bool IsAssociated { get; init; }
}
