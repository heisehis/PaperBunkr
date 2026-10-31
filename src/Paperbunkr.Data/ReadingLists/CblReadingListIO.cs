using System.Xml;
using System.Xml.Linq;
using cYo.Projects.ComicRack.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// .cbl import/export (docs/superpowers/specs/2026-08-06-reading-lists-design.md §4). Reuses
/// <see cref="ComicReadingListContainer"/>/<see cref="ComicReadingListItem"/> directly — the real,
/// already-ported <c>XmlSerializer</c>-based CBL format (Paperbunkr.Engine/Database), dormant since
/// the retarget spike. No new parser needed.
/// </summary>
public static class CblReadingListIO
{
    /// <summary>Stored on <see cref="ReadingListItemSource.SourceKey"/> for entries that came from a .cbl file.</summary>
    public const string SourceKey = "cbl";

    public static ReadingList Import(PaperbunkrDbContext context, string filePath) => ImportWithReport(context, filePath).List;

    /// <summary>
    /// Imports the list and says what became of every entry (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.6-§5.8).
    /// Entries are matched through <see cref="ReadingListResolver"/>; a ComicVine or Metron id the file carries for a book is used first.
    /// </summary>
    public static (ReadingList List, ImportReport Report) ImportWithReport(PaperbunkrDbContext context, string filePath)
    {
        var prepared = Prepare(filePath);
        var resolution = ReadingListResolver.Resolve(context, prepared.References);
        var list = prepared.CreateList();
        int sortOrder = 0;
        foreach (var resolved in ReadingListResolver.Materialize(context, resolution))
            list.Items.Add(new ReadingListItem { Issue = resolved.Issue, SortOrder = sortOrder++, Source = resolved.Source });
        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, null);
        ReadingListManager.RecordCreatedWithItems(context, list);
        context.SaveChanges();
        return (list, resolution.Report(reported: prepared.Reported));
    }

    public static PreparedReadingList Prepare(string filePath)
    {
        var container = ComicReadingListContainer.Deserialize(filePath)
            ?? throw new InvalidDataException($"'{filePath}' is not a valid CBL reading list.");

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = string.IsNullOrEmpty(container.Name) ? Path.GetFileNameWithoutExtension(filePath) : container.Name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var (ids, reported) = ReadProviderIds(filePath, container.Items.Count);
        var references = container.Items
            .Select((item, index) => new ComicReference(
                item.Series,
                item.Number,
                item.Volume < 0 ? null : item.Volume.ToString(),
                item.Year < 0 ? null : item.Year,
                string.IsNullOrEmpty(item.Format) ? null : item.Format,
                SourceKey: SourceKey,
                ComicVineIssueId: ids?[index].ComicVineIssue,
                ComicVineSeriesId: ids?[index].ComicVineSeries,
                MetronIssueId: ids?[index].MetronIssue,
                MetronSeriesId: ids?[index].MetronSeries))
            .ToList();

        return new PreparedReadingList(list.Name, references) { Reported = reported };
    }

    /// <summary>
    /// The provider ids community-made lists carry per book, which ComicRack's own format has no place for and its serializer ignores:
    /// <c>&lt;Book ...&gt;&lt;Database Name="cv" Series="20346" Issue="121607" /&gt;&lt;/Book&gt;</c> (checked against the files of the public
    /// CBL-ReadingLists collection), plus the list's own <c>&lt;NumIssues&gt;</c>. A file without them, or one whose books do not line up with
    /// what the serializer read, yields nothing and imports exactly as before.
    /// </summary>
    private static ((string? ComicVineSeries, string? ComicVineIssue, string? MetronSeries, string? MetronIssue)[]? Ids, int? Reported) ReadProviderIds(
        string filePath, int expectedBooks)
    {
        try
        {
            var root = XDocument.Load(filePath).Root;
            int? reported = int.TryParse(root?.Element("NumIssues")?.Value, out int n) && n >= 0 ? n : null;
            var books = root?.Element("Books")?.Elements("Book").ToList();
            if (books is null || books.Count != expectedBooks)
            {
                return (null, reported);
            }

            var ids = books.Select(book =>
            {
                (string? cvSeries, string? cvIssue, string? metronSeries, string? metronIssue) = (null, null, null, null);
                foreach (var database in book.Elements("Database"))
                {
                    string? series = Id(database.Attribute("Series")?.Value);
                    string? issue = Id(database.Attribute("Issue")?.Value);
                    switch (database.Attribute("Name")?.Value.Trim().ToLowerInvariant())
                    {
                        case "cv" or "comicvine":
                            (cvSeries, cvIssue) = (series, issue);
                            break;
                        case "metron":
                            (metronSeries, metronIssue) = (series, issue);
                            break;
                    }
                }

                return (cvSeries, cvIssue, metronSeries, metronIssue);
            }).ToArray();

            return (ids, reported);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static string? Id(string? value) => long.TryParse(value?.Trim(), out long id) && id > 0 ? id.ToString() : null;

    public static void Export(PaperbunkrDbContext context, int readingListId, string filePath)
    {
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .First(r => r.Id == readingListId);

        // CE: Settings.ExportedListsContainFilenames (docs/superpowers/specs/2026-09-13-preferences-
        // cosmetic-toggles-design.md) - export-only, CE itself never reads this on import either.
        bool includeFilenames = context.GetOrCreateAppSettings().ExportedListsContainFilenames;

        var container = new ComicReadingListContainer { Name = list.Name };
        foreach (var item in list.Items.OrderBy(i => i.SortOrder))
        {
            var issue = item.Issue!;
            container.Items.Add(new ComicReadingListItem
            {
                Series = issue.Series?.Name ?? string.Empty,
                Number = issue.EffectiveNumber() ?? string.Empty,
                Volume = int.TryParse(issue.EffectiveVolume(), out var volumeNumber) ? volumeNumber : -1,
                Year = issue.EffectiveYear() ?? -1,
                Format = issue.Format ?? string.Empty,
                FileName = includeFilenames ? (issue.FilePath ?? string.Empty) : string.Empty,
            });
        }

        container.Serialize(filePath);
    }
}
