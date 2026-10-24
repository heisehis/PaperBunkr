using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

public enum EntryOutcome
{
    MatchedById,
    MatchedByMetadata,
    NeedsChoice,
    Missing,
    Duplicate,
}

/// <summary>One requested entry and what the engine made of it.</summary>
public sealed record ResolvedEntry(ComicReference Reference, MatchResult Match, EntryOutcome Outcome)
{
    /// <summary>The library comic this entry links to; null when a placeholder has to stand in (or the entry is a duplicate).</summary>
    public int? IssueId => Outcome is EntryOutcome.MatchedById or EntryOutcome.MatchedByMetadata ? Match.AutoLink?.Id : null;
}

/// <summary>The outcome of <see cref="ReadingListResolver.Resolve(PaperbunkrDbContext, IReadOnlyList{ComicReference})"/>: nothing has been written yet.</summary>
public sealed record Resolution(IReadOnlyList<ResolvedEntry> Entries, LibraryIndex Library)
{
    public ImportReport Report(IReadOnlyList<string>? skipped = null, int? reported = null) => new(
        reported,
        Entries.Count,
        Entries.Count(e => e.Outcome == EntryOutcome.MatchedById),
        Entries.Count(e => e.Outcome == EntryOutcome.MatchedByMetadata),
        Entries.Count(e => e.Outcome == EntryOutcome.NeedsChoice),
        Entries.Count(e => e.Outcome == EntryOutcome.Missing),
        Entries.Where(e => e.Outcome == EntryOutcome.Duplicate).Select(e => e.Reference.Display).ToList(),
        skipped ?? Array.Empty<string>());
}

/// <summary>An entry ready to become a list item: the issue it points at (a real comic or a placeholder) and its source record.</summary>
public sealed record ResolvedItem(ResolvedEntry Entry, Issue Issue, ReadingListItemSource Source);

/// <summary>
/// The one way every import path turns requested entries into issues (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md
/// §5.6). <see cref="Resolve(PaperbunkrDbContext, IReadOnlyList{ComicReference})"/> reads the library once and decides; it writes nothing,
/// so looking at a list never litters the library. <see cref="Materialize"/> then stages the placeholders for whatever was not linked -
/// the caller saves once.
/// </summary>
public static class ReadingListResolver
{
    public static Resolution Resolve(PaperbunkrDbContext context, IReadOnlyList<ComicReference> references) =>
        Resolve(LibraryIndex.Load(context), references);

    /// <summary>Pure. A comic already taken by an earlier entry makes the later one a duplicate (decision Q12): a list holds a comic once.</summary>
    public static Resolution Resolve(LibraryIndex library, IReadOnlyList<ComicReference> references)
    {
        var taken = new HashSet<int>();
        var waiting = new HashSet<(string SeriesKey, string NumberKey, int? Year)>();
        var entries = new List<ResolvedEntry>(references.Count);
        foreach (var reference in references)
        {
            var match = MatchEngine.Match(reference, library);
            EntryOutcome outcome;
            if (match.AutoLink is { } issue)
            {
                outcome = !taken.Add(issue.Id) ? EntryOutcome.Duplicate
                    : match.ById ? EntryOutcome.MatchedById
                    : EntryOutcome.MatchedByMetadata;
            }
            else
            {
                // Two entries asking for the same comic the library does not have would share one placeholder, which a list can hold once.
                outcome = !waiting.Add((reference.SeriesKey, reference.NumberKey, reference.Year)) ? EntryOutcome.Duplicate
                    : match.NeedsChoice ? EntryOutcome.NeedsChoice
                    : EntryOutcome.Missing;
            }

            entries.Add(new ResolvedEntry(reference, match, outcome));
        }

        return new Resolution(entries, library);
    }

    /// <summary>
    /// Turns the kept entries (everything but duplicates) into issues and source records, in order. Linked entries get their tracked
    /// <see cref="Issue"/>; the rest get a placeholder - an existing one for the same comic if there is one, else a new one added to the
    /// context, in the series row that already owns issues when such a row exists. Nothing is saved here.
    /// </summary>
    public static IReadOnlyList<ResolvedItem> Materialize(PaperbunkrDbContext context, Resolution resolution)
    {
        var kept = resolution.Entries.Where(e => e.Outcome != EntryOutcome.Duplicate).ToList();
        var linkedIds = kept.Select(e => e.IssueId).OfType<int>().ToList();
        var linked = context.Issues
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .Where(i => linkedIds.Contains(i.Id))
            .ToDictionary(i => i.Id);

        PlaceholderPool? placeholders = null;
        var now = DateTime.UtcNow;
        var items = new List<ResolvedItem>(kept.Count);
        foreach (var entry in kept)
        {
            // A comic deleted between Resolve and now falls back to a placeholder (design §10).
            Issue? issue = entry.IssueId is int id ? linked.GetValueOrDefault(id) : null;
            bool isLinked = issue is not null;
            issue ??= (placeholders ??= new PlaceholderPool(context, resolution.Library)).For(entry.Reference);

            items.Add(new ResolvedItem(entry, issue, NewSource(entry.Reference,
                !isLinked ? MatchMethod.None : entry.Match.ById ? MatchMethod.ProviderId : MatchMethod.Metadata,
                isLinked ? MatchState.Linked : entry.Match.NeedsChoice ? MatchState.NeedsChoice : MatchState.Unresolved,
                now)));
        }

        return items;
    }

