using System.Text;
using Paperbunkr.Data.Entities;

namespace ClusterLibraryManager.Templating;

/// <summary>
/// Evaluates a naming template string against one <see cref="Issue"/> (design doc §5).
///
/// <b>Multi-pass, not single-pass - found by actually testing this against CE's own shipped default
/// template.</b> CE's regex (`TemplateParser`'s own doc comment) excludes `{`/`}` from a group's
/// prefix/postfix, so it can never match a group that contains another `{...}` group nested inside
/// it in a single scan. Yet CE's own default template
/// (`{ ({&lt;month&gt;, }&lt;year&gt;)}`) has exactly that shape - an outer `( ... )` wrapper
/// containing an inner `{&lt;month&gt;, }` group, with `&lt;year&gt;` as bare trailing text after it,
/// still inside the outer parens. A single regex pass over that string matches only the *inner*
/// `{&lt;month&gt;, }` group; the outer `{`/`}` characters and the bare `&lt;year&gt;` are left as
/// literal text, which is clearly not the intended result. The only way CE's own default template
/// resolves sensibly is if the real algorithm is iterative: evaluate whatever groups the regex CAN
/// match in this pass, substitute their results back into the string, and re-scan - once the inner
/// group's braces are gone, the outer `{ (...)}` becomes a single flat, matchable group on the next
/// pass. This is what's implemented here (a bounded fixed-point loop), not literal recursion - CE's
/// own grammar doesn't need recursive brace-matching, just repeated flat passes.
/// </summary>
public static class TemplateEvaluator
{
    // Safety bound, not a real limit CE has - a legitimate template needs only as many passes as it
    // has levels of "apparent nesting" (CE's own default template needs exactly 2), so this is
    // generous headroom against a malformed template looping without actually being infinite.
    private const int MaxPasses = 20;

    public static string Evaluate(string template, Issue issue)
    {
        string current = template;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            string next = EvaluateOnePass(current, issue);
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                return next;
            }

            current = next;
        }

        return current;
    }

    /// <summary>Convenience overload for tests/callers that already have a specific single-pass parse
    /// result and just want that one level evaluated, with no further re-scanning.</summary>
    public static string EvaluateNodes(IReadOnlyList<TemplateNode> nodes, Issue issue)
    {
        var builder = new StringBuilder();
        foreach (TemplateNode node in nodes)
        {
            switch (node)
            {
                case LiteralNode literal:
                    builder.Append(literal.Text);
                    break;
                case TokenNode token:
                    AppendToken(builder, token, issue);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string EvaluateOnePass(string template, Issue issue) =>
        EvaluateNodes(TemplateParser.Parse(template), issue);

    /// <summary>
    /// Normal-group collapse rule (CE-verified): an empty resolved value makes the *whole* group -
    /// prefix and postfix included - disappear, not just the token itself, so e.g.
    /// `{ Vol.&lt;volume&gt;}` emits either `" Vol.5"` or nothing, never a bare `" Vol."`.
    ///
    /// Conditional/inversion groups (`?`/`!` name prefixes) are a scoped-down first pass, not CE's
    /// full regex-conditional-argument semantics - flagged rather than guessed at, per this project's
    /// own standing rule:
    /// - Conditional (`?`): same collapse-on-empty behavior as Normal. CE's own regex-conditional-arg
    ///   variant (a paren arg starting with `!` triggering a regex match against the field value)
    ///   isn't implemented.
    /// - Inversion (`!`): emits `prefix + postfix` (literal wrapper text only, never the field's own
    ///   value) when the resolved value is empty/falsy; emits nothing when the value is present.
    ///   Covers CE's "no args, only fires when the field result is empty" case; the yes/no-field
    ///   `(text)(!)`-for-the-No-case form is handled by <see cref="FieldResolvers"/>'s own `manga`/
    ///   `seriesComplete` resolvers directly, not by this group-kind mechanism.
    /// </summary>
    private static void AppendToken(StringBuilder builder, TokenNode token, Issue issue)
    {
        string? value = ResolveValue(token, issue);
        bool isEmpty = string.IsNullOrEmpty(value);

        switch (token.Kind)
        {
            case TokenGroupKind.Normal:
            case TokenGroupKind.Conditional:
                if (!isEmpty)
                {
                    builder.Append(token.Prefix).Append(value).Append(token.Postfix);
                }

                break;
            case TokenGroupKind.Inversion:
                if (isEmpty)
                {
                    builder.Append(token.Prefix).Append(token.Postfix);
                }

                break;
        }
    }

    private static string? ResolveValue(TokenNode token, Issue issue)
    {
        if (string.Equals(token.Name, "Custom", StringComparison.Ordinal))
        {
            string key = ExtractFirstParenSegment(token.Args);
            return FieldResolvers.ResolveCustom(issue, key);
        }

        if (FieldResolvers.ByName.TryGetValue(token.Name, out TokenFieldResolver? resolver))
        {
            return resolver(issue, token.Args);
        }

        throw new NotSupportedException(
            $"Naming template token '<{token.Name}>' is not supported yet - see FieldResolvers.ByName's own doc comment for exactly which CE tokens aren't implemented in this pass and why.");
    }

    private static string ExtractFirstParenSegment(string args)
    {
        int open = args.IndexOf('(');
        int close = args.IndexOf(')');
        return open >= 0 && close > open ? args[(open + 1)..close] : string.Empty;
    }
}
