using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services.Reader;

/// <summary>One leaf block of a chapter: its <c>pb-p</c> id, its plain text and where its tag starts in the markup it was read from.</summary>
public sealed record BookBlock(string BlockId, string Text, int Offset);

/// <summary>
/// Reads the <c>id="pb-p&lt;n&gt;"</c> blocks out of a chapter's injected markup, in document order. A block that contains other blocks (a
/// blockquote around paragraphs) is searched inside instead of being returned itself, so no text appears twice; empty and image-only blocks
/// are dropped. Shared by read aloud and the book page map (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §6.1).
/// </summary>
public static class BookBlockReader
{
    private static readonly Regex Block = new(
        @"<(?<tag>p|h[1-6]|li|blockquote)\b(?<attrs>[^>]*?)\bid\s*=\s*""(?<id>pb-p\d+)""(?<rest>[^>]*)>(?<inner>.*?)</\k<tag>\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex NestedBlock = new(@"<(p|h[1-6]|li|blockquote)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<BookBlock> Read(string? html)
    {
        var blocks = new List<BookBlock>();
        if (!string.IsNullOrWhiteSpace(html))
        {
            Collect(html, 0, blocks);
        }

        return blocks;
    }

    private static void Collect(string html, int baseOffset, List<BookBlock> blocks)
    {
        foreach (Match match in Block.Matches(html))
        {
            var inner = match.Groups["inner"];
            if (NestedBlock.IsMatch(inner.Value))
            {
                Collect(inner.Value, baseOffset + inner.Index, blocks);
                continue;
            }

            string text = Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(inner.Value, " ")), " ").Trim();
            if (text.Length > 0 && text.Any(char.IsLetterOrDigit))
            {
                blocks.Add(new BookBlock(match.Groups["id"].Value, text, baseOffset + match.Index));
            }
        }
    }
}
