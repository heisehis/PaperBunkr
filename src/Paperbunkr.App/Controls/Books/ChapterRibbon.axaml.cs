using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls.Books;

/// <summary>The chapter ribbon: <see cref="Models.RibbonSegment"/>s in one row, sized by weight, with an overline caption above
/// (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2). <see cref="OpenCommand"/> receives the segment's Tag.</summary>
public partial class ChapterRibbon : UserControl
{
    public static readonly StyledProperty<IEnumerable?> SegmentsProperty =
        AvaloniaProperty.Register<ChapterRibbon, IEnumerable?>(nameof(Segments));

    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<ChapterRibbon, string?>(nameof(Caption));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<ChapterRibbon, ICommand?>(nameof(OpenCommand));

    public ChapterRibbon() => InitializeComponent();

    public IEnumerable? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }
}
