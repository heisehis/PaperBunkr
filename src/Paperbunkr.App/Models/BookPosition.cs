namespace Paperbunkr.App.Models;

/// <summary>
/// A reading position within a book (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5) - <see cref="CharacterOffset"/> is the offset of a paragraph boundary within the chapter's
/// concatenated text (paragraphs joined by a blank line), not a mid-paragraph character index -
/// pages only ever break at paragraph boundaries (design spec §4's accepted simplification also
/// applies here: word-wrap within a paragraph is Avalonia's own TextBlock behavior, not something
/// this reader repaginates around). Survives font-size/window-size changes, unlike a page number.
/// </summary>
public readonly record struct BookPosition(int ChapterIndex, int CharacterOffset)
{
    public static readonly BookPosition Start = new(0, 0);
}
