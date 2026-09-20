using System.Text;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Naming;

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

    /// <summary>Peels the LAST parenthesized segment (containing no nested '(') off an args string, for
    /// the Conditional/Inversion groups' own extra trailing arg - CE's exact mechanism
    /// (`lobookmover.py:1355`/`1366`, verified): <c>re.search(r"(\([^(]*\))$", args)</c>. Whatever
    /// remains before it is passed on as the field's own regular args.</summary>
    private static readonly Regex TrailingArgRegex = new(@"\(([^(]*)\)$", RegexOptions.Compiled);

    /// <summary><paramref name="context"/> is null for a caller with no batch context (e.g. most unit
    /// tests evaluating a single issue in isolation) - only the series-level/counter tokens are
    /// affected, resolving to empty in that case rather than throwing.</summary>
    public static string Evaluate(string template, Issue issue, TemplateContext? context = null)
    {
        string current = template;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            string next = EvaluateOnePass(current, issue, context);
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
    public static string EvaluateNodes(IReadOnlyList<TemplateNode> nodes, Issue issue, TemplateContext? context = null)
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
                    AppendToken(builder, token, issue, context);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string EvaluateOnePass(string template, Issue issue, TemplateContext? context) =>
        EvaluateNodes(TemplateParser.Parse(template), issue, context);

    /// <summary>
    /// Ported verbatim from CE's real <c>insert_field</c> (`lobookmover.py:1337`, re-verified once the
    /// extracted plugin source became available again this session - a prior pass had Conditional
    /// collapsing like Normal and Inversion only handling the no-args case, both honestly flagged as
    /// scoped-down guesses at the time, now corrected):
    ///
    /// - <b>Normal</b>: an empty resolved value makes the *whole* group - prefix and postfix included -
    ///   disappear, not just the token itself, so e.g. `{ Vol.&lt;volume&gt;}` emits either `" Vol.5"`
    ///   or nothing, never a bare `" Vol."`.
    /// - <b>Conditional</b> (`?`): the group's LAST paren segment is peeled off as a comparison arg (the
    ///   rest are the field's own args) - never emits the resolved value itself, only `prefix+postfix`,
    ///   and only when the comparison matches: a plain-text arg compares by exact equality; a `!`-led
    ///   arg is a regex (`re.match` semantics - anchored at the start, not required to consume the
    ///   whole string), matched against the resolved value; a bare `!` (empty pattern) always resolves
    ///   to nothing at all. A conditional with no trailing arg at all is a malformed template - CE
    ///   leaves the raw text in place; this throws instead (see <see cref="FieldResolvers"/>'s own
    ///   "unsupported token" doc note for why Paperbunkr prefers a loud error to silent passthrough).
    /// - <b>Inversion</b> (`!`): same trailing-arg peel. No arg at all: emits `prefix+postfix` only when
    ///   the resolved value is empty (unchanged from before). A plain-text arg: emits when the value is
    ///   NOT equal to it. A `!`-led regex arg: emits when the value does NOT match.
    /// </summary>
    private static void AppendToken(StringBuilder builder, TokenNode token, Issue issue, TemplateContext? context)
    {
        switch (token.Kind)
        {
            case TokenGroupKind.Normal:
            {
                string? value = ResolveValue(token.Name, token.Args, issue, context);
                if (!string.IsNullOrEmpty(value))
                {
                    builder.Append(token.Prefix).Append(value).Append(token.Postfix);
                }

                break;
            }

            case TokenGroupKind.Conditional:
            {
                (string fieldArgs, string? conditionArg) = SplitTrailingArg(token.Args);
                if (conditionArg is null)
                {
                    throw new NotSupportedException(
                        $"Conditional naming template token '<?{token.Name}{token.Args}>' has no trailing (condition) argument.");
                }

                string value = ResolveValue(token.Name, fieldArgs, issue, context) ?? string.Empty;

                if (conditionArg.StartsWith('!'))
                {
                    string pattern = conditionArg[1..];
                    if (pattern.Length > 0 && PythonReMatch(pattern, value))
                    {
                        builder.Append(token.Prefix).Append(token.Postfix);
                    }
                }
                else if (string.Equals(value, conditionArg, StringComparison.Ordinal))
                {
                    builder.Append(token.Prefix).Append(token.Postfix);
                }

                break;
            }

            case TokenGroupKind.Inversion:
            {
                (string fieldArgs, string? inversionArg) = SplitTrailingArg(token.Args);
                string value = ResolveValue(token.Name, fieldArgs, issue, context) ?? string.Empty;

                bool emit;
                if (string.IsNullOrEmpty(inversionArg))
                {
                    emit = value.Length == 0;
                }
                else if (inversionArg.StartsWith('!'))
                {
                    emit = !PythonReMatch(inversionArg[1..], value);
                }
                else
                {
                    emit = !string.Equals(value, inversionArg, StringComparison.Ordinal);
                }

                if (emit)
                {
                    builder.Append(token.Prefix).Append(token.Postfix);
                }

                break;
            }
        }
    }

    /// <summary>Peels the trailing `(...)` segment off <paramref name="args"/> for a Conditional/
    /// Inversion group - see <see cref="TrailingArgRegex"/>. No parens at all (an empty args string, the
    /// common "bare `!name`"/`` case) returns a null trailing arg, same as CE's own `if args:` guard
    /// leaving its args/condition variables untouched.</summary>
    private static (string FieldArgs, string? TrailingArg) SplitTrailingArg(string args)
    {
        if (args.Length == 0)
        {
            return (args, null);
        }

        Match match = TrailingArgRegex.Match(args);
        return match.Success ? (args[..^match.Length], match.Groups[1].Value) : (args, null);
    }

    /// <summary>Python's <c>re.match</c> anchors only at the START of the string (unlike a bare .NET
    /// <c>Regex.IsMatch</c>, which searches anywhere) - it doesn't need to consume the whole string,
    /// just begin matching at index 0.</summary>
    private static bool PythonReMatch(string pattern, string value)
    {
        Match match = Regex.Match(value, pattern);
        return match.Success && match.Index == 0;
    }

    private static string? ResolveValue(string name, string args, Issue issue, TemplateContext? context)
    {
        if (string.Equals(name, "Custom", StringComparison.Ordinal))
        {
            string key = ExtractFirstParenSegment(args);
            return FieldResolvers.ResolveCustom(issue, key);
        }

        if (FieldResolvers.ByName.TryGetValue(name, out TokenFieldResolver? resolver))
        {
            return resolver(issue, args, context);
        }

        throw new NotSupportedException(
            $"Naming template token '<{name}>' is not supported yet - see FieldResolvers.ByName's own doc comment for exactly which CE tokens aren't implemented in this pass and why.");
    }

    private static string ExtractFirstParenSegment(string args)
    {
        int open = args.IndexOf('(');
        int close = args.IndexOf(')');
        return open >= 0 && close > open ? args[(open + 1)..close] : string.Empty;
    }
}
