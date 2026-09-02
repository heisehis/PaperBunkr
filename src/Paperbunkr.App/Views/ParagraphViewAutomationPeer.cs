using Avalonia.Automation.Peers;

namespace Paperbunkr.App.Views;

/// <summary>
/// Closes the gap docs/superpowers/specs/2026-09-01-books-reader-screen-reader-accessibility-
/// design.md flags: <see cref="ParagraphView"/> is a hand-drawn <c>Control</c>, which gets no
/// automation-tree exposure for free the way a plain <c>TextBlock</c> would (via Avalonia's own
/// built-in text-block automation peer). Without this, every book read through the reflow reader
/// would be invisible to Narrator/NVDA/JAWS the moment <c>ParagraphView</c> replaced <c>TextBlock</c>
/// - built alongside <c>ParagraphView</c> itself in the same implementation pass per that spec's
/// Cross-Spec Dependency note, not deferred to a later accessibility-specific pass.
///
/// Reports the paragraph's plain text (<see cref="ParagraphView.Text"/>) - independent of the
/// word-spacing rendering adjustment, which is a display-only concern irrelevant to what gets
/// announced. Highlighted-range status exposure is left for later (per the accessibility spec, the
/// core requirement is text being reachable at all, not richer per-range status).
/// </summary>
public sealed class ParagraphViewAutomationPeer : ControlAutomationPeer
{
    public ParagraphViewAutomationPeer(ParagraphView owner) : base(owner)
    {
    }

    protected override string GetNameCore() => ((ParagraphView)Owner).Text;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;
}
