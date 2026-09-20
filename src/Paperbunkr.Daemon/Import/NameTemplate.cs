using System.Globalization;
using System.Text;

namespace Paperbunkr.Daemon.Import;

/// <summary>
/// File/folder naming for imports, in ComicRack CE's template syntax - a port of CE's <c>ExtendedStringFormater</c>
/// (<c>_reference/ComicRackCE/cYo.Common/Text/ExtendedStringFormater.cs</c>), verified against it rather than reinvented:
/// <list type="bullet">
///   <item><c>{token}</c> is replaced by its value; <c>{token:000}</c> applies a numeric format (CE does this with <c>string.Format("{0:000}")</c>).</item>
///   <item><c>[ ... ]</c> is an optional group: the whole group, literal text included, disappears if <b>any</b> token inside it is empty.</item>
///   <item><c>\</c> escapes the next character.</item>
///   <item>The result is trimmed.</item>
/// </list>
/// Deviations, all deliberate: the extra tokens <c>{publisher}</c> and <c>{volumeyear}</c> (CE has neither); a numeric format is applied only when
/// the value is a whole number, so an issue "1.5" is never rounded to "002"; CE's <c>$function&lt;...&gt;</c> calls are not supported; and because the
/// result is a path, <c>/</c> separates folders and every segment is made filesystem-safe.
/// </summary>
public static class NameTemplate
{
    public static readonly string[] KnownTokens =
    {
        "series", "title", "volume", "volumeyear", "number", "year", "month", "day", "format", "publisher", "filename",
    };

    /// <summary>Formats a template (CE semantics) with the given values; a token the lookup returns null/empty for counts as empty.</summary>
    public static string Format(string template, Func<string, string?> getValue)
    {
        return Format(template, getValue, out _).Trim();
    }

    /// <summary>The template's error, or <c>null</c> when it is usable: balanced brackets/braces and only known tokens.</summary>
    public static string? Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "The naming template can't be empty.";
        }

        int square = 0, brace = 0;
        var token = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (brace > 0)
            {
                if (c == '{') return "Braces can't be nested.";
                if (c == '}')
                {
                    brace--;
                    var name = token.ToString().Split(':')[0].Trim().ToLowerInvariant();
                    if (!KnownTokens.Contains(name))
                    {
                        return $"Unknown token {{{name}}}. Known: {string.Join(", ", KnownTokens.Select(t => "{" + t + "}"))}.";
                    }

                    token.Clear();
                }
                else
                {
                    token.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '{': brace++; break;
                case '}': return "There is a } with no matching {.";
                case '[': square++; break;
                case ']':
                    if (--square < 0) return "There is a ] with no matching [.";
                    break;
            }
        }

        if (brace > 0) return "A { is never closed.";
        if (square > 0) return "A [ is never closed.";
        return null;
    }

    /// <summary>
    /// Formats the template and turns the result into a relative path: <c>/</c> (or <c>\</c> that wasn't an escape) separate folders, each segment is
    /// sanitized, and empty segments (an empty <c>{publisher}</c> folder, say) are dropped so no folder is ever named "".
    /// </summary>
    public static string FormatPath(string template, Func<string, string?> getValue, string extension)
    {
        var text = Format(template, getValue);
        var segments = text.Split('/', StringSplitOptions.None)
            .Select(SanitizeSegment)
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add("Unnamed");
        }

        segments[^1] += extension.StartsWith('.') || extension.Length == 0 ? extension : "." + extension;
        return string.Join('/', segments);
    }

    /// <summary>Makes one path segment safe on Windows: illegal characters replaced, no trailing dots/spaces, no reserved device names, bounded length.</summary>
    public static string SanitizeSegment(string segment)
    {
        var sb = new StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            sb.Append(c < ' ' || "\\:*?\"<>|".Contains(c) ? (c == ':' ? '-' : '_') : c);
        }

        var text = sb.ToString().Trim().TrimEnd('.', ' ');
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var stem = text.Split('.')[0].ToUpperInvariant();
        if (Reserved.Contains(stem))
        {
            text = "_" + text;
        }

        return text.Length > 120 ? text[..120].TrimEnd('.', ' ') : text;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string Format(string template, Func<string, string?> getValue, out bool success)
    {
        var sb = new StringBuilder();
        success = true;

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            switch (c)
            {
                case '[':
                {
                    var group = GetPart(template, ref i, '[', ']');
                    var value = Format(group, getValue, out bool groupSuccess);
                    if (groupSuccess)
                    {
                        sb.Append(value);
                    }

                    break;
                }

                case '{':
                {
                    var value = FormatToken(GetPart(template, ref i, '{', '}'), getValue);
                    success &= !string.IsNullOrEmpty(value);
                    sb.Append(value);
                    break;
                }

                case '\\':
                    if (i + 1 < template.Length)
                    {
                        sb.Append(template[++i]);
                    }

                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string FormatToken(string token, Func<string, string?> getValue)
    {
        var parts = token.Split(':');
        var value = getValue(parts[0].Trim().ToLowerInvariant());
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A numeric format ("000") applies only to whole numbers: "5" -> "005", but "1.5" and "Annual 1" stay as they are.
        if (parts.Length > 1 && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long whole))
        {
            try
            {
                return whole.ToString(parts[1], CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return value;
            }
        }

        return value;
    }

    /// <summary>The text between an opening character and its matching close (nesting-aware), advancing <paramref name="index"/> to the close.</summary>
    private static string GetPart(string text, ref int index, char open, char close)
    {
        int depth = 0, start = index + 1, end = text.Length;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\\')
            {
                index += 2;
                continue;
            }

            if (c == open)
            {
                if (depth++ == 0)
                {
                    start = index + 1;
                }
            }
            else if (c == close && --depth == 0)
            {
                end = index;
                break;
            }

            index++;
        }

        return start <= end && end <= text.Length ? text[start..end] : string.Empty;
    }
}