    public static ReadingListItemSource NewSource(ComicReference reference, MatchMethod method, MatchState state, DateTime? checkedUtc) => new()
    {
        Series = reference.Series,
        Number = reference.Number,
        Volume = reference.Volume,
        Year = reference.Year,
        Format = reference.Format,
        Title = reference.Title,
        SeriesKey = reference.SeriesKey,
        NumberKey = reference.NumberKey,
        SourceKey = reference.SourceKey,
        SourceEntryId = reference.SourceEntryId,
        ComicVineIssueId = reference.ComicVineIssueId,
        ComicVineSeriesId = reference.ComicVineSeriesId,
        MetronIssueId = reference.MetronIssueId,
        MetronSeriesId = reference.MetronSeriesId,
        Method = method,
        State = state,
        LastCheckedUtc = checkedUtc,
    };

    /// <summary>
    /// For each reference, the issue that already answers for it without creating anything: the comic the engine would link, else the
    /// placeholder already standing in for it, else null. One library read for the whole batch.
    /// </summary>
    public static IReadOnlyList<int?> FindExisting(PaperbunkrDbContext context, IReadOnlyList<ComicReference> references)
    {
        var library = LibraryIndex.Load(context);
        PlaceholderPool? placeholders = null;
        return references
            .Select(reference => MatchEngine.Match(reference, library).AutoLink?.Id
                ?? (placeholders ??= new PlaceholderPool(context, library)).Find(reference)?.Id)
            .ToList();
    }

    /// <summary>What a stored source record asked for, as the engine takes it.</summary>
    public static ComicReference ToReference(ReadingListItemSource source) => new(
        source.Series, source.Number, source.Volume, source.Year, source.Format, source.Title, source.SourceKey, source.SourceEntryId,
        source.ComicVineIssueId, source.ComicVineSeriesId, source.MetronIssueId, source.MetronSeriesId);

    /// <summary>
    /// Finds or stages the placeholder for a comic the library does not have. Loaded once per import: the existing placeholders and the
    /// series rows, by key.
    /// </summary>
    internal sealed class PlaceholderPool
    {
        private readonly PaperbunkrDbContext _context;
        private readonly LibraryIndex _library;
        private readonly Dictionary<(string SeriesKey, string NumberKey), List<Issue>> _existing = new();
        private readonly Dictionary<string, List<Series>> _seriesByKey = new(StringComparer.Ordinal);
        private readonly HashSet<Issue> _handedOut = new();

        public PlaceholderPool(PaperbunkrDbContext context, LibraryIndex library)
        {
            _context = context;
            _library = library;
            foreach (var series in context.Series.Where(s => s.RemoteSourceId == null).OrderBy(s => s.Id))
            {
                string key = SeriesKeys.Of(series.Name);
                if (!_seriesByKey.TryGetValue(key, out var rows))
                {
                    _seriesByKey[key] = rows = new List<Series>();
                }

                rows.Add(series);
            }

            foreach (var placeholder in context.Issues.Include(i => i.Series).Where(i => i.IsPlaceholder))
            {
                Remember(placeholder, SeriesKeys.Of(placeholder.Series?.Name));
            }
        }

        /// <summary>The placeholder already standing in for this comic, if any.</summary>
        public Issue? Find(ComicReference reference)
        {
            if (!_existing.TryGetValue((reference.SeriesKey, reference.NumberKey), out var candidates))
            {
                return null;
            }

            // The same number of the same title from another decade is another comic; an unknown year fits either.
            // One handed out earlier in this import belongs to a different entry (the resolver already set real duplicates aside).
            return candidates.FirstOrDefault(p => !_handedOut.Contains(p)
                && (reference.Year is not int wanted || p.Year is not int year || Math.Abs(wanted - year) <= 1));
        }

        public Issue For(ComicReference reference)
        {
            string seriesKey = reference.SeriesKey;
            if (Find(reference) is { } same)
            {
                _handedOut.Add(same);
                return same;
            }

            var placeholder = new Issue
            {
                Series = SeriesFor(reference, seriesKey),
                Number = reference.Number,
                Volume = reference.Volume,
                Year = reference.Year,
                Format = reference.Format,
                FileIsMissing = true,
                IsPlaceholder = true,
            };
            _context.Issues.Add(placeholder);
            Remember(placeholder, seriesKey);
            _handedOut.Add(placeholder);
            return placeholder;
        }

        /// <summary>The row that owns issues under this name if there is one, else any row with the name, else a new one - never a second
        /// row beside one that exists (the duplicate rows the old first-match lookup could not see past).</summary>
        private Series SeriesFor(ComicReference reference, string seriesKey)
        {
            _seriesByKey.TryGetValue(seriesKey, out var rows);
            int? owningId = _library.InSeries(seriesKey).FirstOrDefault()?.SeriesId;
            var series = rows?.FirstOrDefault(s => s.Id == owningId) ?? rows?.FirstOrDefault();
            if (series is not null)
            {
                return series;
            }

            series = new Series { Name = reference.Series, SortName = reference.Series };
            _context.Series.Add(series);
            _seriesByKey[seriesKey] = new List<Series> { series };
            return series;
        }

        private void Remember(Issue placeholder, string seriesKey)
        {
            var key = (seriesKey, IssueNumberKey.Of(placeholder.Number));
            if (!_existing.TryGetValue(key, out var list))
            {
                _existing[key] = list = new List<Issue>();
            }

            list.Add(placeholder);
        }
    }
}
