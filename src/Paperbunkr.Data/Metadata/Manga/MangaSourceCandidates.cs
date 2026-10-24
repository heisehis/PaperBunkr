using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>A value a provider offered for a series that has not been applied: shown next to what the series holds now.</summary>
public sealed record MangaCandidate(
    int ProposalId,
    MetadataProposalField Field,
    string FieldLabel,
    string Current,
    string Proposed,
    ExternalMetadataProvider? Provider,
    bool IsLocked,
    bool NeedsRole);

/// <summary>A provider this series is linked to (separate from a tracker link: this is where metadata comes from, not whose reading list it appears on).</summary>
public sealed record MangaProviderLink(ExternalMetadataProvider Provider, string ExternalId, string? Url, DateTime? LastFetchedAt);

/// <summary>
/// Provider candidates for a manga series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §11): the pending series-level proposals a refresh left because the
/// field is locked (or, for credits, because a provider only gives a joined name list and the role is the user's call), shown against the current value. Accepting one
/// goes through the same save path as typing it, so chapters, snapshots and the file queue all behave the same. Nothing here changes a series without an explicit Accept.
/// </summary>
public sealed class MangaSourceCandidates
{
    private static readonly MetadataProposalField[] Fields =
    {
        MetadataProposalField.Summary, MetadataProposalField.Status, MetadataProposalField.Genre, MetadataProposalField.Creator,
        MetadataProposalField.Publisher, MetadataProposalField.Imprint, MetadataProposalField.AgeRating,
        MetadataProposalField.Writer, MetadataProposalField.Penciller, MetadataProposalField.ReadingMode,
    };

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MangaSeriesMetadataService _service;

    public MangaSourceCandidates(Func<PaperbunkrDbContext> contextFactory, MangaSeriesMetadataService service)
    {
        _contextFactory = contextFactory;
        _service = service;
    }

    public IReadOnlyList<MangaProviderLink> Links(int seriesId)
    {
        using var context = _contextFactory();
        return context.ExternalMediaIds.AsNoTracking().Where(e => e.SeriesId == seriesId).OrderBy(e => e.Provider)
            .AsEnumerable()
            .Select(e => new MangaProviderLink(e.Provider, e.ExternalId, e.Url, e.LastFetchedAt))
            .ToList();
    }

    public IReadOnlyList<MangaCandidate> List(int seriesId)
    {
        using var context = _contextFactory();
        var series = context.Series.AsNoTracking().Include(s => s.Credits).Include(s => s.SeriesTags).AsSplitQuery().FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return Array.Empty<MangaCandidate>();
        }

        var locked = MangaFieldLocks.Parse(series.LockedFields);
        var pending = context.MetadataProposals.AsNoTracking()
            .Where(p => p.SeriesId == seriesId && p.Status == MetadataProposalStatus.Pending && p.ProposedValue != null)
            .OrderByDescending(p => p.CreatedAt)
            .AsEnumerable()
            .Where(p => Fields.Contains(p.Field))
            .ToList();

        var rows = new List<MangaCandidate>();
        foreach (var proposal in pending)
        {
            string proposed = proposal.ProposedValue!;
            string current = CurrentText(series, proposal.Field);
            if (current.Length == 0 && !string.IsNullOrWhiteSpace(proposal.CurrentValue))
            {
                // A series not confirmed in Edit Series yet holds nothing itself; what its chapters said when this was offered is what it is being compared with.
                current = proposal.CurrentValue;
            }

            if (string.Equals(current.Trim(), proposed.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;   // nothing to decide: the series already holds it
            }

            rows.Add(new MangaCandidate(
                proposal.Id, proposal.Field, Label(proposal.Field), current, proposed, proposal.ProviderKey,
                IsLocked: LockKey(proposal.Field) is { } key && locked.Contains(key),
                NeedsRole: proposal.Field == MetadataProposalField.Creator));
        }

        return rows;
    }

