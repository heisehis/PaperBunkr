using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Books;

namespace Paperbunkr.App.Services;

/// <summary>One line of the match dialog's per-field diff. <see cref="Field"/> is a <see cref="BookFields"/> key.</summary>
public sealed record BookFieldDiff(string Field, string Label, string Current, string Proposed, bool DefaultChecked);

public sealed record BookMatchCandidate(BookProviderRecord Record, double Score);

public sealed record BookBulkMatchResult(int Matched, int NeedsReview, int NoMatch, int Skipped, int Failed, bool RateLimited)
{
    public string Describe()
    {
        var parts = new List<string>();
        if (Matched > 0) parts.Add($"{Matched} matched");
        if (NeedsReview > 0) parts.Add($"{NeedsReview} need a closer look (open the book and choose Match metadata)");
        if (NoMatch > 0) parts.Add($"{NoMatch} had no match");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        if (Failed > 0) parts.Add($"{Failed} failed");
        string text = parts.Count == 0 ? "Nothing to match" : string.Join(", ", parts);
        return RateLimited ? text + ". Stopped early: the source asked us to slow down." : text;
    }
}

/// <summary>
/// Looks books up in the public catalogues and applies what the user accepts
/// (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5). The dialog uses <see cref="SearchAsync"/> /
/// <see cref="BuildDiff"/> / <see cref="Apply"/>; the bulk action uses <see cref="RunBulkAsync"/>, which only ever applies a
/// single clear winner and only fills empty fields.
/// </summary>
public sealed class BookMetadataMatchService
{
    public const string GoogleBooksCredentialProvider = "GoogleBooks";

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<IReadOnlyList<IBookMetadataProvider>> _providers;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _downloadCover;
    private readonly Func<IReadOnlyList<int>, Task>? _writeBack;

    public BookMetadataMatchService()
        : this(PaperbunkrDb.CreateContext, ProvidersFromSettings, DownloadCoverAsync, ids => new BookFileWriteBackService().AutoWriteAsync(ids))
    {
    }

    internal BookMetadataMatchService(
        Func<PaperbunkrDbContext> contextFactory,
        Func<IReadOnlyList<IBookMetadataProvider>> providers,
        Func<string, CancellationToken, Task<byte[]?>>? downloadCover = null,
        Func<IReadOnlyList<int>, Task>? writeBack = null)
    {
        _contextFactory = contextFactory;
        _providers = providers;
        _downloadCover = downloadCover ?? ((_, _) => Task.FromResult<byte[]?>(null));
        _writeBack = writeBack;
    }

    /// <summary>The catalogues the user left switched on, using the saved Google Books key if there is one.</summary>
    public static IReadOnlyList<IBookMetadataProvider> ProvidersFromSettings()
    {
        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        string? key = CredentialStore.Get(context, GoogleBooksCredentialProvider, CredentialKind.ApiKey);

        var list = new List<IBookMetadataProvider>();
        if (settings.BookMetadataOpenLibraryEnabled)
        {
            list.Add(OpenLibraryBookProvider.Shared);
        }

        if (settings.BookMetadataGoogleBooksEnabled)
        {
            list.Add(new GoogleBooksProvider(() => key));
        }

        return list;
    }

