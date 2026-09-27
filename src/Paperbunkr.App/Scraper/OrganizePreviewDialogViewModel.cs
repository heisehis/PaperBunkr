using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Backs <see cref="OrganizePreviewDialogView"/>: what an Organize is about to do, shown before any file moves
/// (docs/superpowers/specs/2026-09-25-library-organizer-audit-fixes-design.md, Phase 3). The plugin's own Simulate mode only wrote a log;
/// here the same information is on screen first, with a Confirm/Cancel. A Simulate run shows this and offers Close only.
/// </summary>
public sealed partial class OrganizePreviewDialogViewModel
{
    private readonly Action<bool> _resolve;

    public OrganizePreviewDialogViewModel(OrganizePlanSummary summary, OrganizerProfile profile, Action<bool> resolve)
        : this(summary, new[] { profile }, resolve)
    {
    }

    public OrganizePreviewDialogViewModel(OrganizePlanSummary summary, IReadOnlyList<OrganizerProfile> profiles, Action<bool> resolve)
    {
        _resolve = resolve;
        var profile = profiles[0];
        bool severalProfiles = profiles.Count > 1;
        IsSimulation = profiles.All(p => p.Mode == OrganizerMode.Simulate);
        Title = IsSimulation
            ? "Simulation - nothing will be moved"
            : severalProfiles
                ? $"Organize {summary.Moving} comic{(summary.Moving == 1 ? string.Empty : "s")} with {profiles.Count} profiles?"
                : $"{(profile.Mode == OrganizerMode.Copy ? "Copy" : "Move")} {summary.Moving} comic{(summary.Moving == 1 ? string.Empty : "s")}?";
        Subtitle = severalProfiles
            ? string.Join("; ", profiles.Select(p => $"{p.Name} ({p.Mode})"))
            : $"Profile \"{profile.Name}\" into {profile.BaseFolder}";
        Counts = BuildCounts(summary);
        Sample = summary.Sample;
        SampleNote = summary.Moving > summary.Sample.Count ? $"Showing the first {summary.Sample.Count} of {summary.Moving}." : string.Empty;
        ProblemMessages = summary.ProblemMessages;
        HasProblems = summary.Problems > 0;
        ConfirmLabel = severalProfiles ? "Organize" : profile.Mode == OrganizerMode.Copy ? "Copy" : "Move";
        CancelLabel = IsSimulation ? "Close" : "Cancel";
        HasSample = summary.Sample.Count > 0;
    }

    public string CancelLabel { get; }

    public bool HasSample { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public bool IsSimulation { get; }

    public string ConfirmLabel { get; }

    public IReadOnlyList<string> Counts { get; }

    public IReadOnlyList<PlanPreviewRow> Sample { get; }

    public string SampleNote { get; }

    public bool HasProblems { get; }

    public IReadOnlyList<string> ProblemMessages { get; }

    private static IReadOnlyList<string> BuildCounts(OrganizePlanSummary s)
    {
        var lines = new List<string> { $"{s.Moving} to process" };
        if (s.Collisions > 0)
        {
            lines.Add($"{s.Collisions} would overwrite or duplicate a file that is already there (you will be asked)");
        }

        if (s.AlreadyInPlace > 0)
        {
            lines.Add($"{s.AlreadyInPlace} already in the right place");
        }

        if (s.Skipped > 0)
        {
            lines.Add($"{s.Skipped} left alone (a required field is empty, or a later profile places it)");
        }

        if (s.Problems > 0)
        {
            lines.Add($"{s.Problems} cannot be organized (see below)");
        }

        return lines;
    }

    [RelayCommand]
    private void Confirm() => _resolve(true);

    [RelayCommand]
    private void Cancel() => _resolve(false);
}
