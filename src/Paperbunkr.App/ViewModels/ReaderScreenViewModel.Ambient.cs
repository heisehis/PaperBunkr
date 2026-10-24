using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Ambient letterbox (Cosmetics pitch #22, docs/superpowers/specs/2026-10-12-cosmetics-pitch-completion-design.md B): while the background mode is
/// <c>Ambient</c> the canvas background is a gradient sampled from the current page's thumbnail edges. It reads the thumbnail the reader already
/// holds, so it never asks for a decode; until that page's thumbnail exists the previous colours stay (or the default background on a fresh open).
/// </summary>
public partial class ReaderScreenViewModel
{
    private bool _ambientActive;

    /// <summary>Parallax and slow drift on the ambient backdrop (Preferences > Reader > Ambient motion, Motion v2 E Q34); only with the
    /// Ambient background.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _ambientMotionEnabled;

    /// <summary>Book look for spreads (Preferences > Reader, overridable per profile; Motion v2 reader §7).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _bookLook = true;

    /// <summary>How far through the issue the reader is, 0..1 - the page-stack edges' thickness on each side of a spread.</summary>
    public double BookProgress => PageCount <= 1 ? 0 : Math.Clamp((double)CurrentPageIndex / (PageCount - 1), 0, 1);

    private void SetAmbientActive(bool active)
    {
        _ambientActive = active;
        if (active)
        {
            RefreshAmbientBackdrop();
        }
    }

    /// <summary>Recomputes the ambient gradient for the current page; a no-op unless the Ambient mode is on.</summary>
    internal void RefreshAmbientBackdrop()
    {
        if (!_ambientActive)
        {
            return;
        }

        try
        {
            if (_currentPageIndex < 0 || _currentPageIndex >= Thumbnails.Count)
            {
                return;
            }

            if (Thumbnails[_currentPageIndex].CoverImage is not { } bitmap || EdgesOf(bitmap) is not { } edges)
            {
                return; // keep whatever is showing
            }

            CanvasBackgroundBrush = PageEdgeColors.BuildBrush(edges);
        }
        catch (Exception)
        {
            // A background choice must never crash the reader (same rule as the Texture mode).
        }
    }

    private static PageEdgeColors.Edges? EdgesOf(Bitmap bitmap)
    {
        int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0 || (long)width * height > 1_000_000)
        {
            return null;
        }

        var pixels = new uint[width * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length * 4, width * 4);
        }
        finally
        {
            handle.Free();
        }

        // CopyPixels hands back the bitmap's own format; thumbnails are 32-bit, and the colour averaging only needs R, G and B to be consistently ordered
        // (a swapped red/blue would only shift the hue of an already-darkened backdrop, but the common Bgra8888 order matches PageEdgeColors).
        if (bitmap.Format == Avalonia.Platform.PixelFormats.Rgba8888)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                uint p = pixels[i];
                pixels[i] = (p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
            }
        }

        return PageEdgeColors.FromPixels(pixels, width, height);
    }
}
