using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>A persisted highlight's range, local to one <see cref="ParagraphView"/>'s own <see cref="ParagraphView.Text"/> (0-based) - translating to/from the Book domain's chapter-global offset scheme is the caller's job.</summary>
public readonly record struct ParagraphHighlight(int Start, int End, BookHighlightColor Color);

/// <summary>
/// Routed (bubbling), not a plain CLR event - <see cref="ParagraphView"/> instances are templated
/// per-paragraph inside an <c>ItemsControl</c>, so a single handler needs to catch this from any of
/// them without subscribing to each instance individually. <c>BookReaderScreen.axaml.cs</c> adds one
/// handler for <see cref="ParagraphView.SelectionCompletedEvent"/> at the containing panel instead.
/// </summary>
public sealed class ParagraphSelectionEventArgs : RoutedEventArgs
{
    public ParagraphSelectionEventArgs(RoutedEvent routedEvent, int start, int end, Rect bounds) : base(routedEvent)
    {
        Start = start;
        End = end;
        Bounds = bounds;
    }

    /// <summary>Local (0-based within this paragraph's Text) offset, inclusive.</summary>
    public int Start { get; }

    /// <summary>Local offset, exclusive.</summary>
    public int End { get; }

    /// <summary>Selection's bounding rect, in this control's own coordinate space - used to anchor the color-palette popup.</summary>
    public Rect Bounds { get; }
}

/// <summary>Routed for the same reason as <see cref="ParagraphSelectionEventArgs"/>.</summary>
public sealed class ParagraphHighlightTappedEventArgs : RoutedEventArgs
{
    public ParagraphHighlightTappedEventArgs(RoutedEvent routedEvent, ParagraphHighlight highlight, Rect bounds) : base(routedEvent)
    {
        Highlight = highlight;
        Bounds = bounds;
    }

    public ParagraphHighlight Highlight { get; }

    public Rect Bounds { get; }
}

