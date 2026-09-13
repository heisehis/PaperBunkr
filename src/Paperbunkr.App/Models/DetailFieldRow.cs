using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// One label/value row in the Detail screen's Details tab "Additional Details" section
/// (docs/superpowers/specs/2026-09-13-details-tab-credits-and-fields-design.md) - Imprint, Web,
/// Notes, Scan Information, Alternate Series, Series Group, Story Arc Number. Never added to
/// <see cref="ViewModels.DetailTabsViewModel.AdditionalDetails"/> when the field is blank across
/// every issue in the series.
/// </summary>
public partial class DetailFieldRow : ObservableObject
{
    public DetailFieldRow(string label, string value, bool isLink)
    {
        Label = label;
        Value = value;
        IsLink = isLink;
    }

    public string Label { get; }

    public string Value { get; }

    /// <summary>True only when this is the Web field and the series' issues agree on exactly one
    /// URL - a clickable link needs one unambiguous target, so disagreement across issues falls
    /// back to plain (non-clickable) joined text instead.</summary>
    public bool IsLink { get; }

    /// <summary>Opens <see cref="Value"/> in the OS default browser - same <c>Process.Start</c>
    /// pattern this codebase already uses for outbound URLs (e.g. tracker OAuth sign-in in
    /// PreferencesScreenViewModel). Swallows failure silently (no shell/browser available),
    /// matching every existing call site of this pattern.</summary>
    [RelayCommand]
    private void OpenLink()
    {
        if (!IsLink)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = Value, UseShellExecute = true });
        }
        catch
        {
        }
    }
}
