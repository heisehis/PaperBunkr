using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Paperbunkr.App.Views;

/// <summary>The Library's collection banner (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md). DataContext is the Library view model.</summary>
public partial class CollectionBanner : UserControl
{
    public CollectionBanner()
    {
        InitializeComponent();
    }
}

/// <summary>"more" / "less" for the banner's description toggle.</summary>
public sealed class BannerMoreLessConverter : IValueConverter
{
    public static readonly BannerMoreLessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Show less" : "Show more";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