/// <summary>
/// Custom text-rendering control backing docs/superpowers/specs/2026-09-01-books-reader-ergonomics-
/// and-annotations-design.md Component 2 - replaces the reflow reader's plain per-paragraph
/// <c>TextBlock</c>. Built directly on <c>Avalonia.Media.TextFormatting.TextLayout</c> (the layer
/// <c>SelectableTextBlock</c>/<c>TextBox</c> are themselves built on) rather than any built-in text
/// control, because word spacing needs manual per-word glyph-run placement that no built-in control
/// exposes - verified via the design's Step 3 spike against the real API surface, not assumed:
/// <see cref="TextRunProperties"/> has no spacing-related member at all, and <see cref="TextLayout"/>'s
/// own <c>letterSpacing</c> constructor parameter is a single uniform paragraph-level value (used here
/// for <see cref="BookReaderSettings.CharacterSpacing"/>, not per-word gaps).
///
/// Mechanism: <see cref="Text"/> is split into one <see cref="TextCharacters"/> run per word via
/// <see cref="WordSplitTextSource"/>. The spike confirmed the shaper keeps these as separate
/// <c>TextLine.TextRuns</c> entries even though every run shares identical <see cref="TextRunProperties"/>
/// (i.e. it does not silently coalesce same-formatting adjacent runs). <see cref="Render"/> then draws
/// those runs itself with a manually-tracked cumulative X cursor, adding
/// <see cref="BookReaderSettings.WordSpacing"/> pixels after any run ending in whitespace - this is
/// what actually produces the visual gap, since <c>TextLine.Draw</c> would use the shaper's own
/// un-gapped internal layout.
///
/// Hit-testing (pointer -> character offset, for drag-selection) and highlight-range rendering both
/// reuse <see cref="TextLayout"/>'s own <c>HitTestPoint</c>/<c>HitTestTextRange</c> rather than
/// hand-rolling glyph-level math: <see cref="_runSpans"/> is the only custom bookkeeping this control
/// owns, a per-run table of (original layout X, visual/gapped X) built once per layout pass, consulted
/// backward to de-gap a pointer position before calling <c>HitTestPoint</c>, and forward to shift
/// <c>HitTestTextRange</c>'s rects to their correct visual position for highlight fills.
///
/// Known v1 simplification, called out rather than silently dropped: <see cref="TranslateRectForward"/>
/// shifts a highlight rect by the gap accumulated at its start only, without also inflating its width
/// for word-gaps that fall entirely inside the highlighted range - correct for the common case (a
/// highlight within one or two words of a line) but can under-fill by a few pixels per word for a
/// highlight spanning many words on one line. Revisit if that proves visually noticeable.
/// </summary>
public sealed class ParagraphView : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ParagraphView, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<BookReaderSettings?> SettingsProperty =
        AvaloniaProperty.Register<ParagraphView, BookReaderSettings?>(nameof(Settings));

    public static readonly StyledProperty<IReadOnlyList<ParagraphHighlight>> HighlightsProperty =
        AvaloniaProperty.Register<ParagraphView, IReadOnlyList<ParagraphHighlight>>(nameof(Highlights), Array.Empty<ParagraphHighlight>());

    static ParagraphView()
    {
        AffectsMeasure<ParagraphView>(TextProperty, SettingsProperty);
        AffectsRender<ParagraphView>(TextProperty, SettingsProperty, HighlightsProperty, BoundsProperty);
    }

    public ParagraphView()
    {
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BookReaderSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public IReadOnlyList<ParagraphHighlight> Highlights
    {
        get => GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    public static readonly RoutedEvent<ParagraphSelectionEventArgs> SelectionCompletedEvent =
        RoutedEvent.Register<ParagraphView, ParagraphSelectionEventArgs>(nameof(SelectionCompleted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<ParagraphHighlightTappedEventArgs> HighlightTappedEvent =
        RoutedEvent.Register<ParagraphView, ParagraphHighlightTappedEventArgs>(nameof(HighlightTapped), RoutingStrategies.Bubble);

    public event EventHandler<ParagraphSelectionEventArgs> SelectionCompleted
    {
        add => AddHandler(SelectionCompletedEvent, value);
        remove => RemoveHandler(SelectionCompletedEvent, value);
    }

    public event EventHandler<ParagraphHighlightTappedEventArgs> HighlightTapped
    {
        add => AddHandler(HighlightTappedEvent, value);
        remove => RemoveHandler(HighlightTappedEvent, value);
    }

    private static readonly IReadOnlyDictionary<BookHighlightColor, IBrush> HighlightFills = new Dictionary<BookHighlightColor, IBrush>
    {
        [BookHighlightColor.Yellow] = new SolidColorBrush(Color.Parse("#66FFD54F")),
        [BookHighlightColor.Green] = new SolidColorBrush(Color.Parse("#6681C784")),
        [BookHighlightColor.Blue] = new SolidColorBrush(Color.Parse("#6664B5F6")),
        [BookHighlightColor.Pink] = new SolidColorBrush(Color.Parse("#66F06292")),
    };

    private static readonly IBrush SelectionFill = new SolidColorBrush(Color.Parse("#5590CAF9"));

    private readonly record struct RunSpan(int LineIndex, int CharStart, int CharEnd, double OriginalX, double VisualX, double Width);

    private TextLayout? _layout;
    private readonly List<RunSpan> _runSpans = new();
    private readonly List<double> _lineYOrigins = new();
    private double _layoutWidth = -1;

    private int? _dragAnchor;
    private int? _dragCurrent;
    private bool _isDragging;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SettingsProperty)
        {
            if (change.OldValue is BookReaderSettings oldSettings)
            {
                oldSettings.PropertyChanged -= OnSettingsChanged;
            }

            if (change.NewValue is BookReaderSettings newSettings)
            {
                newSettings.PropertyChanged += OnSettingsChanged;
            }

            InvalidateLayoutCache();
        }
        else if (change.Property == TextProperty)
        {
            InvalidateLayoutCache();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateLayoutCache();
        InvalidateMeasure();
    }

    private void InvalidateLayoutCache()
    {
        _layout?.Dispose();
        _layout = null;
        _layoutWidth = -1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(availableSize.Width);
        return new Size(availableSize.Width, _layout?.Height ?? 0);
    }

    private void EnsureLayout(double maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);
        if (_layout is not null && Math.Abs(_layoutWidth - maxWidth) < 0.5)
        {
            return;
        }

        InvalidateLayoutCache();
        _layoutWidth = maxWidth;

        var settings = Settings;
        var typeface = new Typeface(settings?.ResolvedFontFamily ?? FontFamily.Default);
        double fontSize = settings?.FontSize ?? 17;
        double lineHeight = settings is null ? fontSize * 1.6 : settings.LineHeightPixels;
        double characterSpacing = settings?.CharacterSpacing ?? 0;

        var runProps = new GenericTextRunProperties(
            typeface, fontSize, null, settings?.Foreground ?? Brushes.Black, Brushes.Transparent,
            BaselineAlignment.Baseline, CultureInfo.InvariantCulture, null);

        string text = Text;
        var source = new WordSplitTextSource(text, runProps);
        var paragraphProps = new GenericTextParagraphProperties(runProps, TextAlignment.Left, TextWrapping.Wrap, lineHeight, characterSpacing);

        // Real bug found via manual testing 2026-09-02: maxHeight: 0 here is NOT "unconstrained" (the
        // assumption this code originally shipped with) - it's a literal zero-height clip, floored at
        // one line. Every paragraph silently collapsed to a single line regardless of actual length,
        // which is what produced the "blank reading pane" symptom (chrome/title still correct, since
        // RecomputeCurrentPage itself was never at fault - confirmed via a live-data diagnostic probe
        // against real book text, and independently via a minimal TextLayout repro comparing maxHeight
        // values directly). PositiveInfinity is the correct "let the paragraph take whatever height its
        // wrapped content needs" value - this control's own height is unconstrained by design (the
        // paragraph list scrolls), only its width is fixed.
        _layout = new TextLayout(source, paragraphProps, TextTrimming.None, maxWidth: maxWidth, maxHeight: double.PositiveInfinity, maxLines: 0);

        BuildRunSpans(text);
    }

    /// <summary>Walks the freshly-built <see cref="_layout"/>'s lines/runs once, recording each run's original (un-gapped) and visual (gapped) X so <see cref="Render"/>, <see cref="DeGapPoint"/>, and <see cref="TranslateRectForward"/> all share one source of truth.</summary>
    private void BuildRunSpans(string text)
    {
        _runSpans.Clear();
        _lineYOrigins.Clear();

        if (_layout is null)
        {
            return;
        }

        double wordSpacing = Settings?.WordSpacing ?? 0;
        double y = 0;
        int charCursor = 0;

        foreach (var line in _layout.TextLines)
        {
            _lineYOrigins.Add(y);
            double originalX = 0;
            double visualX = 0;

            foreach (var run in line.TextRuns)
            {
                if (run is not DrawableTextRun drawable)
                {
                    continue;
                }

                int start = charCursor;
                int end = start + drawable.Length;
                double width = drawable.Size.Width;

                _runSpans.Add(new RunSpan(_lineYOrigins.Count - 1, start, end, originalX, visualX, width));

                originalX += width;
                visualX += width;

                bool endsWithWhitespace = end > 0 && end <= text.Length && char.IsWhiteSpace(text[end - 1]);
                if (endsWithWhitespace && wordSpacing != 0)
                {
                    visualX += wordSpacing;
                }

                charCursor = end;
            }

            y += line.Height;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_layout is null)
        {
            return;
        }

        DrawHighlightFills(context);
        DrawLiveSelectionFill(context);
        DrawRunsManually(context);
    }

    private void DrawRunsManually(DrawingContext context)
    {
        if (_layout is null)
        {
            return;
        }

        int spanIndex = 0;
        foreach (var line in _layout.TextLines)
        {
            foreach (var run in line.TextRuns)
            {
                if (run is not DrawableTextRun drawable)
                {
                    continue;
                }

                var span = _runSpans[spanIndex++];
                drawable.Draw(context, new Point(span.VisualX, _lineYOrigins[span.LineIndex]));
            }
        }
    }

    private void DrawHighlightFills(DrawingContext context)
    {
        foreach (var highlight in Highlights)
        {
            if (highlight.End <= highlight.Start)
            {
                continue;
            }

            var brush = HighlightFills[highlight.Color];
            foreach (var rect in HitTestRangeVisual(highlight.Start, highlight.End - highlight.Start))
            {
                context.FillRectangle(brush, rect);
            }
        }
    }

    private void DrawLiveSelectionFill(DrawingContext context)
    {
        if (!_isDragging || _dragAnchor is not { } anchor || _dragCurrent is not { } current || anchor == current)
        {
            return;
        }

        int start = Math.Min(anchor, current);
        int end = Math.Max(anchor, current);
        foreach (var rect in HitTestRangeVisual(start, end - start))
        {
            context.FillRectangle(SelectionFill, rect);
        }
    }

    /// <summary>Original-coordinate rects from <c>TextLayout.HitTestTextRange</c>, each shifted to visual (gapped) X per the class doc's known v1 simplification.</summary>
    private IEnumerable<Rect> HitTestRangeVisual(int start, int length)
    {
        if (_layout is null)
        {
            yield break;
        }

        foreach (var rect in _layout.HitTestTextRange(start, length))
        {
            yield return TranslateRectForward(rect);
        }
    }

    private Rect TranslateRectForward(Rect originalRect)
    {
        var span = _runSpans.FirstOrDefault(s => originalRect.X >= s.OriginalX - 0.5 && originalRect.X < s.OriginalX + s.Width + 0.5);
        double shift = span.Width > 0 || span.CharEnd > 0 ? span.VisualX - span.OriginalX : 0;
        return originalRect.Translate(new Vector(shift, 0));
    }

    /// <summary>Converts a pointer position in this control's own (visual/gapped) coordinates back to the original, un-gapped coordinates <see cref="TextLayout.HitTestPoint"/> expects.</summary>
    private Point DeGapPoint(Point visual)
    {
        int lineIndex = 0;
        for (int i = 0; i < _lineYOrigins.Count; i++)
        {
            if (_lineYOrigins[i] <= visual.Y)
            {
                lineIndex = i;
            }
        }

        var lineSpans = _runSpans.Where(s => s.LineIndex == lineIndex).ToList();
        if (lineSpans.Count == 0)
        {
            return visual;
        }

        RunSpan? containing = null;
        foreach (var span in lineSpans)
        {
            if (visual.X >= span.VisualX && visual.X < span.VisualX + span.Width)
            {
                containing = span;
                break;
            }
        }

        // Fell in a word-gap (or past the last run on the line) - attribute it to the nearest run's
        // trailing edge, same rationale as the class doc's TranslateRectForward simplification.
        var target = containing ?? lineSpans.Last();
        double delta = Math.Clamp(visual.X - target.VisualX, 0, target.Width);
        return new Point(target.OriginalX + delta, visual.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_layout is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);

        if (TryFindTappedHighlight(point, out var highlight, out var bounds))
        {
            RaiseEvent(new ParagraphHighlightTappedEventArgs(HighlightTappedEvent, highlight, bounds));
            e.Handled = true;
            return;
        }

        var originalPoint = DeGapPoint(point);
        var hit = _layout.HitTestPoint(ref originalPoint);
        _dragAnchor = hit.TextPosition;
        _dragCurrent = hit.TextPosition;
        _isDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging || _layout is null)
        {
            return;
        }

        var originalPoint = DeGapPoint(e.GetPosition(this));
        var hit = _layout.HitTestPoint(ref originalPoint);
        _dragCurrent = hit.TextPosition;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);

        if (_dragAnchor is not { } anchor || _dragCurrent is not { } current || anchor == current)
        {
            _dragAnchor = null;
            _dragCurrent = null;
            InvalidateVisual();
            return;
        }

        int start = Math.Min(anchor, current);
        int end = Math.Max(anchor, current);
        var bounds = HitTestRangeVisual(start, end - start)
            .Aggregate((Rect?)null, (acc, r) => acc is { } a ? a.Union(r) : r) ?? new Rect();

        RaiseEvent(new ParagraphSelectionEventArgs(SelectionCompletedEvent, start, end, bounds));

        _dragAnchor = null;
        _dragCurrent = null;
        InvalidateVisual();
    }

    private bool TryFindTappedHighlight(Point visualPoint, out ParagraphHighlight highlight, out Rect bounds)
    {
        foreach (var candidate in Highlights)
        {
            foreach (var rect in HitTestRangeVisual(candidate.Start, candidate.End - candidate.Start))
            {
                if (rect.Contains(visualPoint))
                {
                    highlight = candidate;
                    bounds = rect;
                    return true;
                }
            }
        }

        highlight = default;
        bounds = default;
        return false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ParagraphViewAutomationPeer(this);

    /// <summary>Splits paragraph text into one <see cref="TextCharacters"/> run per word (see the class doc's Mechanism section) - the shaper keeps these as separate drawable runs even though every run shares identical <see cref="TextRunProperties"/>, which is what makes per-word manual gap insertion possible.</summary>
    private sealed class WordSplitTextSource : ITextSource
    {
        private readonly (int Start, int Length)[] _runs;
        private readonly string _text;
        private readonly TextRunProperties _props;

        public WordSplitTextSource(string text, TextRunProperties props)
        {
            _text = text;
            _props = props;
            _runs = SplitIntoWordRuns(text);
        }

        /// <summary>Each run keeps its trailing whitespace (if any) attached, so a run boundary always falls exactly on a word/space boundary - <see cref="ParagraphView.BuildRunSpans"/> relies on this to decide which runs "end in whitespace" for word-gap insertion.</summary>
        private static (int Start, int Length)[] SplitIntoWordRuns(string text)
        {
            if (text.Length == 0)
            {
                return Array.Empty<(int, int)>();
            }

            var runs = new List<(int Start, int Length)>();
            int runStart = 0;
            bool inWhitespace = char.IsWhiteSpace(text[0]);

            for (int i = 1; i < text.Length; i++)
            {
                bool isWhitespace = char.IsWhiteSpace(text[i]);
                if (isWhitespace && !inWhitespace)
                {
                    // Word just ended - close its run once its trailing whitespace run (if any) is
                    // captured too, so fold forward instead of splitting here.
                    inWhitespace = true;
                }
                else if (!isWhitespace && inWhitespace)
                {
                    runs.Add((runStart, i - runStart));
                    runStart = i;
                    inWhitespace = false;
                }
            }

            runs.Add((runStart, text.Length - runStart));
            return runs.ToArray();
        }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= _text.Length)
            {
                return null;
            }

            foreach (var (start, length) in _runs)
            {
                if (textSourceIndex < start + length)
                {
                    int offsetIntoRun = textSourceIndex - start;
                    var slice = _text.AsMemory(start + offsetIntoRun, length - offsetIntoRun);
                    return new TextCharacters(slice, _props);
                }
            }

            return null;
        }
    }
}
