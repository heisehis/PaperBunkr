namespace Paperbunkr.App.Models;

/// <summary>
/// One issue in the Library inspector's issue strip (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 3): a
/// number chip that says at a glance whether the issue is read, next to read, or has lost its file. Number chips wrap, so the
/// strip stays usable for a long series where a rail of covers would scroll for a long way; the cover and title are one hover
/// or focus away, in the strip's shared popup.
/// </summary>
public sealed class PreviewIssueChip
{
    /// <summary>The issue's row: what a click drills into, and where the popup's cover comes from.</summary>
    public required IssueListRow Row { get; init; }

    /// <summary>What the chip shows: the issue number, or its position in the series when it has none.</summary>
    public required string Label { get; init; }

    public bool IsRead { get; init; }

    /// <summary>The issue the panel's Continue button opens.</summary>
    public bool IsNext { get; init; }

    /// <summary>The file can no longer be found on disk. Drawn dashed and named in <see cref="StateLabel"/>, so it never rests on colour alone.</summary>
    public bool IsMissing { get; init; }

    /// <summary>Popup heading, e.g. "#13 The Long Fall".</summary>
    public required string Title { get; init; }

    /// <summary>Popup second line: "File missing", "Next to read", "Read", "In progress" or "Unread".</summary>
    public required string StateLabel { get; init; }

    /// <summary>Screen-reader name: the number and the state together.</summary>
    public string AccessibleName => $"Issue {Label}, {StateLabel}";
}
