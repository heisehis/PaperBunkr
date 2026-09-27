using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One Library Health "Reported pages" row (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-
/// design.md §4): a page the reader flagged as bad, with three actions - open it in the reader, tag it Deleted
/// (so page skipping passes over it), or dismiss the report. Reports are never resolved automatically.
/// </summary>
public partial class PageReportRowViewModel : ViewModelBase
{
    private readonly Action<PageReportRowViewModel> _onOpen;
    private readonly Action<PageReportRowViewModel> _onTagDeleted;
    private readonly Action<PageReportRowViewModel> _onDismiss;

    public PageReportRowViewModel(
        int reportId,
        int issueId,
        int pageNumber,
        string displayLabel,
        PageReportReason reason,
        Action<PageReportRowViewModel> onOpen,
        Action<PageReportRowViewModel> onTagDeleted,
        Action<PageReportRowViewModel> onDismiss)
    {
        ReportId = reportId;
        IssueId = issueId;
        PageNumber = pageNumber;
        DisplayLabel = displayLabel;
        ReasonLabel = LabelFor(reason);
        _onOpen = onOpen;
        _onTagDeleted = onTagDeleted;
        _onDismiss = onDismiss;
    }

    public int ReportId { get; }

    public int IssueId { get; }

    /// <summary>0-based, as stored.</summary>
    public int PageNumber { get; }

    /// <summary>"Series #N · page P" (page is 1-based for display).</summary>
    public string DisplayLabel { get; }

    public string ReasonLabel { get; }

    public static string LabelFor(PageReportReason reason) => reason switch
    {
        PageReportReason.Corrupt => "Corrupt",
        PageReportReason.Blank => "Blank",
        PageReportReason.LowRes => "Low resolution",
        _ => "Other",
    };

    [RelayCommand]
    private void Open() => _onOpen(this);

    [RelayCommand]
    private void TagDeleted() => _onTagDeleted(this);

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
