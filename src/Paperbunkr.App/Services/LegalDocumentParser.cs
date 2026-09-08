using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

public enum LegalBlockKind
{
    Heading1,
    Heading2,
    Quote,
    Bullet,
    Paragraph,
}

/// <summary>One inline run within a <see cref="LegalDocumentBlock"/> - plain, bold, or code, never both.</summary>
public sealed record LegalInlineRun(string Text, bool Bold, bool Code);

/// <summary>One line of a parsed legal document (docs/superpowers/specs/2026-09-07-about-redesign-design.md §Architecture 2).</summary>
public sealed record LegalDocumentBlock(LegalBlockKind Kind, IReadOnlyList<LegalInlineRun> Runs);

/// <summary>
/// Parses the narrow markdown subset actually used by the bundled legal docs (LICENSE, PRIVACY.md,
/// TERMS.md, COMICVINE_NOTICE.md) - headers, blockquote, bullets, bold, inline code, plain
/// paragraphs. Not a general CommonMark parser (docs/superpowers/specs/2026-09-07-about-redesign-
/// design.md Non-goals) - mirrors <see cref="ChangelogParser"/>'s shape (static class, pure string
/// parsing, no I/O).
/// </summary>
public static class LegalDocumentParser
{
    private static readonly Regex InlineSpanPattern = new(@"\*\*(?<bold>[^*]+)\*\*|`(?<code>[^`]+)`", RegexOptions.Compiled);

    public static IReadOnlyList<LegalDocumentBlock> Parse(string markdown)
    {
        var blocks = new List<LegalDocumentBlock>();
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            LegalBlockKind kind;
            string text;
            if (line.StartsWith("## "))
            {
                kind = LegalBlockKind.Heading2;
                text = line[3..];
            }
            else if (line.StartsWith("# "))
            {
                kind = LegalBlockKind.Heading1;
                text = line[2..];
            }
            else if (line.StartsWith("> "))
            {
                kind = LegalBlockKind.Quote;
                text = line[2..];
            }
            else if (line.StartsWith("- "))
            {
                kind = LegalBlockKind.Bullet;
                text = line[2..];
            }
            else
            {
                kind = LegalBlockKind.Paragraph;
                text = line;
            }

            blocks.Add(new LegalDocumentBlock(kind, ParseRuns(text)));
        }

        return blocks;
    }

    private static IReadOnlyList<LegalInlineRun> ParseRuns(string text)
    {
        var runs = new List<LegalInlineRun>();
        int lastEnd = 0;
        foreach (Match match in InlineSpanPattern.Matches(text))
        {
            if (match.Index > lastEnd)
            {
                runs.Add(new LegalInlineRun(text[lastEnd..match.Index], Bold: false, Code: false));
            }

            if (match.Groups["bold"].Success)
            {
                runs.Add(new LegalInlineRun(match.Groups["bold"].Value, Bold: true, Code: false));
            }
            else
            {
                runs.Add(new LegalInlineRun(match.Groups["code"].Value, Bold: false, Code: true));
            }

            lastEnd = match.Index + match.Length;
        }

        if (lastEnd < text.Length)
        {
            runs.Add(new LegalInlineRun(text[lastEnd..], Bold: false, Code: false));
        }

        return runs;
    }
}
