using Avalonia;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers what's reliably unit-testable for <see cref="ParagraphView"/> without simulating real
/// pointer routing through a hosted window (measure/layout behavior and the automation peer) - the
/// interactive drag-select flow itself is covered by the plan's Step 6 manual on-screen check and
/// Step 8's <c>Paperbunkr.App.UiTests</c> FlaUI coverage instead, per docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-plan.md's own test strategy.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ParagraphViewTests
{
    private static ParagraphView CreateMeasured(string text, BookReaderSettings? settings = null, double maxWidth = 400)
    {
        var view = new ParagraphView { Text = text, Settings = settings ?? new BookReaderSettings() };
        view.Measure(new Size(maxWidth, double.PositiveInfinity));
        return view;
    }

    [Fact]
    public void Measure_ProducesPositiveHeight_ForNonEmptyText()
    {
        var view = CreateMeasured("A paragraph long enough to wrap across more than one line at a narrow width.", maxWidth: 150);

        Assert.True(view.DesiredSize.Height > 0);
    }

    /// <summary>
    /// Real bug found via manual testing 2026-09-02 (the "blank reading pane" report): every
    /// paragraph silently measured as exactly one line tall, regardless of actual length, because
    /// ParagraphView.EnsureLayout passed maxHeight: 0 to TextLayout's ITextSource-based constructor
    /// assuming 0 meant "unconstrained" - it's actually a literal zero-height clip floored at one
    /// line. None of the existing tests caught this because they only asserted Height > 0 (trivially
    /// true even for a single clipped line) rather than that height scales with content at a fixed
    /// width - which is the actual signal a "text doesn't wrap" regression needs.
    /// </summary>
    [Fact]
    public void Measure_TallerForLongerTextAtTheSameWidth_ProvingRealWrapping()
    {
        const string shortText = "One short line.";
        const string longText = "One short line. But this paragraph keeps going for quite a while, with several more " +
            "clauses and sentences, specifically so that at a narrow fixed width it is forced to wrap across " +
            "multiple lines rather than fitting on just one.";

        var shortView = CreateMeasured(shortText, maxWidth: 200);
        var longView = CreateMeasured(longText, maxWidth: 200);

        Assert.True(longView.DesiredSize.Height > shortView.DesiredSize.Height * 2,
            $"Expected the much longer paragraph to measure meaningfully taller (multi-line wrapping) " +
            $"at the same width, but short={shortView.DesiredSize.Height} long={longView.DesiredSize.Height}.");
    }

    [Fact]
    public void Measure_HandlesEmptyText_WithoutThrowing()
    {
        var view = CreateMeasured(string.Empty);

        Assert.True(view.DesiredSize.Height >= 0);
    }

    [Fact]
    public void Measure_WithWordSpacing_ProducesTallerOrEqualLayout_ThanWithoutIt()
    {
        // A crude but real proxy for "word spacing is actually wired into layout": at a width tight
        // enough that extra inter-word gaps push a word onto the next line, height should increase
        // (or at minimum never decrease) once WordSpacing is applied versus zero.
        const string text = "one two three four five six seven eight nine ten";

        var withoutSpacing = CreateMeasured(text, new BookReaderSettings { WordSpacing = 0 }, maxWidth: 220);
        var withSpacing = CreateMeasured(text, new BookReaderSettings { WordSpacing = 40 }, maxWidth: 220);

        Assert.True(withSpacing.DesiredSize.Height >= withoutSpacing.DesiredSize.Height);
    }

    [Fact]
    public void Measure_WithOutOfRangeHighlight_DoesNotThrow()
    {
        var view = new ParagraphView
        {
            Text = "short",
            Settings = new BookReaderSettings(),
            Highlights = [new ParagraphHighlight(0, 999, BookHighlightColor.Yellow)],
        };

        view.Measure(new Size(400, double.PositiveInfinity));

        Assert.True(view.DesiredSize.Height >= 0);
    }

    [Fact]
    public void AutomationPeer_ReportsPlainText_IndependentOfWordSpacing()
    {
        var view = CreateMeasured("Reflowable prose.", new BookReaderSettings { WordSpacing = 20 });
        var peer = new ParagraphViewAutomationPeer(view);

        Assert.Equal("Reflowable prose.", peer.GetName());
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Text, peer.GetAutomationControlType());
    }
}
