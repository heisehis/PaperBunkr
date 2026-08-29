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

    /// <summary>Display title, rendered in Bebas. Named <c>HeaderTitle</c> (not <c>Title</c>) so a
    /// concrete VM can also carry an unrelated <c>Title</c> property (e.g. the book title) - and so
    /// every implementer backs it with a real change-notifying member rather than an explicit
    /// interface impl that never raises PropertyChanged (the "all series stuck on one title" bug).</summary>
    string HeaderTitle { get; }

    /// <summary>Second line under the title - manga native + romaji; null on comic/book (line hidden).</summary>
    string? SecondaryTitle { get; }

    /// <summary>Single dot-separated meta line, e.g. "Image · Ongoing · 66 issues · 12 unread".</summary>
    string MetaLine { get; }

    /// <summary>Optional body line under <see cref="MetaLine"/> - a written synopsis / blurb. Null or
    /// empty hides it. Default null so the three detail-screen ViewModels are unaffected until they
    /// opt in; the Home spotlight adapter sets it (docs/superpowers/specs/2026-08-28-home-screen-
    /// redesign-design.md §3).</summary>
    string? Synopsis => null;

    /// <summary>Ordered action buttons; first <c>IsPrimary</c> one is the accent button.</summary>
    IReadOnlyList<DetailHeroAction> Actions { get; }

    /// <summary>Tracker ring data; null hides the ring (everything except a linked manga series).</summary>
    DetailHeroProgress? TrackerProgress { get; }
}
