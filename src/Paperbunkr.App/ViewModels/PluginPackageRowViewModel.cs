using System;
using cYo.Projects.ComicRack.Engine;

namespace Paperbunkr.App.ViewModels;

/// <summary>One installed plugin package row (the CE-style "Packages" panel, as distinct from the command list grouped by hook below it) - Name/Version/Author display plus the shared <see cref="TwoStepConfirm"/> delete affordance (docs/superpowers/specs/2026-08-22-delete-functionality-design.md), matching every other real-data delete in this app rather than a modal confirm dialog.</summary>
public sealed class PluginPackageRowViewModel
{
    private readonly PackageManager.Package _package;

    public PluginPackageRowViewModel(PackageManager.Package package, Action onRemoved)
    {
        _package = package;
        DeleteConfirm = new TwoStepConfirm(onRemoved, idleLabel: "Remove", armedLabel: "Confirm remove?");
    }

    public PackageManager.Package Package => _package;

    public string Name => _package.Name;

    public string? Description => string.IsNullOrEmpty(_package.Description) ? null : _package.Description;

    public bool HasDescription => Description is not null;

    public string? Version => string.IsNullOrEmpty(_package.Version) ? null : _package.Version;

    public string? Author => string.IsNullOrEmpty(_package.Author) ? null : _package.Author;

    /// <summary>Full-trust, not audited-write, tier (docs/superpowers/specs/2026-09-11-plugin-api-v4-
    /// native-tier-design.md §2) - drives the Plugin screen's "Full read/write access to your library
    /// database" notice, separate from and in addition to the per-command `confirmWrites` flag, which
    /// only ever applies to the audited `.csx` tier.</summary>
    public bool IsNativeTier => _package.IsNativeTier;

    /// <summary>True while an install/uninstall is staged but not yet applied (v4 §4's restart-to-
    /// apply model - always true for a Native-tier package right after Install/Uninstall, and false
    /// again once <see cref="Plugins.PluginPackageService.ApplyPendingChanges"/> runs at next launch).
    /// A Script-tier package never reaches this state - it always commits immediately.</summary>
    public bool IsPending => _package.PackageType is PackageManager.PackageType.PendingInstall or PackageManager.PackageType.PendingRemove;

    public TwoStepConfirm DeleteConfirm { get; }
}
