using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The surface a <c>DetailHero</c> / <c>DetailBand</c> binds against, implemented by all three
/// detail-screen ViewModels (<see cref="DetailScreenViewModel"/>,
/// <see cref="MangaDetailScreenViewModel"/>, <see cref="BookDetailScreenViewModel"/>). Keeps the
/// shared header controls independent of which screen hosts them - see
/// docs/superpowers/specs/2026-08-28-detail-screens-streaming-redesign-design.md.
/// </summary>
public interface IDetailHeaderSource : INotifyPropertyChanged
{
    /// <summary>Fallback gradient painted behind the foreground cover thumbnail before/without art.</summary>
    IBrush CoverBrush { get; }

    /// <summary>The crisp foreground cover thumbnail.</summary>
    Bitmap? CoverImage { get; }

    /// <summary>Pre-blurred edge-to-edge backdrop (via <c>BackdropBlurRenderer</c>); null falls back to <see cref="CoverBrush"/>.</summary>
    Bitmap? BackdropImage { get; }

    /// <summary>Display title, rendered in Bebas.</summary>
    string Title { get; }

    /// <summary>Second line under the title - manga native + romaji; null on comic/book (line hidden).</summary>
    string? SecondaryTitle { get; }

    /// <summary>Single dot-separated meta line, e.g. "Image · Ongoing · 66 issues · 12 unread".</summary>
    string MetaLine { get; }

    /// <summary>Ordered action buttons; first <c>IsPrimary</c> one is the accent button.</summary>
    IReadOnlyList<DetailHeroAction> Actions { get; }

    /// <summary>Tracker ring data; null hides the ring (everything except a linked manga series).</summary>
    DetailHeroProgress? TrackerProgress { get; }
}
