using System;
using System.IO;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Points every cover-cache location (generated + custom thumbnail dirs for comics and books, plus
/// the <see cref="CoverCacheState"/> sidecar) at a throwaway temp folder for the life of the object,
/// then restores the originals on <see cref="Dispose"/>. Any test whose code path reaches
/// <c>CoverThumbnailService</c> / <c>BookCoverThumbnailService</c> must use this - those services'
/// orphan sweep would otherwise attic the real per-user cache
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md).
/// </summary>
public sealed class CoverCacheTestRedirect : IDisposable
{
    private readonly string _root;
    private readonly string _origComicThumbs;
    private readonly string _origBookThumbs;
    private readonly string _origCustomComic;
    private readonly string _origCustomBook;
    private readonly string _origState;

    public CoverCacheTestRedirect()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_covers_{Guid.NewGuid():N}");

        _origComicThumbs = CoverThumbnailPaths.ThumbnailDirectory;
        _origBookThumbs = BookCoverThumbnailPaths.ThumbnailDirectory;
        _origCustomComic = CustomCoverPaths.Directory;
        _origCustomBook = CustomBookCoverPaths.Directory;
        _origState = CoverCacheState.FilePath;

        CoverThumbnailPaths.ThumbnailDirectory = Path.Combine(_root, "thumbnails");
        BookCoverThumbnailPaths.ThumbnailDirectory = Path.Combine(_root, "book-thumbnails");
        CustomCoverPaths.Directory = Path.Combine(_root, "custom-covers");
        CustomBookCoverPaths.Directory = Path.Combine(_root, "custom-book-covers");
        CoverCacheState.FilePath = Path.Combine(_root, "cover-cache-state.json");
    }

    public string ComicThumbnailDir => CoverThumbnailPaths.ThumbnailDirectory;

    public string BookThumbnailDir => BookCoverThumbnailPaths.ThumbnailDirectory;

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _origComicThumbs;
        BookCoverThumbnailPaths.ThumbnailDirectory = _origBookThumbs;
        CustomCoverPaths.Directory = _origCustomComic;
        CustomBookCoverPaths.Directory = _origCustomBook;
        CoverCacheState.FilePath = _origState;

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
