using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Timeline mode's "Review inferred ages" panel (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md) - an issue whose comic age is currently only
/// inferred from its year. Accept writes the CE-style label into <c>Issue.BookAge</c>, making it
/// authoritative.
/// </summary>
public partial class InferredAgeRowViewModel : ViewModelBase
{
    private readonly Action<InferredAgeRowViewModel> _onAccept;

    public InferredAgeRowViewModel(InferredAgeRow row, Action<InferredAgeRowViewModel> onAccept)
    {
        _onAccept = onAccept;
        IssueId = row.Issue.Id;
        Age = row.Age;
        DisplayLabel = $"{row.Issue.Series?.Name ?? "Unknown"} #{row.Issue.EffectiveNumber()}";
        AgeLabel = ComicAgeCatalog.All[row.Age].DisplayName;
        YearLabel = row.Issue.Year?.ToString() ?? "?";
        IsReducedConfidence = row.Confidence is > 0m and < 1.0m;
        ConfidenceReason = row.Reason;
    }

    public int IssueId { get; }
    public ComicAge Age { get; }
    public string DisplayLabel { get; }
    public string AgeLabel { get; }
    public string YearLabel { get; }
    public bool IsReducedConfidence { get; }
    public string? ConfidenceReason { get; }

    [RelayCommand]
    private void Accept() => _onAccept(this);
}
