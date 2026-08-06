namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-defined name/value pair attached to an <see cref="Issue"/>. Fresh design, not a port of
/// CE's packed <c>CustomValuesStore</c> delimited string (parsed on every match in
/// <c>ComicBookCustomValuesMatcher</c>) — a real child table queries cleanly in SQL instead, per
/// the provenance practice in docs/onboarding.md §2 of writing fresh where it costs nothing.
/// </summary>
public class IssueCustomValue
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