    /// <summary>Applies one candidate. <paramref name="creditRole"/> says which credit a joined author/artist list goes under (default Writer); it is only used for a credit candidate.</summary>
    public MangaSaveResult Accept(int proposalId, string? creditRole = null)
    {
        int seriesId;
        MetadataProposalField field;
        string proposed;
        ExternalMetadataProvider? provider;
        using (var context = _contextFactory())
        {
            var proposal = context.MetadataProposals.AsNoTracking().FirstOrDefault(p => p.Id == proposalId && p.Status == MetadataProposalStatus.Pending);
            if (proposal?.SeriesId is not int sid || proposal.ProposedValue is null)
            {
                return MangaSaveResult.Nothing;
            }

            seriesId = sid;
            field = proposal.Field;
            proposed = proposal.ProposedValue;
            provider = proposal.ProviderKey;
        }

        var edit = _service.Load(seriesId);
        if (edit is null || edit.IsRemote)
        {
            return MangaSaveResult.Nothing;
        }

        switch (field)
        {
            case MetadataProposalField.Summary:
                edit.Summary = proposed;
                break;
            case MetadataProposalField.Status:
                if (Enum.TryParse<SeriesStatus>(proposed, out var status))
                {
                    edit.Status = status;
                }

                break;
            case MetadataProposalField.Genre:
                edit.Values.Genres.Clear();
                foreach (string name in CreatorResolver.ParseNames(proposed))
                {
                    edit.Values.Genres.Add(new MangaTagValue(name, "Genre", IssueTagWeight.Unset));
                }

                break;
            case MetadataProposalField.Creator:
                string role = MangaInheritedFields.CreditRoles.Contains(creditRole ?? string.Empty) ? creditRole! : "Writer";
                var names = edit.Values.Credits.GetValueOrDefault(role)?.ToList() ?? new List<string>();
                foreach (string name in CreatorResolver.ParseNames(proposed))
                {
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }

                edit.Values.Credits[role] = names;
                break;
            case MetadataProposalField.Publisher:
                edit.Values.Text[MangaInheritedFields.Publisher] = proposed;
                break;
            case MetadataProposalField.Imprint:
                edit.Values.Text[MangaInheritedFields.Imprint] = proposed;
                break;
            case MetadataProposalField.AgeRating:
                edit.Values.Text[MangaInheritedFields.AgeRating] = proposed;
                break;
            case MetadataProposalField.Writer:
                edit.Values.Credits["Writer"] = CreatorResolver.ParseNames(proposed).ToList();
                break;
            case MetadataProposalField.Penciller:
                edit.Values.Credits["Penciller"] = CreatorResolver.ParseNames(proposed).ToList();
                break;
            case MetadataProposalField.ReadingMode:
                if (Enum.TryParse<ReadingMode>(proposed, out var mode))
                {
                    edit.ReadingMode = mode;
                }

                break;
            default:
                return MangaSaveResult.Nothing;
        }

        edit.AppliedFromProvider = provider ?? ExternalMetadataProvider.MangaBaka;
        var result = _service.Save(edit, MangaExceptionChoice.KeepAsExceptions);

        // Resolved whether or not the value differed: the question has been answered.
        using (var context = _contextFactory())
        {
            var proposal = context.MetadataProposals.FirstOrDefault(p => p.Id == proposalId);
            if (proposal is not null)
            {
                proposal.Status = MetadataProposalStatus.Accepted;
                proposal.ResolvedAt = DateTime.UtcNow;
                proposal.ReviewedAt = DateTime.UtcNow;
                context.SaveChanges();
            }
        }

        return result;
    }

    public void Reject(int proposalId)
    {
        using var context = _contextFactory();
        var proposal = context.MetadataProposals.FirstOrDefault(p => p.Id == proposalId && p.Status == MetadataProposalStatus.Pending);
        if (proposal is null)
        {
            return;
        }

        proposal.Status = MetadataProposalStatus.Rejected;
        proposal.ReviewedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    private static string Label(MetadataProposalField field) => field switch
    {
        MetadataProposalField.Summary => "Summary",
        MetadataProposalField.Status => "Publication status",
        MetadataProposalField.Genre => "Genres",
        MetadataProposalField.Creator => "Authors and artists",
        MetadataProposalField.AgeRating => "Age rating",
        MetadataProposalField.Writer => "Authors (Writer)",
        MetadataProposalField.Penciller => "Artists (Penciller)",
        MetadataProposalField.ReadingMode => "Reading mode",
        _ => field.ToString(),
    };

    private static string? LockKey(MetadataProposalField field) => field switch
    {
        MetadataProposalField.Summary => MangaFieldLocks.SummaryKey,
        MetadataProposalField.Status => MangaFieldLocks.StatusKey,
        MetadataProposalField.Genre => MangaInheritedFields.Genre,
        MetadataProposalField.Publisher => MangaInheritedFields.Publisher,
        MetadataProposalField.Imprint => MangaInheritedFields.Imprint,
        MetadataProposalField.AgeRating => MangaInheritedFields.AgeRating,
        MetadataProposalField.Writer => "Writer",
        MetadataProposalField.Penciller => "Penciller",
        _ => null,
    };

    private static string CurrentText(Series series, MetadataProposalField field) => field switch
    {
        MetadataProposalField.Summary => series.Summary ?? string.Empty,
        MetadataProposalField.Status => series.Status.ToString(),
        MetadataProposalField.Genre => string.Join(", ", series.SeriesTags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value)) is { Length: > 0 } tags ? tags : series.Genre ?? string.Empty,
        MetadataProposalField.Creator => string.Join("; ", series.Credits.OrderBy(c => c.Role).ThenBy(c => c.SortOrder).Select(c => $"{c.Role}: {c.Name}")),
        MetadataProposalField.Publisher => series.Publisher ?? string.Empty,
        MetadataProposalField.Imprint => series.Imprint ?? string.Empty,
        MetadataProposalField.AgeRating => series.AgeRating ?? string.Empty,
        MetadataProposalField.Writer => string.Join(", ", series.Credits.Where(c => c.Role == "Writer").OrderBy(c => c.SortOrder).Select(c => c.Name)),
        MetadataProposalField.Penciller => string.Join(", ", series.Credits.Where(c => c.Role == "Penciller").OrderBy(c => c.SortOrder).Select(c => c.Name)),
        MetadataProposalField.ReadingMode => series.ReadingMode.ToString(),
        _ => string.Empty,
    };
}
