using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// A page source that can change the pixels of the pages it hands out (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #2 and #3): auto-levels and auto-crop. Separate from
/// <see cref="IReaderPageSource"/> so a source that does not process (a test fake, the PDF pipeline) needs nothing.
/// </summary>
public interface IReaderPageProcessing
{
    /// <summary>
    /// Sets what is done to pages decoded from now on. <paramref name="cropOverrides"/> maps a 0-based page number to its own choice (a page not in it follows <see cref="PageProcessingOptions.AutoCrop"/>).
    /// Pages already cached under another setting are simply not found again, so the next request decodes them the new way.
    /// </summary>
    void SetProcessing(PageProcessingOptions options, IReadOnlyDictionary<int, PageCropMode>? cropOverrides);

    /// <summary>What is set now.</summary>
    PageProcessingOptions Processing { get; }

    /// <summary>What auto-crop finds on a page's original pixels (a tuning aid: it decodes the page again and caches nothing), or null when there is no plain border to trim.</summary>
    PageCropRect? DetectCrop(int pageIndex);
}
