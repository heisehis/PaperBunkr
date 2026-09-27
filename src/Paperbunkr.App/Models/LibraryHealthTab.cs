namespace Paperbunkr.App.Models;

/// <summary>The sub-tabs of the Library Health card (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md).</summary>
public enum LibraryHealthTab
{
    /// <summary>Stat tiles, Verify Now, the confirm-missing setting and the "Needs attention" list.</summary>
    Overview,

    /// <summary>The queues that need a decision: duplicates, series conflicts, content type, metadata proposals, ad pages, reported pages, similar series.</summary>
    Review,

    /// <summary>File and row hygiene: missing files, empty rows, recently removed.</summary>
    Files,
}
