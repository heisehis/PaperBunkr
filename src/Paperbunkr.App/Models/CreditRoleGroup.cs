using System.Collections.ObjectModel;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>
/// One credit role row on the Detail screen's Details tab (docs/superpowers/specs/2026-09-13-
/// details-tab-credits-and-fields-design.md) - e.g. "Writer" with every distinct writer name
/// across the series as chips. Never added to <see cref="DetailTabsViewModel.CreditRoles"/>
/// when the role has zero non-blank values across the whole series.
/// </summary>
public sealed class CreditRoleGroup
{
    public CreditRoleGroup(string label, ObservableCollection<TagPillViewModel> chips)
    {
        Label = label;
        Chips = chips;
    }

    public string Label { get; }

    public ObservableCollection<TagPillViewModel> Chips { get; }
}
