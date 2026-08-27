using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Runtime state for one series bulk-edit field row (docs/superpowers/specs/2026-08-24-library-
/// multiselect-slice3-design.md), one instance per <see cref="SeriesBulkFieldDescriptor"/> per edit
/// session - same auto-stage-on-edit shape as <see cref="BulkFieldViewModel"/>, trimmed to only the
/// Text/Enum kinds Series fields actually use (no rating stars, no token-insert - Series has no
/// field needing either).
/// </summary>
public partial class SeriesBulkFieldViewModel : ObservableObject
{
    public SeriesBulkFieldDescriptor Descriptor { get; }

    public SeriesBulkFieldViewModel(SeriesBulkFieldDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public string Label => Descriptor.Label;
    public string? Caveat => Descriptor.Caveat;
    public bool HasCaveat => Descriptor.Caveat is not null;
    public bool IsTextKind => Descriptor.Kind == FieldKind.Text;
    public bool IsEnumKind => Descriptor.Kind == FieldKind.Enum;

    /// <summary>Candidate values for an <see cref="IsEnumKind"/> row's flyout.</summary>
    public IReadOnlyList<string> Options => Descriptor.Options ?? [];

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isStaged;

    /// <summary>Auto-stage on any edit - matches <see cref="BulkFieldViewModel"/>'s own behavior.</summary>
    partial void OnValueChanged(string value) => IsStaged = true;

    /// <summary>Invoked by an <see cref="IsEnumKind"/> row's flyout options.</summary>
    [RelayCommand] private void SetValue(string value) => Value = value;
}
