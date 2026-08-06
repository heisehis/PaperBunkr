using System;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Decodes comic pages to Avalonia bitmaps (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md
/// §2). One instance wraps one opened archive for the duration of an issue-viewing session.
/// </summary>
public interface IPageImageDecoder : IDisposable
{
    int PageCount { get; }

    /// <summary>Decodes (or returns from cache) the page at the given 0-based index. Throws if the page can't be decoded by any path — callers should catch and show a per-page error state.</summary>
    Bitmap GetPage(int pageIndex);
}
