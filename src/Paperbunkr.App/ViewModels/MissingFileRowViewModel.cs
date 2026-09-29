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
    private readonly Action<MissingFileRowViewModel>? _onRestore;

    public MissingFileRowViewModel(
        int issueId,
        string displayLabel,
        Func<MissingFileRowViewModel, Task> onRelink,
        Action<MissingFileRowViewModel> onRemove,
        Action<MissingFileRowViewModel> onDismiss,
        HealthSeverity severity = HealthSeverity.Warning,
        string severityLabel = "Missing",
        Action<MissingFileRowViewModel>? onRestore = null)
    {
        IssueId = issueId;
        Severity = severity;
        SeverityLabel = severityLabel;
        DisplayLabel = displayLabel;
        _onRelink = onRelink;
        _onDismiss = onDismiss;
        _onRestore = onRestore;
        DeleteConfirm = new TwoStepConfirm(() => onRemove(this));
    }

    public int IssueId { get; }

    public string DisplayLabel { get; }

    /// <summary>Severity chip data - previously baked into <see cref="DisplayLabel"/> as a text suffix.</summary>
    public HealthSeverity Severity { get; }

    public string SeverityLabel { get; }

    /// <summary>
    /// A row the user already dismissed, shown in its section's "Dismissed" sub-group
    /// (docs/superpowers/specs/2026-09-28-library-health-dismissed-rows-design.md) - offers Restore in
    /// place of Dismiss and a neutral chip in place of the severity chip.
    /// </summary>
    public bool IsDismissed => _onRestore is not null;

    public bool IsError => !IsDismissed && Severity == HealthSeverity.Error;

    public bool IsWarning => !IsDismissed && Severity == HealthSeverity.Warning;

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

    [RelayCommand]
    private void Restore()
    {
        DeleteConfirm.Cancel();
        _onRestore?.Invoke(this);
    }
}
