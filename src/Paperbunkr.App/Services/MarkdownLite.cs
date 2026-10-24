using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services;

public enum MdBlockKind
{
    Heading1,
    Heading2,
    Heading3,
    Paragraph,
    Bullet,
    Quote,
    Preformatted,
}

public enum MdRunStyle
{
    Plain,
    Bold,
    Italic,
    Code,
    Link,
    Underline,
}

/// <summary>One inline run. <see cref="Target"/> is set for <see cref="MdRunStyle.Link"/> only.</summary>
public sealed record MdRun(string Text, MdRunStyle Style, string? Target = null);

/// <summary>One block (a heading, paragraph, bullet, quote or preformatted text) and its inline runs.</summary>
public sealed record MdBlock(MdBlockKind Kind, IReadOnlyList<MdRun> Runs)
{
    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// The markdown subset the bundled docs and <c>CHANGELOG.md</c> actually use (docs/superpowers/specs/2026-09-26-about-polish-design.md
/// §1), shared by the changelog and the legal viewer. Unlike the line-per-block parser it replaced, consecutive lines join into one
/// block, so hard-wrapped paragraphs and bullets read as one piece of text. Not CommonMark: no tables, images, nesting or HTML.
/// Never throws; anything it doesn't recognise stays literal text.
/// </summary>
public static class MarkdownLite
{
    private static readonly Regex HeadingPattern = new(@"^(?<hashes>#{1,6})\s+(?<text>.*?)\s*#*$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^[-*+]\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^\d+[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex RulePattern = new(@"^([-*_])(\s*\1){2,}$", RegexOptions.Compiled);

    // Tried left to right at each position, so "**" is bold before "*" can be italic. The italic forms need a non-word, non-marker
    // character (or the edge) on both outer sides, so snake_case and 2*3*4 stay literal.
    private const string InlineAlternatives =
        @"\*\*(?<bold>[^*]+?)\*\*" +
        @"|`(?<code>[^`]+)`" +
        @"|\[(?<ltext>[^\]]+)\]\((?<ltarget>[^)\s]+)\)" +
        @"|<(?<auto>https?://[^>\s]+)>" +
        @"|(?<![\w*])\*(?<ital>[^*\s](?:[^*]*?[^*\s])?)\*(?![\w*])" +
        @"|(?<![\w_])_(?<ital2>[^_\s](?:[^_]*?[^_\s])?)_(?![\w_])";

    private static readonly Regex InlinePattern = new(InlineAlternatives, RegexOptions.Compiled);

    // Descriptions also underline (<u>) and turn a bare web address into a link.
    private static readonly Regex DescriptionInlinePattern = new(
        InlineAlternatives + @"|<u>(?<under>.+?)</u>" + @"|(?<auto2>https?://[^\s<>)\]]*[^\s<>)\].,;:!?'""])", RegexOptions.Compiled);

    private static readonly Regex HtmlBreak = new(@"<\s*(br|/p|/div|/li)\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlTag = new(@"</?\s*(?!u>|/u>)[a-z][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A provider or user-typed description as blocks: <c>**bold**</c>, <c>*italic*</c>, <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/<c>&lt;u&gt;</c>, <c>[b]</c>/<c>[i]</c>/<c>[u]</c>,
    /// <c>[text](url)</c> and bare web addresses render; other tags are dropped and every line break starts a new paragraph (descriptions are written line by line).
    /// </summary>
    private static readonly Regex RuleLine = new(@"^[\s\-_=–—]+$", RegexOptions.Compiled);

    public static IReadOnlyList<MdBlock> ParseDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string s = text.Replace("\r\n", "\n");
        s = Regex.Replace(s, @"<\s*(b|strong)\s*>(.*?)<\s*/\s*(b|strong)\s*>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*(i|em)\s*>(.*?)<\s*/\s*(i|em)\s*>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*u\s*>(.*?)<\s*/\s*u\s*>", "<u>$1</u>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[b\](.*?)\[/b\]", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[i\](.*?)\[/i\]", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[u\](.*?)\[/u\]", "<u>$1</u>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*a\s[^>]*href\s*=\s*""([^""]+)""[^>]*>(.*?)<\s*/\s*a\s*>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = HtmlBreak.Replace(s, "\n");
        s = HtmlTag.Replace(s, string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);

        var blocks = new List<MdBlock>();
        foreach (string raw in s.Split('\n'))
        {
            string line = raw.Trim();
            // A line of only dashes, underscores or equals signs is a scraped separator, not text (2026-10-20 UI polish, remaining screens Q5).
            if (line.Length == 0 || RuleLine.IsMatch(line))
            {
                continue;
            }

            var runs = ParseInline(line, DescriptionInlinePattern);
            blocks.Add(new MdBlock(MdBlockKind.Paragraph, runs));
        }

        return blocks;
    }

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        MdBlockKind? kind = null;
        var lines = new List<string>();
        bool inFence = false;

        void Flush()
        {
            if (kind is { } k && lines.Count > 0)
            {
                blocks.Add(k == MdBlockKind.Preformatted
                    ? new MdBlock(k, [new MdRun(string.Join("\n", lines), MdRunStyle.Plain)])
                    : new MdBlock(k, ParseInline(string.Join(" ", lines))));
            }

            kind = null;
            lines.Clear();
        }

        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();

            if (inFence)
            {
                if (line.StartsWith("```"))
                {
                    Flush();
                    inFence = false;
                }
                else
                {
                    lines.Add(raw.TrimEnd());
                }

                continue;
            }

            if (line.StartsWith("```"))
            {
                Flush();
                inFence = true;
                kind = MdBlockKind.Preformatted;
                continue;
            }

            if (line.Length == 0 || RulePattern.IsMatch(line))
            {
                Flush();
                continue;
            }

            var heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                Flush();
                var level = heading.Groups["hashes"].Length switch
                {
                    1 => MdBlockKind.Heading1,
                    2 => MdBlockKind.Heading2,
                    _ => MdBlockKind.Heading3,
                };
                blocks.Add(new MdBlock(level, ParseInline(heading.Groups["text"].Value)));
                continue;
            }

            var bullet = BulletPattern.Match(line);
            if (bullet.Success && !line.StartsWith("**"))
            {
                Flush();
                kind = MdBlockKind.Bullet;
                lines.Add(bullet.Groups["text"].Value.Trim());
                continue;
            }

            if (line.StartsWith('>'))
            {
                string text = line[1..].Trim();
                if (kind != MdBlockKind.Quote)
                {
                    Flush();
                    kind = MdBlockKind.Quote;
                }

                if (text.Length > 0)
                {
                    lines.Add(text);
                }

                continue;
            }

            if (NumberedPattern.IsMatch(line))
            {
                Flush();
                kind = MdBlockKind.Paragraph;
                lines.Add(line);
                continue;
            }

            // A continuation line joins whatever block is open (paragraph, bullet, or a quote's lazy continuation).
            kind ??= MdBlockKind.Paragraph;
            lines.Add(line);
        }

        Flush();
        return blocks;
    }