    private static readonly Lazy<HttpClient> CoverClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/1.0 (+https://github.com/paperbunkr/paperbunkr)");
        return client;
    });

    private static async Task<byte[]?> DownloadCoverAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            return await CoverClient.Value.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Candidates from every enabled catalogue, best first. Throws <see cref="BookProviderException"/> only when <i>every</i> catalogue failed.</summary>
    public async Task<IReadOnlyList<BookMatchCandidate>> SearchAsync(int bookId, string? titleOverride, string? authorOverride, CancellationToken ct)
    {
        using var context = _contextFactory();
        var book = await context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, ct).ConfigureAwait(false);
        if (book is null)
        {
            return Array.Empty<BookMatchCandidate>();
        }

        string title = string.IsNullOrWhiteSpace(titleOverride) ? book.Title : titleOverride.Trim();
        string? author = string.IsNullOrWhiteSpace(authorOverride) ? book.Author : authorOverride.Trim();
        return await SearchAsync(book.ISBN, title, author, book.LanguageISO, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<BookMatchCandidate>> SearchAsync(string? isbn, string title, string? author, string? language, CancellationToken ct)
    {
        var providers = _providers();
        var all = new List<BookProviderRecord>();
        BookProviderException? last = null;
        int failed = 0;

        foreach (var provider in providers)
        {
            try
            {
                all.AddRange(await provider.SearchAsync(isbn, title, author, ct).ConfigureAwait(false));
            }
            catch (BookProviderException ex)
            {
                failed++;
                last = ex;
            }
        }

        if (providers.Count > 0 && failed == providers.Count && last is not null)
        {
            throw last;
        }

        return BookMatchRanker.Rank(all, title, author, isbn, language)
            .Select(r => new BookMatchCandidate(r.Record, r.Score))
            .ToList();
    }

    /// <summary>The candidate with its description / subjects filled in from the catalogue's detail record.</summary>
    public async Task<BookProviderRecord> GetFullAsync(BookProviderRecord candidate, CancellationToken ct)
    {
        var provider = _providers().FirstOrDefault(p => p.Kind == candidate.Source);
        if (provider is null)
        {
            return candidate;
        }

        BookProviderRecord? detail;
        try
        {
            detail = await provider.GetAsync(candidate.Id, ct).ConfigureAwait(false);
        }
        catch (BookProviderException)
        {
            return candidate; // the search record is still good enough to apply
        }

        return detail is null ? candidate : Merge(candidate, detail);
    }

    internal static BookProviderRecord Merge(BookProviderRecord search, BookProviderRecord detail) => search with
    {
        Subtitle = search.Subtitle ?? detail.Subtitle,
        Description = detail.Description ?? search.Description,
        Subjects = detail.Subjects.Count > 0 ? detail.Subjects : search.Subjects,
        Publisher = search.Publisher ?? detail.Publisher,
        Isbn = search.Isbn ?? detail.Isbn,
        LanguageISO = search.LanguageISO ?? detail.LanguageISO,
        Year = search.Year ?? detail.Year,
        CoverUrl = search.CoverUrl ?? detail.CoverUrl,
    };

    /// <summary>Current vs proposed for every field the record could change. Only differing, non-empty proposals appear; empty current values are pre-ticked.</summary>
    public IReadOnlyList<BookFieldDiff> BuildDiff(int bookId, BookProviderRecord record)
    {
        using var context = _contextFactory();
        var book = context.Books.AsNoTracking().Include(b => b.BookSeries).Include(b => b.Tags).FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            return Array.Empty<BookFieldDiff>();
        }

        var diffs = new List<BookFieldDiff>();

        void Add(string field, string label, string? current, string? proposed)
        {
            string cur = current?.Trim() ?? string.Empty;
            string prop = proposed?.Trim() ?? string.Empty;
            if (prop.Length > 0 && !string.Equals(cur, prop, StringComparison.Ordinal))
            {
                diffs.Add(new BookFieldDiff(field, label, cur, prop, DefaultChecked: cur.Length == 0));
            }
        }

        Add(BookFields.Subtitle, "Subtitle", book.Subtitle, record.Subtitle);
        Add(BookFields.Publisher, "Publisher", book.Publisher, record.Publisher);
        Add(BookFields.Language, "Language", book.LanguageISO, record.LanguageISO);
        Add(BookFields.Isbn, "ISBN", book.ISBN, record.Isbn);
        Add(BookFields.Summary, "Summary", book.Summary, record.Description);
        Add(BookFields.Series, "Series", book.BookSeries?.Name, record.SeriesName);
        Add(BookFields.SeriesIndex, "Series number", book.SeriesIndex?.ToString("0.###", CultureInfo.InvariantCulture), record.SeriesIndex?.ToString("0.###", CultureInfo.InvariantCulture));
        Add(BookFields.CommunityRating, "Community rating",
            book.CommunityRating?.ToString("0.0#", CultureInfo.InvariantCulture),
            record.CommunityRating is float c && c > 0f ? c.ToString("0.0#", CultureInfo.InvariantCulture) : null);

        var haveGenres = book.Tags.Where(t => t.Field == BookTagField.Genre).Select(t => t.Value).ToList();
        var newGenres = record.Subjects.Where(s => !haveGenres.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        if (newGenres.Count > 0)
        {
            diffs.Add(new BookFieldDiff(BookFields.Genre, "Genres (added)", string.Join(", ", haveGenres), string.Join(", ", newGenres), DefaultChecked: true));
        }

        return diffs;
    }

    /// <summary>Writes the accepted <paramref name="fields"/> (overriding what is there, Manual or not), records provenance, optionally replaces the cover.</summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(int bookId, BookProviderRecord record, ISet<string> fields, bool applyCover, CancellationToken ct)
    {
        IReadOnlyList<string> changed;
        using (var context = _contextFactory())
        {
            var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return Array.Empty<string>();
            }

            changed = BookMetadataApplier.Apply(
                book, record.ToFieldValues(), record.Source, record.Id, DateTime.UtcNow,
                name => ResolveSeries(context, name), BookApplyMode.Override, fields);
            context.SaveChanges();
        }

        if (applyCover && !string.IsNullOrWhiteSpace(record.CoverUrl))
        {
            byte[]? bytes = await _downloadCover(record.CoverUrl, ct).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pb-cover-{Guid.NewGuid():N}.jpg");
                try
                {
                    await System.IO.File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
                    new BookCoverThumbnailService().TrySetCustomCover(bookId, path);
                }
                finally
                {
                    try { System.IO.File.Delete(path); } catch (System.IO.IOException) { }
                }
            }
        }

        if (changed.Count > 0 && _writeBack is not null)
        {
            _ = _writeBack(new[] { bookId });
        }

        return changed;
    }

    private static BookSeries ResolveSeries(PaperbunkrDbContext context, string name)
    {
        var existing = context.BookSeries.ToList().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = new BookSeries { Name = name };
        context.BookSeries.Add(created);
        return created;
    }

    public void SetSkipForever(int bookId, bool skip)
    {
        using var context = _contextFactory();
        var book = context.Books.Find(bookId);
        if (book is not null && book.SkipMetadataMatch != skip)
        {
            book.SkipMetadataMatch = skip;
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Bulk "Match metadata…": for each book that is not skip-forever, applies a single clear winner (fill-empty only, never overwriting).
    /// Ambiguous books are counted and left for the per-book dialog; a rate-limited catalogue stops the run.
    /// </summary>
    public async Task<BookBulkMatchResult> RunBulkAsync(IReadOnlyList<int> bookIds, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        int matched = 0, review = 0, none = 0, skipped = 0, failed = 0, done = 0;
        bool limited = false;
        progress?.Report((0, bookIds.Count));

        foreach (int id in bookIds)
        {
            ct.ThrowIfCancellationRequested();

            Book? book;
            using (var context = _contextFactory())
            {
                book = context.Books.AsNoTracking().FirstOrDefault(b => b.Id == id);
            }

            if (book is null || book.SkipMetadataMatch)
            {
                skipped++;
                progress?.Report((++done, bookIds.Count));
                continue;
            }

            try
            {
                var candidates = await SearchAsync(book.ISBN, book.Title, book.Author, book.LanguageISO, ct).ConfigureAwait(false);
                if (candidates.Count == 0)
                {
                    none++;
                }
                else
                {
                    var winner = BookMatchRanker.ClearWinner(candidates.Select(c => new RankedBookMatch(c.Record, c.Score)).ToList());
                    if (winner is null)
                    {
                        review++;
                    }
                    else
                    {
                        var full = await GetFullAsync(winner, ct).ConfigureAwait(false);
                        await ApplyFillEmptyAsync(id, full).ConfigureAwait(false);
                        matched++;
                    }
                }
            }
            catch (BookProviderException ex) when (ex.IsRateLimited)
            {
                limited = true;
                break;
            }
            catch (BookProviderException)
            {
                failed++;
            }

            progress?.Report((++done, bookIds.Count));
        }

        return new BookBulkMatchResult(matched, review, none, skipped, failed, limited);
    }

    private async Task ApplyFillEmptyAsync(int bookId, BookProviderRecord record)
    {
        using (var context = _contextFactory())
        {
            var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return;
            }

            var changed = BookMetadataApplier.Apply(
                book, record.ToFieldValues(), record.Source, record.Id, DateTime.UtcNow, name => ResolveSeries(context, name));
            context.SaveChanges();
            if (changed.Count == 0)
            {
                return;
            }
        }

        if (_writeBack is not null)
        {
            await _writeBack(new[] { bookId }).ConfigureAwait(false);
        }
    }
}
