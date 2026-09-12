using System.Text.RegularExpressions;

namespace ClusterLibraryManager.Templating;

/// <summary>
/// Parses a naming template string into an ordered list of <see cref="TemplateNode"/>s (design doc
/// §5). CE's own grammar root (`lobookmover.py:1211`, verified):
/// <code>{(?P&lt;prefix&gt;[^{}&lt;]*)&lt;(?P&lt;name&gt;[^\d\s(&gt;]*)(?P&lt;args&gt;\d*|(?:\([^){}]*\))*)&gt;(?P&lt;postfix&gt;[^{}]*)}</code>
/// Deliberately NOT a recursive-descent parser building a nested tree - CE's own regex explicitly
/// excludes `{`/`}` from `prefix`/`postfix`, meaning `{...}` groups never nest within each other in
/// CE's actual grammar. A flat left-to-right scan for non-overlapping matches, alternating literal
/// runs and token groups, is exactly as expressive as CE's real grammar - not a simplification of it.
/// Uses `[GeneratedRegex]` (compile-time source-generated, not a runtime `new Regex(...)`) per the
/// review pass that replaced an earlier "single big regex plus manual bookkeeping" sketch.
/// </summary>
public static partial class TemplateParser
{
    [GeneratedRegex(@"\{(?<prefix>[^{}<]*)<(?<name>[^\d\s(>]*)(?<args>\d*|(?:\([^){}]*\))*)>(?<postfix>[^{}]*)\}")]
    private static partial Regex TokenGroupRegex();

    public static IReadOnlyList<TemplateNode> Parse(string template)
    {
        var nodes = new List<TemplateNode>();
        int cursor = 0;

        foreach (Match match in TokenGroupRegex().Matches(template))
        {
            if (match.Index > cursor)
            {
                nodes.Add(new LiteralNode(template[cursor..match.Index]));
            }

            nodes.Add(ParseTokenGroup(match));
            cursor = match.Index + match.Length;
        }

        if (cursor < template.Length)
        {
            nodes.Add(new LiteralNode(template[cursor..]));
        }

        return nodes;
    }

    private static TokenNode ParseTokenGroup(Match match)
    {
        string prefix = match.Groups["prefix"].Value;
        string rawName = match.Groups["name"].Value;
        string args = match.Groups["args"].Value;
        string postfix = match.Groups["postfix"].Value;

        TokenGroupKind kind = TokenGroupKind.Normal;
        string name = rawName;
        if (rawName.StartsWith('?'))
        {
            kind = TokenGroupKind.Conditional;
            name = rawName[1..];
        }
        else if (rawName.StartsWith('!'))
        {
            kind = TokenGroupKind.Inversion;
            name = rawName[1..];
        }

        return new TokenNode(prefix, name, args, postfix, kind);
    }
}
