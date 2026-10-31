using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Turns a plugin metadata provider's answer into review-queue proposals (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-
/// pitch-completion-design.md §C). Unlike a built-in provider link, nothing is applied and nothing is stored as a link: each field the plugin
/// supplied that differs from the series becomes a <b>Pending</b> proposal (source <see cref="MetadataProposalSource.Other"/>), so the user
/// approves every change. A status the app doesn't recognise is dropped rather than proposed as Unknown; the same pending value isn't
/// proposed twice.
/// </summary>
public static class PluginMetadataProposals
{
    public static int Create(PaperbunkrDbContext context, int seriesId, PluginSeriesMetadata metadata)
    {
        var series = context.Series.Find(seriesId);
        if (series is null)
        {
            return 0;
        }

        string? status = string.IsNullOrWhiteSpace(metadata.Status) ? null : SeriesStatusNormalizer.Normalize(metadata.Status) is var s && s != SeriesStatus.Unknown ? s.ToString() : null;
        string? genre = metadata.Genres.Count == 0 ? null : string.Join(", ", metadata.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

        int added = 0;
        added += Propose(context, series, MetadataProposalField.Summary, series.Summary, metadata.Description);
        added += Propose(context, series, MetadataProposalField.Status, series.Status.ToString(), status);
        added += Propose(context, series, MetadataProposalField.Genre, series.Genre, genre);
        added += Propose(context, series, MetadataProposalField.Publisher, series.Publisher, metadata.Publisher);
        context.SaveChanges();
        return added;
    }

    private static int Propose(PaperbunkrDbContext context, Series series, MetadataProposalField field, string? current, string? proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed) || string.Equals(current?.Trim(), proposed.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        bool alreadyOffered = context.MetadataProposals.Any(p =>
            p.SeriesId == series.Id && p.Field == field && p.Source == MetadataProposalSource.Other
            && p.Status == MetadataProposalStatus.Pending && p.ProposedValue == proposed);
        if (alreadyOffered)
        {
            return 0;
        }

        context.MetadataProposals.Add(new MetadataProposal
        {
            SeriesId = series.Id,
            Field = field,
            CurrentValue = current,
            ProposedValue = proposed.Trim(),
            Source = MetadataProposalSource.Other,
            Confidence = 0.8m,
            Status = MetadataProposalStatus.Pending,
        });
        return 1;
    }
}
