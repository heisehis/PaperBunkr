namespace Paperbunkr.App.Scraper;

/// <summary>One line of the profile editor's live preview: a comic and where the templates would put it (or why not).</summary>
public sealed class PreviewRowViewModel(string label, string text, bool isProblem)
{
    public string Label { get; } = label;

    /// <summary>"→ Publisher/Series/Series #01.cbz", or the reason nothing would happen.</summary>
    public string Text { get; } = text;

    /// <summary>The templates could not name this comic (shown in the error colour).</summary>
    public bool IsProblem { get; } = isProblem;

    public bool IsNotProblem => !IsProblem;
}
