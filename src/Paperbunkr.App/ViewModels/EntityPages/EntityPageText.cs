using System;
using System.Linq;
using System.Text;

namespace Paperbunkr.App.ViewModels.EntityPages;

/// <summary>Wording helpers for the entity pages (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §3, §4.3, §5.3).</summary>
public static class EntityPageText
{
    /// <summary>"1 appearance", "32 appearances".</summary>
    public static string Count(int count, string one, string many) => $"{count:N0} {(count == 1 ? one : many)}";

    /// <summary>Joins the non-empty parts with " · " ("2013 · 1 issue", never "2013 · · 1 issue").</summary>
    public static string JoinDetail(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>A credit role as people read it: "CoverArtist" → "Cover artist", "Penciller" stays, blank → "Other".</summary>
    public static string RoleName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Other";
        string value = raw.Trim();
        if (value.Contains(' ')) return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
        var words = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (index > 0 && char.IsUpper(c) && !char.IsUpper(value[index - 1])) words.Append(' ');
            words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return words.ToString();
    }

    /// <summary>"2009–2026", "2009", or empty when no year is known.</summary>
    public static string YearSpan(int? first, int? last) => (first, last) switch
    {
        (int a, int b) when a != b => $"{a}–{b}",
        (int a, _) => a.ToString(),
        (null, int b) => b.ToString(),
        _ => string.Empty,
    };
}
