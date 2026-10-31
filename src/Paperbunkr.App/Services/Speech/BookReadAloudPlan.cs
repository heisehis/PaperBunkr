using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Services.Speech;

/// <summary>One paragraph-sized piece of the chapter to speak: the page block it came from and its plain text.</summary>
public sealed record SpeechBlock(string BlockId, string Text);

/// <summary>
/// Turns a chapter's markup into the ordered list of blocks to read aloud (docs/superpowers/specs/2026-10-27-books-slice4d-read-aloud-design.md §3).
/// Blocks are the <c>id="pb-p&lt;n&gt;"</c> elements the highlights and reading position already use; a block that contains another block (a
/// blockquote around paragraphs) is skipped so nothing is spoken twice, and empty or image-only blocks are dropped.
/// </summary>
public static class BookReadAloudPlan
{
    /// <param name="startBlockId">Reading starts at this block (the one at the top of the page); the chapter start when null or unknown.</param>
    public static IReadOnlyList<SpeechBlock> Build(string? chapterHtml, string? startBlockId)
    {
        if (string.IsNullOrWhiteSpace(chapterHtml))
        {
            return Array.Empty<SpeechBlock>();
        }

        var blocks = BookBlockReader.Read(chapterHtml).Select(b => new SpeechBlock(b.BlockId, b.Text)).ToList();

        // The top-of-page block may be one that is not read (a blockquote around paragraphs, an empty or image-only block): start at the next read block after it.
        if (startBlockId is not null && Number(startBlockId) is int startNumber)
        {
            return blocks.Where(b => Number(b.BlockId) >= startNumber).ToList();
        }

        return blocks;
    }

    private static int? Number(string blockId) =>
        blockId.StartsWith("pb-p", StringComparison.Ordinal) && int.TryParse(blockId.AsSpan(4), out int n) ? n : null;
}
