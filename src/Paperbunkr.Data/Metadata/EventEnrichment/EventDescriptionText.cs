using System.Net;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

public static class EventDescriptionText
{
    public static string Plain(string? text)
    {
        var s = Regex.Replace(text ?? "", @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<br\s*/?>|</(?:p|h[1-6]|li|div)>", "\n", RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));
        return Regex.Replace(s.Replace("\r", ""), @"\n[ \t]*\n[ \t]*\n+", "\n\n").Trim();
    }

    public static string? Overview(string? text)
    {
        var lines = Plain(text).Split('\n');
        var result = new List<string>(); bool bibliography = false;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (Regex.IsMatch(line, @"^(?:collected editions?|non[- .]?u\.?s\.? editions?|related items|reprints?|external links|references|bibliography)\s*:?(?:\s*)$", RegexOptions.IgnoreCase))
            { bibliography = true; continue; }
            if (Regex.IsMatch(line, @"^(?:plot(?: summary)?|synopsis|premise|overview)\s*:?(?:\s*)$", RegexOptions.IgnoreCase))
            { bibliography = false; continue; }
            if (bibliography || Regex.IsMatch(line, @"^(?:summary|short summary describing this arc\.?|no (?:description|synopsis|summary)(?: available)?\.?)$", RegexOptions.IgnoreCase)) continue;
            result.Add(line);
        }
        string value = string.Join('\n', result).Trim();
        // Short title/link lists in unrecognized languages are not automatically treated as prose.
        bool prose = result.Any(line => Regex.IsMatch(line, @"[.!?。！？]$") || Regex.Matches(line, @"\S+").Count >= 8);
        return value.Length == 0 || !prose ? null : value;
    }
}
