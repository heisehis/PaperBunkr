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

    /// <summary>Single dot-separated meta line, e.g. "Image · Ongoing · 66 issues · 12 unread".
    /// Still populated for screen readers / the Home spotlight, but the hero shows
    /// <see cref="MetaBadges"/> instead whenever that list is non-empty.</summary>
    string MetaLine { get; }

    /// <summary>Kavita-style icon chips for the hero meta row - publisher / status / year / format /
    /// age-rating / language (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
    /// design.md Part 4). Empty by default so the Home spotlight keeps its plain
    /// <see cref="MetaLine"/>; the three detail screens override it.</summary>
    IReadOnlyList<DetailMetaBadge> MetaBadges => System.Array.Empty<DetailMetaBadge>();

    /// <summary>Whether <see cref="MetaBadges"/> has anything - drives the hero's badges-vs-<see cref="MetaLine"/> switch.</summary>
    bool HasMetaBadges => MetaBadges.Count > 0;

    /// <summary>Plain dot-separated "N issues · M unread" (or "N chapters · M unread" for manga) -
    /// kept as its own text line rather than a badge (user direction 2026-09-04: wanted the original
    /// plain-text look back, positioned under the badge row and above the action buttons). Null when
    /// there's nothing to show (book detail, Home spotlight).</summary>
    string? IssueSummaryLine => null;

    /// <summary>Optional body line under <see cref="MetaLine"/> - a written synopsis / blurb. Null or
    /// empty hides it. Default null so the three detail-screen ViewModels are unaffected until they
    /// opt in; the Home spotlight adapter sets it (docs/superpowers/specs/2026-08-28-home-screen-
    /// redesign-design.md §3).</summary>
    string? Synopsis => null;

    /// <summary>Shared-element key for the hero's own foreground cover thumbnail (docs/superpowers/
    /// specs/2026-09-04-navigation-transition-system-design.md) - "series-cover:{seriesId}", matching
    /// <c>MainViewModel.CurrentDrillSharedKey</c>'s/<c>GoDetailForSeries</c>'s own scheme so a Library
    /// series card and this hero agree on the same key independently, with no shared lookup. Default
    /// null so BookDetail (books have no Library-grid cover tile to morph from/to) and the Home
    /// spotlight adapter (not a drill-down navigation target) need no change.</summary>
    string? SharedElementKey => null;

    /// <summary>Ordered action buttons; first <c>IsPrimary</c> one is the accent button.</summary>
    IReadOnlyList<DetailHeroAction> Actions { get; }

    /// <summary>The series' own <see cref="Data.Entities.ReadingStatus"/> as an enum name (or
    /// <see langword="null"/> for <see cref="Data.Entities.ReadingStatus.Unknown"/> / not
    /// applicable) - the hero renders it as a coloured <c>BrandMark</c> glyph on the meta line
    /// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md §8a). Default
    /// null so the book-detail VM and Home spotlight adapter need no change.</summary>
    string? ReadingStatus => null;

    /// <summary>When non-null, the hero (and band) render the interactive reading-status setter
    /// instead of the read-only glyph (Part 2 §C). Same instance handed to both surfaces by the
    /// host screen. Null for book detail (books have no reading status) and the Home spotlight.</summary>
    ReadingStatusPickerViewModel? ReadingStatusPicker => null;

    /// <summary>Tracker ring data; null hides the ring (everything except a linked manga series).</summary>
    DetailHeroProgress? TrackerProgress { get; }
}