    /// <summary>The whole of <paramref name="text"/> as one verbatim block (<c>LICENSE</c> is plain text, not markdown).</summary>
    public static IReadOnlyList<MdBlock> Preformatted(string text)
    {
        string normalized = text.Replace("\r\n", "\n").TrimEnd('\n', ' ');
        return normalized.Length == 0 ? [] : [new MdBlock(MdBlockKind.Preformatted, [new MdRun(normalized, MdRunStyle.Plain)])];
    }

    public static IReadOnlyList<MdRun> ParseInline(string text) => ParseInline(text, InlinePattern);

    private static IReadOnlyList<MdRun> ParseInline(string text, Regex pattern)
    {
        var runs = new List<MdRun>();
        int lastEnd = 0;
        foreach (Match m in pattern.Matches(text))
        {
            if (m.Index > lastEnd)
            {
                runs.Add(new MdRun(text[lastEnd..m.Index], MdRunStyle.Plain));
            }

            if (m.Groups["bold"].Success)
            {
                runs.Add(new MdRun(m.Groups["bold"].Value, MdRunStyle.Bold));
            }
            else if (m.Groups["code"].Success)
            {
                runs.Add(new MdRun(m.Groups["code"].Value, MdRunStyle.Code));
            }
            else if (m.Groups["ltext"].Success)
            {
                runs.Add(new MdRun(m.Groups["ltext"].Value.Replace("`", ""), MdRunStyle.Link, m.Groups["ltarget"].Value));
            }
            else if (m.Groups["auto"].Success)
            {
                runs.Add(new MdRun(m.Groups["auto"].Value, MdRunStyle.Link, m.Groups["auto"].Value));
            }
            else if (m.Groups["auto2"].Success)
            {
                runs.Add(new MdRun(m.Groups["auto2"].Value, MdRunStyle.Link, m.Groups["auto2"].Value));
            }
            else if (m.Groups["under"].Success)
            {
                runs.Add(new MdRun(m.Groups["under"].Value, MdRunStyle.Underline));
            }
            else
            {
                string italic = m.Groups["ital"].Success ? m.Groups["ital"].Value : m.Groups["ital2"].Value;
                runs.Add(new MdRun(italic, MdRunStyle.Italic));
            }

            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
        {
            runs.Add(new MdRun(text[lastEnd..], MdRunStyle.Plain));
        }

        return runs;
    }
}
