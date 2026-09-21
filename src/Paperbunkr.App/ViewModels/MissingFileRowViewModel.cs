using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One Needs Review "Missing Files" row, offering three remediation actions
/// (docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §2): Relink (pick a new file),
/// Remove from library (delete the underlying <c>Issue</c> row, guarded by <see cref="TwoStepConfirm"/> -
/// this row is where that pattern originated, now shared) and Dismiss (acknowledge and stop asking,
/// without touching the data).
/// </summary>
/// <summary>How serious a Library Health row is - drives the row's severity chip (docs/superpowers/
/// specs/2026-09-21-cosmetics-pitch-design.md #5): Warning is amber, Error is red.</summary>
public enum HealthSeverity
{
    Warning,
    Error,
}

public partial class MissingFileRowViewModel : ViewModelBase
{
    private readonly Func<MissingFileRowViewModel, Task> _onRelink;
    private readonly Action<MissingFileRowViewModel> _onDismiss;

    public MissingFileRowViewModel(
        int issueId,
        string displayLabel,
        Func<MissingFileRowViewModel, Task> onRelink,
        Action<MissingFileRowViewModel> onRemove,
        Action<MissingFileRowViewModel> onDismiss,
        HealthSeverity severity = HealthSeverity.Warning,
        string severityLabel = "Missing")
    {
        IssueId = issueId;
        Severity = severity;
        SeverityLabel = severityLabel;
        DisplayLabel = displayLabel;
        _onRelink = onRelink;
        _onDismiss = onDismiss;
        DeleteConfirm = new TwoStepConfirm(() => onRemove(this));
    }

    public int IssueId { get; }

    public string DisplayLabel { get; }

    /// <summary>Severity chip data - previously baked into <see cref="DisplayLabel"/> as a text suffix.</summary>
    public HealthSeverity Severity { get; }

    public string SeverityLabel { get; }

    public bool IsError => Severity == HealthSeverity.Error;

    public bool IsWarning => Severity == HealthSeverity.Warning;

    public TwoStepConfirm DeleteConfirm { get; }

    [RelayCommand]
    private async Task Relink()
    {
        DeleteConfirm.Cancel();
        await _onRelink(this);
    }

    [RelayCommand]
    private void Dismiss()
    {
        DeleteConfirm.Cancel();
        _onDismiss(this);
    }
}
