using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Where a book page starts: the unit, the block and how many words into that block (0 unless a long paragraph was split).</summary>
public sealed record BookPageStart(int UnitIndex, string BlockId, int WordOffset);

/// <summary>
/// Book pages numbered by word count, the same at every font size and window size (docs/superpowers/specs/2026-10-27-books-reader-paged-
/// scroll-modes-design.md §6.1). A page closes at the first paragraph end once it holds <c>min</c> words, or before a paragraph that would
/// take it past <c>max</c> once it holds half of <c>min</c> (so a long paragraph does not make a 500-word page, nor a short one a 60-word page).
/// A paragraph longer than <c>max</c> is split at sentence ends. Every unit starts a new page.
/// </summary>
public sealed class BookPageMap
{
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…][""'”’)\]]?)\s+", RegexOptions.Compiled);

    private readonly Dictionary<(int Unit, string Block), int> _pageOfBlock;

    private BookPageMap(IReadOnlyList<BookPageStart> pages, Dictionary<(int, string), int> pageOfBlock)
    {
        Pages = pages;
        _pageOfBlock = pageOfBlock;
    }

    public IReadOnlyList<BookPageStart> Pages { get; }

    public int Count => Pages.Count;

    /// <param name="units">Each unit's blocks in reading order (<see cref="BookBlockReader.Read"/> of the unit's markup).</param>
    public static BookPageMap Build(IReadOnlyList<IReadOnlyList<BookBlock>> units, int minWords, int maxWords)
    {
        maxWords = Math.Max(maxWords, minWords + 1);
        var pages = new List<BookPageStart>();
        var pageOfBlock = new Dictionary<(int, string), int>();

        for (int unit = 0; unit < units.Count; unit++)
        {
            int wordsOnPage = 0;
            bool unitStarted = false;

            foreach (var block in units[unit])
            {
                foreach (var (wordOffset, words) in Segments(block.Text, maxWords))
                {
                    bool close = unitStarted && (wordsOnPage >= minWords || (wordsOnPage + words > maxWords && wordsOnPage >= minWords / 2));
                    if (!unitStarted || close)
                    {
                        pages.Add(new BookPageStart(unit, block.BlockId, wordOffset));
                        wordsOnPage = 0;
                        unitStarted = true;
                    }

                    pageOfBlock.TryAdd((unit, block.BlockId), pages.Count - 1);
                    wordsOnPage += words;
                }
            }

            if (!unitStarted)
            {
                // A unit with no readable text (a cover, an image page) still takes a page, as it does on paper.
                pages.Add(new BookPageStart(unit, string.Empty, 0));
            }
        }

        return new BookPageMap(pages, pageOfBlock);
    }

    /// <summary>The page holding the start of a block; for a unit's block that is unknown (or null), the unit's first page. -1 for an unknown unit.</summary>
    public int PageOf(int unitIndex, string? blockId)
    {
        if (blockId is not null && _pageOfBlock.TryGetValue((unitIndex, blockId), out int page))
        {
            return page;
        }

        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].UnitIndex == unitIndex)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A paragraph as (word offset, word count) pieces: itself when it fits, else sentence runs of at most <paramref name="maxWords"/> words (a sentence longer than that stays whole).</summary>
    private static IEnumerable<(int WordOffset, int Words)> Segments(string text, int maxWords)
    {
        int total = CountWords(text);
        if (total <= maxWords)
        {
            yield return (0, total);
            yield break;
        }

        int offset = 0;
        int run = 0;
        foreach (string sentence in SentenceEnd.Split(text))
        {
            int words = CountWords(sentence);
            if (run > 0 && run + words > maxWords)
            {
                yield return (offset, run);
                offset += run;
                run = 0;
            }

            run += words;
        }

        if (run > 0)
        {
            yield return (offset, run);
        }
    }

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
