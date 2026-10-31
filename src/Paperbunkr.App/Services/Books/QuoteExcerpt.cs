using System.Text;

namespace Paperbunkr.App.Services.Books;

/// <summary>Cuts the paragraph at a reader's saved position into the Books spread's pull quote
/// (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §6.1): whitespace collapsed, cut at a word boundary, an ellipsis when cut.</summary>
public static class QuoteExcerpt
{
    public const int DefaultMaxLength = 160;

    /// <summary>The quote for <paramref name="text"/>, or null when there is nothing to quote.</summary>
    public static string? Cut(string? text, int maxLength = DefaultMaxLength)
    {
        string collapsed = Collapse(text);
        if (collapsed.Length == 0)
        {
            return null;
        }

        if (collapsed.Length <= maxLength)
        {
            return collapsed;
        }

        // Last space at or before the limit; a single word longer than the limit is cut mid-word rather than dropped.
        int cut = collapsed.LastIndexOf(' ', maxLength);
        string head = (cut > 0 ? collapsed[..cut] : collapsed[..maxLength]).TrimEnd(' ', ',', ';', ':', '-', '—', '–');
        return head + "…";
    }

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
