using System;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Books;

/// <summary>
/// Constructs the right <see cref="IBookTextSource"/> for a <see cref="Book"/>'s
/// <see cref="BookFormat"/> (docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-
/// design.md). Every call site across the app that used to hand-roll a binary
/// <c>format == BookFormat.Epub ? new EpubBookSource(...) : new PdfBookSource(...)</c> ternary now
/// goes through here instead - that pattern would have silently misrouted FB2/MOBI books into the
/// PDF (rasterized page image) path the moment those formats existed, since it only ever checked for
/// Epub specifically. Lives in <c>Paperbunkr.Data</c> (not <c>Paperbunkr.Engine</c>, which knows
/// nothing about <see cref="BookFormat"/>, and not <c>Paperbunkr.App</c>) because <c>Paperbunkr.Data</c>
/// already references <c>Paperbunkr.Engine</c> (see <c>AnnotationExportService</c>'s own direct
/// <c>EpubBookSource</c> use) and is the one project both the App layer and Data-layer callers like
/// <c>AnnotationExportService</c> can reach.
/// </summary>
public static class BookTextSourceFactory
{
    public static IBookTextSource Create(BookFormat format, string filePath) => format switch
    {
        BookFormat.Epub => new EpubBookSource(filePath),
        BookFormat.Fb2 => new Fb2BookSource(filePath),
        BookFormat.Mobi => new MobiBookSource(filePath),
        BookFormat.Pdf => new PdfBookSource(filePath),
        _ => throw new NotSupportedException($"No IBookTextSource implementation is registered for {format}."),
    };
}
