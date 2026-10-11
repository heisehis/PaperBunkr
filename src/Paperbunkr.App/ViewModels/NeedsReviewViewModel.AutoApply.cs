using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Metadata Proposals section's own settings and its confidence-gated bulk accept (docs/superpowers/specs/
/// 2026-10-06-smart-features-design.md §6.1): whether new proposals apply themselves at all (the policy, which had no control
/// anywhere before), the confidence they need to do so, and "Accept all at or above X%" for the ones left waiting.
/// </summary>
public partial class NeedsReviewViewModel
{
    private bool _loadingAutoApplySettings;
    private TwoStepConfirm? _acceptAboveThresholdConfirm;

    /// <summary>The <see cref="MetadataResolutionPolicy"/>: on = Automatic (new proposals apply themselves), off = Prompt (all wait for review).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoApplyThresholdLabel))]
    private bool _applyProposalsAutomatically = true;

    /// <summary><see cref="AppSettings.AutoApplyMinConfidence"/> as a whole percent, 0-100. 0 = any confidence (the behaviour before the threshold existed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoApplyThresholdLabel), nameof(HasThresholdAccept), nameof(AcceptAboveThresholdConfirm))]
    private int _autoApplyThresholdPercent;

    public string AutoApplyThresholdLabel => !ApplyProposalsAutomatically
        ? "Nothing applies itself"
        : AutoApplyThresholdPercent == 0 ? "Any confidence" : $"{AutoApplyThresholdPercent}% or more";

    /// <summary>"Accept all at or above X%" is only offered with a threshold set and something pending.</summary>
    public bool HasThresholdAccept => AutoApplyThresholdPercent > 0 && HasPendingProposalItems;

    /// <summary>Rebuilt when the threshold changes, since its label names the threshold.</summary>
    public TwoStepConfirm AcceptAboveThresholdConfirm =>
        _acceptAboveThresholdConfirm ??= new TwoStepConfirm(AcceptAboveThreshold, $"Accept all at or above {AutoApplyThresholdPercent}%", "Confirm accept?");

    /// <summary>Reads the two settings into the controls without writing them straight back.</summary>
    private void ApplyAutoApplySettings(MetadataResolutionPolicy policy, decimal minConfidence)
    {
        _loadingAutoApplySettings = true;
        try
        {
            ApplyProposalsAutomatically = policy == MetadataResolutionPolicy.Automatic;
            AutoApplyThresholdPercent = (int)Math.Round(MetadataAutoApply.ClampThreshold(minConfidence) * 100m);
        }
        finally
        {
            _loadingAutoApplySettings = false;
        }
    }

    partial void OnApplyProposalsAutomaticallyChanged(bool value) => SaveAutoApplySettings();

    partial void OnAutoApplyThresholdPercentChanged(int value)
    {
        _acceptAboveThresholdConfirm = null;
        SaveAutoApplySettings();
    }

    /// <summary>Every change persists at once, like the rest of Preferences.</summary>
    private void SaveAutoApplySettings()
    {
        if (_loadingAutoApplySettings)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var settings = context.GetOrCreateAppSettings();
        settings.MetadataResolutionPolicy = ApplyProposalsAutomatically ? MetadataResolutionPolicy.Automatic : MetadataResolutionPolicy.Prompt;
        settings.AutoApplyMinConfidence = Math.Clamp(AutoApplyThresholdPercent, 0, 100) / 100m;
        context.SaveChanges();
    }

    /// <summary>
    /// Accepts every pending proposal whose confidence is at or above the threshold and leaves the rest waiting. The comparison runs
    /// here, not in SQL: SQLite stores a decimal as text and cannot order it.
    /// </summary>
    private void AcceptAboveThreshold()
    {
        decimal threshold = Math.Clamp(AutoApplyThresholdPercent, 0, 100) / 100m;
        DateTime? now = DateTime.UtcNow;
        List<int> writeTimeIds;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var confident = context.MetadataProposals
                .Where(p => p.Status == MetadataProposalStatus.Pending)
                .Select(p => new { p.Id, p.Confidence, p.Field, p.SeriesId })
                .ToList()
                .Where(p => p.Confidence >= threshold)
                .ToList();

            // Write-time proposals (a Series-field one moves the issue, a series-scoped one writes its Series field) go one by one.
            writeTimeIds = confident.Where(p => p.Field == MetadataProposalField.Series || p.SeriesId != null).Select(p => p.Id).ToList();
            var plainIds = confident.Select(p => p.Id).Except(writeTimeIds).ToList();
            foreach (var chunk in plainIds.Chunk(500))
            {
                context.MetadataProposals
                    .Where(p => chunk.Contains(p.Id))
                    .ExecuteUpdate(s => s
                        .SetProperty(p => p.Status, MetadataProposalStatus.Accepted)
                        .SetProperty(p => p.ResolvedAt, now)
                        .SetProperty(p => p.ReviewedAt, now));
            }
        }

        foreach (int id in writeTimeIds)
        {
            ResolveProposal(id, accept: true);
        }

        FinishProposalBulkAction(MetadataProposalStatus.Pending, null);
    }
}
