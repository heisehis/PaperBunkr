using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Reader;
using System.Linq;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The reader's slide-in info panel (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #28): a read-only view of the open issue's metadata. The summary is collapsed until asked for
/// (a summary can spoil), unless the Preferences default says otherwise. Owned by <see cref="ReaderScreenViewModel"/>, which fills it on load.
/// </summary>
public sealed partial class ReaderInfoPanelViewModel : ObservableObject
{
    public System.Action<Paperbunkr.Data.Entities.ComicMetadataEntityKind, string>? OpenEntity { get; set; }
    public System.Collections.Generic.IReadOnlyList<TagPillViewModel> CharacterLinks => Links(Model?.Characters, Paperbunkr.Data.Entities.ComicMetadataEntityKind.Character);
    public System.Collections.Generic.IReadOnlyList<TagPillViewModel> TeamLinks => Links(Model?.Teams, Paperbunkr.Data.Entities.ComicMetadataEntityKind.Team);
    public System.Collections.Generic.IReadOnlyList<TagPillViewModel> LocationLinks => Links(Model?.Locations, Paperbunkr.Data.Entities.ComicMetadataEntityKind.Location);
    public System.Collections.Generic.IReadOnlyList<ReaderCreditLinks> CreditLinks => (Model?.Credits ?? System.Array.Empty<InfoCredit>())
        .Select(c => new ReaderCreditLinks(c.Role, Links(ReaderInfoBuilder.Split(c.Names), Paperbunkr.Data.Entities.ComicMetadataEntityKind.Creator))).ToList();
    private System.Collections.Generic.IReadOnlyList<TagPillViewModel> Links(System.Collections.Generic.IReadOnlyList<string>? names, Paperbunkr.Data.Entities.ComicMetadataEntityKind kind) =>
        (names ?? System.Array.Empty<string>()).Select(name => new TagPillViewModel(name, null, Paperbunkr.Data.Entities.IssueTagWeight.Unset,
            value => OpenEntity?.Invoke(kind, value), null)).ToList();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModel))]
    private ReaderInfoModel? _model;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The summary is showing. Starts as the Preferences default for each issue opened; "Show summary" sets it for the rest of the visit to this issue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummaryCollapsed))]
    private bool _isSummaryExpanded;

    public bool HasModel => Model is not null;

    /// <summary>There is a summary and it is still hidden: the "Show summary" button shows.</summary>
    public bool IsSummaryCollapsed => Model is { HasSummary: true } && !IsSummaryExpanded;

    /// <summary>Replaces what the panel shows (a fresh issue) and applies the summary default.</summary>
    public void Set(ReaderInfoModel? model, bool showSummaryByDefault)
    {
        Model = model;
        IsSummaryExpanded = showSummaryByDefault;
        OnPropertyChanged(nameof(IsSummaryCollapsed));
        OnPropertyChanged(nameof(CharacterLinks)); OnPropertyChanged(nameof(TeamLinks)); OnPropertyChanged(nameof(LocationLinks)); OnPropertyChanged(nameof(CreditLinks));
    }

    [RelayCommand]
    private void ShowSummary() => IsSummaryExpanded = true;

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    public void Close() => IsOpen = false;
}

public sealed record ReaderCreditLinks(string Role, System.Collections.Generic.IReadOnlyList<TagPillViewModel> People);
