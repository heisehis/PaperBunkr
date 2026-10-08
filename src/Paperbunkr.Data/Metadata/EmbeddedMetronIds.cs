using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The provider ids in a comic's embedded <c>MetronInfo.xml</c> (docs/superpowers/specs/2026-10-05-
/// metroninfo-write-back-design.md, D12). <see cref="EmbeddedComicInfoReader"/> already falls back to
/// that file for a book with no <c>ComicInfo.xml</c>, but the result is a <see cref="ComicInfo"/>,
/// which has nowhere to put an id - so a file Metron-Tagger identified used to arrive unlinked.
///
/// Read straight from <c>.cbz</c> and image folders only, the formats the write side produces; the
/// archive engines have no "give me this entry" call for the rest.
/// </summary>
public sealed record EmbeddedMetronIds(
    IReadOnlyDictionary<ComicProvider, int> IssueIds,
    int? GcdIssueId,
    ComicProvider? Primary,
    int? PrimarySeriesId,
    string? SeriesName)
{
    public static EmbeddedMetronIds? TryRead(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                string entryPath = Path.Combine(path, "MetronInfo.xml");
                if (!File.Exists(entryPath))
                {
                    return null;
                }

                using var file = File.OpenRead(entryPath);
                return FromDocument(MetronInfo.TryRead(file));
            }

            if (!Path.GetExtension(path).Equals(".cbz", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                return null;
            }

            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "MetronInfo.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            return FromDocument(MetronInfo.TryRead(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Null when the document names no id Paperbunkr has a home for.</summary>
    public static EmbeddedMetronIds? FromDocument(MetronInfo? document)
    {
        if (document is null)
        {
            return null;
        }

        var issueIds = new Dictionary<ComicProvider, int>();
        int? gcdIssueId = null;
        ComicProvider? primary = null;
        foreach (var id in document.Ids)
        {
            // Every id we store is a whole number; a slug or uuid is some other source's shape.
            if (!int.TryParse(id.Value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                continue;
            }

            ComicProvider? provider = id.Source switch
            {
                InformationSource.Metron => ComicProvider.Metron,
                InformationSource.ComicVine => ComicProvider.ComicVine,
                _ => null,
            };

            if (provider is ComicProvider known)
            {
                issueIds.TryAdd(known, value);
                if (id.PrimarySpecified && id.Primary)
                {
                    primary ??= known;
                }
            }
            else if (id.Source == InformationSource.GrandComicsDatabase)
            {
                gcdIssueId ??= value;
            }
        }

        if (issueIds.Count == 0 && gcdIssueId is null)
        {
            return null;
        }

        // Series id= is "the identification number from the source of information", i.e. the primary
        // ID's source. Without a primary of ours there is no telling whose number it is.
        int? seriesId = primary is not null
            && int.TryParse(document.Series?.Id?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : null;

        return new EmbeddedMetronIds(issueIds, gcdIssueId, primary, seriesId, document.Series?.Name);
    }

    /// <summary>
    /// Links <paramref name="issue"/> (already saved, so it has an id) to these ids. Only fills gaps: a
    /// provider the issue or its series is already linked to, or a GCD id it already has, wins over
    /// the file. Saves as it goes.
    /// </summary>
    public void ApplyTo(PaperbunkrDbContext context, Issue issue)
    {
        foreach (var (provider, externalId) in IssueIds)
        {
            if (!HasLink(context, ComicMetadataEntityKind.Issue, issue.Id, provider))
            {
                ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Issue, provider, issue.Id, externalId);
            }
        }

        // The series id belongs to the series the file names. The issue may have been filed under a
        // different one (a filename-derived reassignment), which must not inherit it.
        if (Primary is ComicProvider primary && PrimarySeriesId is int seriesId
            && context.Series.Where(s => s.Id == issue.SeriesId).Select(s => s.Name).FirstOrDefault() is { } seriesName
            && !string.IsNullOrWhiteSpace(SeriesName) && TitleNormalizer.NamesMatch(seriesName, SeriesName)
            && !HasLink(context, ComicMetadataEntityKind.Series, issue.SeriesId, primary))
        {
            ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Series, primary, issue.SeriesId, seriesId);
        }

        if (GcdIssueId is int gcdIssueId && issue.GcdIssueId is null)
        {
            issue.GcdIssueId = gcdIssueId;
            context.SaveChanges();
        }
    }

    private static bool HasLink(PaperbunkrDbContext context, ComicMetadataEntityKind kind, int entityId, ComicProvider provider) =>
        context.ComicMetadataExternalIds.Any(e => e.EntityKind == kind && e.EntityId == entityId && e.Provider == provider);
}
