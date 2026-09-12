using System;
using cYo.Projects.ComicRack.Engine;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One installed plugin's sidebar row in the master-detail Plugins screen (docs/superpowers/specs/
/// 2026-09-12-plugin-management-screen-redesign-design.md §4.4/§4.5) - identity, health, and a
/// select action only. What used to live here (Description, the inline <see cref="TwoStepConfirm"/>
/// delete affordance) moved to <see cref="PluginPackageDetailViewModel"/>, since those are properties
/// of the *selected* package, not something every sidebar row needs to carry.
/// </summary>
public sealed partial class PluginPackageRowViewModel : ViewModelBase
{
    private readonly PackageManager.Package _package;
    private readonly Action<PluginPackageRowViewModel> _onSelect;

    public PluginPackageRowViewModel(PackageManager.Package package, bool isBroken, Action<PluginPackageRowViewModel> onSelect)
    {
        _package = package;
        IsBroken = isBroken;
        _onSelect = onSelect;
    }

    public PackageManager.Package Package => _package;

    public string Name => _package.Name;

    public string? Version => string.IsNullOrEmpty(_package.Version) ? null : _package.Version;

    /// <summary>Full-trust, not audited-write, tier (docs/superpowers/specs/2026-09-11-plugin-api-v4-
    /// native-tier-design.md §2) - drives the sidebar/detail's "Full read/write access to your library
    /// database" notice.</summary>
    public bool IsNativeTier => _package.IsNativeTier;

    /// <summary>True while an install/uninstall is staged but not yet applied (v4 §4's restart-to-
    /// apply model). A Script-tier package never reaches this state - it always commits immediately.</summary>
    public bool IsPending => _package.PackageType is PackageManager.PackageType.PendingInstall or PackageManager.PackageType.PendingRemove;

    /// <summary>Health dot (docs §4.3) - true when this package's native load failed, or every one
    /// of its own commands is broken while other packages in the same discovery pass produced
    /// healthy commands. Computed once by <see cref="PluginScreenViewModel.Refresh"/> and passed in,
    /// not recomputed here - a fresh row is built on every refresh anyway.</summary>
    public bool IsBroken { get; }

    [RelayCommand]
    private void Select() => _onSelect(this);
}
