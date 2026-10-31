using System.Globalization;
using Avalonia.Data.Converters;

namespace Paperbunkr.App.Views;

/// <summary>Small text converters for the Book Detail jacket (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §4).</summary>
public static class BookDetailLabels
{
    /// <summary>The synopsis toggle: "Show more" while collapsed, "Show less" once expanded.</summary>
    public static readonly IValueConverter SynopsisToggle =
        new FuncValueConverter<bool, string>(expanded => expanded ? "Show less" : "Show more");

    /// <summary>Overline text in capitals (Avalonia has no text-transform).</summary>
    public static readonly IValueConverter Upper =
        new FuncValueConverter<string?, string>(text => (text ?? string.Empty).ToUpper(CultureInfo.CurrentCulture));
}
