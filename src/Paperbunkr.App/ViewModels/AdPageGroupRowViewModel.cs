using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One page in an <see cref="AdPageGroupRowViewModel"/> (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §5): the proposed page with its own Accept / Reject.
/// </summary>
public partial class AdPageProposalRowViewModel : ViewModelBase
{
    private readonly Action<AdPageProposalRowViewModel> _onAccept;
    private readonly Action<AdPageProposalRowViewModel> _onReject;

    public AdPageProposalRowViewModel(
        int proposalId,
        int issueId,
        int pageNumber,
        string label,
        int distance,
        Action<AdPageProposalRowViewModel> onAccept,
        Action<AdPageProposalRowViewModel> onReject)
    {
        ProposalId = proposalId;
        IssueId = issueId;
        PageNumber = pageNumber;
        Label = label;
        Distance = distance;
        _onAccept = onAccept;
        _onReject = onReject;
    }

    public int ProposalId { get; }

    public int IssueId { get; }

    /// <summary>0-based, as stored.</summary>
    public int PageNumber { get; }

    /// <summary>"Series #N · page P" (page is 1-based for display).</summary>
    public string Label { get; }

    /// <summary>Hamming distance to the matched ad; 0 is an exact match.</summary>
    public int Distance { get; }

    public string MatchLabel => Distance == 0 ? "Exact match" : $"{Distance} bit{(Distance == 1 ? string.Empty : "s")} off";

    [RelayCommand]
    private void Accept() => _onAccept(this);

    [RelayCommand]
    private void Reject() => _onReject(this);
}

/// <summary>
/// One Needs Review "Advertisement pages" group: every pending proposal that matched the same ad in the ad library,
/// with the ad's thumbnail, the match count, Accept all / Reject all, and the single pages under an expander
/// (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5). Nothing is applied until the user
/// accepts.
/// </summary>
public partial class AdPageGroupRowViewModel : ViewModelBase
{
    private readonly Action<AdPageGroupRowViewModel> _onAcceptAll;
    private readonly Action<AdPageGroupRowViewModel> _onRejectAll;

    public AdPageGroupRowViewModel(
        int? adHashId,
        string sourceLabel,
        IEnumerable<AdPageProposalRowViewModel> pages,
        Action<AdPageGroupRowViewModel> onAcceptAll,
        Action<AdPageGroupRowViewModel> onRejectAll)
    {
        AdHashId = adHashId;
        SourceLabel = sourceLabel;
        Pages = new ObservableCollection<AdPageProposalRowViewModel>(pages);
        _onAcceptAll = onAcceptAll;
        _onRejectAll = onRejectAll;
    }

    public int? AdHashId { get; }

    /// <summary>Where the ad in the library came from ("Series #N · page P"), or a note when its source issue is gone.</summary>
    public string SourceLabel { get; }

    public ObservableCollection<AdPageProposalRowViewModel> Pages { get; }

    public int MatchCount => Pages.Count;

    public string CountLabel => MatchCount == 1 ? "1 page matches this ad" : $"{MatchCount} pages match this ad";

    public IReadOnlyList<int> ProposalIds => Pages.Select(p => p.ProposalId).ToList();

    /// <summary>The ad's page image, loaded off the UI thread after the section is built; null until (or unless) it loads.</summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void AcceptAll() => _onAcceptAll(this);

    [RelayCommand]
    private void RejectAll() => _onRejectAll(this);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
