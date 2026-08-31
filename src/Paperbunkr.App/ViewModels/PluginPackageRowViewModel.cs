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

    public TwoStepConfirm DeleteConfirm { get; }
}
