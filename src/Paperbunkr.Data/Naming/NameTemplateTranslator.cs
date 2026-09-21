using System.Text;

namespace Paperbunkr.Data.Naming;

/// <summary>Outcome of <see cref="NameTemplateTranslator.Translate"/>: the translated template, or why it could not be translated.</summary>
public sealed record TranslationResult(bool Success, string? Template, string? Error)
{
    public static TranslationResult Ok(string template) => new(true, template, null);

    public static TranslationResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Converts a template written in the acquisition importer's original grammar (CE's <c>ExtendedStringFormater</c>: <c>{token}</c>,
/// <c>{token:000}</c>, <c>[optional group]</c>, <c>\</c> escapes) into the Organizer grammar (<c>{prefix&lt;name(args)&gt;postfix}</c>) that
/// <see cref="TemplateEvaluator"/> understands, so both features share one engine
/// (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 5).
/// <para>
/// The translation is deliberately conservative. The two grammars are not equivalent in general, and a wrong guess would silently rename files, so
/// anything that cannot be expressed exactly is <b>refused</b> with a reason instead of approximated: an optional group holding more than one token
/// (the Organizer grammar drops a group per token, not per bracket), nested groups, a numeric format that is not plain zeros, a numeric format on a
/// token the engine cannot pad, a literal <c>{</c>, <c>}</c> or (in a prefix) <c>&lt;</c>, and unknown tokens. It never throws.
/// </para>
/// </summary>
public static class NameTemplateTranslator
{
    /// <summary>Import token -> (Organizer token name, whether the token accepts a zero-pad width, width used when the import template gives no format).</summary>
    private static readonly IReadOnlyDictionary<string, (string Name, bool CanPad, int DefaultWidth)> Tokens =
        new Dictionary<string, (string, bool, int)>(StringComparer.Ordinal)
        {
            ["series"] = ("series", false, 0),
            ["title"] = ("title", false, 0),
            ["volume"] = ("volume", false, 0),
            ["volumeyear"] = ("volumeyear", false, 0),
            ["number"] = ("number", true, 0),
            ["year"] = ("year", true, 0),
            ["month"] = ("month#", true, 2),     // the import grammar's {month} is the two-digit number, not the month name
            ["day"] = ("Day", true, 2),
            ["format"] = ("format", false, 0),
            ["publisher"] = ("publisher", false, 0),
            ["filename"] = ("filename", false, 0),
        };

    public static TranslationResult Translate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return TranslationResult.Fail("The template is empty.");
        }

        var output = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 >= template.Length)
                    {
                        return TranslationResult.Fail("The template ends with a lone backslash.");
                    }

                    if (!AppendLiteral(output, template[++i], LiteralPosition.Top, out string? escapeError))
                    {
                        return TranslationResult.Fail(escapeError!);
                    }

                    break;

                case '{':
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        return TranslationResult.Fail("A { is never closed.");
                    }

                    var token = TranslateToken(template[(i + 1)..close], out string? tokenError);
                    if (token is null)
                    {
                        return TranslationResult.Fail(tokenError!);
                    }

                    output.Append("{<").Append(token).Append(">}");
                    i = close;
                    break;
                }

                case '[':
                {
                    int close = FindGroupEnd(template, i, out string? groupError);
                    if (close < 0)
                    {
                        return TranslationResult.Fail(groupError!);
                    }

                    var group = TranslateGroup(template[(i + 1)..close], out string? error);
                    if (group is null)
                    {
                        return TranslationResult.Fail(error!);
                    }

                    output.Append(group);
                    i = close;
                    break;
                }

                case ']':
                    return TranslationResult.Fail("There is a ] with no matching [.");
                case '}':
                    return TranslationResult.Fail("There is a } with no matching {.");

                default:
                    if (!AppendLiteral(output, c, LiteralPosition.Top, out string? literalError))
                    {
                        return TranslationResult.Fail(literalError!);
                    }

                    break;
            }
        }

        return TranslationResult.Ok(output.ToString());
    }

    private enum LiteralPosition
    {
        Top,
        Prefix,
        Postfix,
    }

    /// <summary>Appends one literal character where the Organizer grammar can carry it; <c>{</c> and <c>}</c> never can, and <c>&lt;</c> cannot sit in a group's prefix.</summary>
    private static bool AppendLiteral(StringBuilder builder, char c, LiteralPosition position, out string? error)
    {
        error = null;
        if (c is '{' or '}' || (c == '<' && position == LiteralPosition.Prefix))
        {
            error = $"The character '{c}' can't be used as literal text in this template.";
            return false;
        }

        builder.Append(c);
        return true;
    }

    /// <summary>Index of the ']' closing the group opened at <paramref name="open"/>, honouring escapes; nested groups are refused.</summary>
    private static int FindGroupEnd(string template, int open, out string? error)
    {
        error = null;
        for (int i = open + 1; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                error = "Nested optional groups ([ ... [ ... ] ... ]) can't be converted.";
                return -1;
            }

            if (c == ']')
            {
                return i;
            }
        }

        error = "A [ is never closed.";
        return -1;
    }

    /// <summary>One optional group. Exactly one token becomes <c>{prefix&lt;name&gt;postfix}</c>; no token is plain text; more than one is refused.</summary>
    private static string? TranslateGroup(string body, out string? error)
    {
        error = null;
        var prefix = new StringBuilder();
        var postfix = new StringBuilder();
        string? token = null;

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            StringBuilder target = token is null ? prefix : postfix;
            var position = token is null ? LiteralPosition.Prefix : LiteralPosition.Postfix;

            if (c == '\\')
            {
                if (i + 1 >= body.Length)
                {
                    error = "An optional group ends with a lone backslash.";
                    return null;
                }

                if (!AppendLiteral(target, body[++i], position, out error))
                {
                    return null;
                }
            }
            else if (c == '{')
            {
                int close = body.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = "A { is never closed.";
                    return null;
                }

                if (token is not null)
                {
                    error = "An optional group with more than one token can't be converted (the new grammar drops a group per token, not per bracket).";
                    return null;
                }

                token = TranslateToken(body[(i + 1)..close], out error);
                if (token is null)
                {
                    return null;
                }

                i = close;
            }
            else if (!AppendLiteral(target, c, position, out error))
            {
                return null;
            }
        }

        // No token: the group is always shown, so it is just its text.
        return token is null ? prefix.ToString() : "{" + prefix + "<" + token + ">" + postfix + "}";
    }

    /// <summary>The Organizer token expression (name plus optional pad width) for the inside of one <c>{...}</c>.</summary>
    private static string? TranslateToken(string inner, out string? error)
    {
        error = null;
        var parts = inner.Split(':');
        string name = parts[0].Trim().ToLowerInvariant();

        if (!Tokens.TryGetValue(name, out var target))
        {
            error = $"Unknown token {{{name}}}.";
            return null;
        }

        if (parts.Length > 2)
        {
            error = $"Too many ':' in {{{inner}}}.";
            return null;
        }

        int width = target.DefaultWidth;
        if (parts.Length == 2)
        {
            string format = parts[1];
            if (format.Length == 0 || format.Any(ch => ch != '0'))
            {
                error = $"The format '{format}' in {{{inner}}} can't be converted (only zero padding such as 000 is supported).";
                return null;
            }

            if (!target.CanPad)
            {
                error = $"{{{name}}} can't be zero-padded in the new template grammar.";
                return null;
            }

            width = format.Length;
        }

        return width > 0 ? target.Name + width : target.Name;
    }
}
