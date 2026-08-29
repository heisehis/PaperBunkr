using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One back/forward step for Library's browse history (docs/superpowers/specs/
/// 2026-08-19-library-browse-history-design.md) - "which list" plus the search query, mirroring
/// CE's own <c>IBrowseHistory</c>/<c>CursorList&lt;IComicBookListProvider&gt;</c> model
/// (`ComicListBrowser.cs`) but scoped to fields Paperbunkr's sidebar actually has. Record equality
/// gives <c>CursorList&lt;T&gt;.AddAtCursor</c>'s duplicate-of-cursor dedup check the right
/// behavior for free.</summary>
public sealed record LibraryBrowseState(ContentType? ActiveContentType, int? ActiveCollectionId, string SearchQuery);
