using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Plugins;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The settings overlay for a plugin that declares a <c>&lt;Settings&gt;</c> schema (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §6.4) - hosted through the same <c>OpenPluginSettingsAsync</c> path a
/// native plugin's own settings view uses. One <see cref="PluginSettingRowViewModel"/> per declared setting,
/// in declaration order.
/// </summary>
public sealed class PluginSettingsSchemaViewModel : ViewModelBase
{
    public PluginSettingsSchemaViewModel(string pluginKey, string pluginName, PluginSettingsSchema schema, PluginSettingsAccess access)
    {
        Title = $"{pluginName} settings";
        Rows = new ObservableCollection<PluginSettingRowViewModel>(
            schema.Definitions.Select(d => new PluginSettingRowViewModel(pluginKey, d, access)));

        // Said once at the top rather than repeated on every secret row.
        SecretsUnavailableNote = !access.Protector.IsAvailable && schema.Definitions.Any(d => d.Type == PluginSettingType.Secret)
            ? "Secrets can't be stored safely on this platform, so the secret fields below are read-only."
            : null;
    }

    public string Title { get; }

    public ObservableCollection<PluginSettingRowViewModel> Rows { get; }

    public string? SecretsUnavailableNote { get; }

    public bool HasSecretsUnavailableNote => SecretsUnavailableNote is not null;
}

/// <summary>
/// One declared setting in the overlay. Edits save immediately when valid; an invalid edit is refused with an
/// inline message and is <b>not</b> saved. An invalid value <em>already stored</em> is shown as-is and flagged,
/// never rewritten (the plugin meanwhile sees the declared default - see <see cref="PluginSettingsAccess.Get"/>).
/// </summary>
public sealed partial class PluginSettingRowViewModel : ViewModelBase
{
    private readonly string _pluginKey;
    private readonly PluginSettingDefinition _definition;
    private readonly PluginSettingsAccess _access;
    private readonly bool _loading;

    public PluginSettingRowViewModel(string pluginKey, PluginSettingDefinition definition, PluginSettingsAccess access)
    {
        _pluginKey = pluginKey;
        _definition = definition;
        _access = access;
        ChoiceLabels = definition.Choices.Select(c => c.Label).ToList();

        _loading = true;
        string shown = access.GetForEditing(pluginKey, definition, out bool unreadableSecret);
        if (IsToggle)
        {
            _isOn = string.Equals(shown, "true", StringComparison.OrdinalIgnoreCase);
        }
        else if (IsChoice)
        {
            _text = definition.Choices.FirstOrDefault(c => c.Value == shown)?.Label ?? shown;
        }
        else
        {
            _text = shown;
        }

        _loading = false;

        IsEnabled = !(IsSecret && !access.Protector.IsAvailable);
        _error = InitialProblem(access.GetRaw(pluginKey, definition.Key), unreadableSecret);
    }

    public string Key => _definition.Key;

    public string Label => _definition.Label;

    public string? Description => _definition.Description;

    public bool IsToggle => _definition.Type == PluginSettingType.Toggle;

    public bool IsText => _definition.Type == PluginSettingType.Text;

    public bool IsChoice => _definition.Type == PluginSettingType.Choice;

    public bool IsNumber => _definition.Type == PluginSettingType.Number;

    public bool IsSecret => _definition.Type == PluginSettingType.Secret;

    /// <summary>False only for a secret on a platform with no DPAPI - shown, but not editable.</summary>
    public bool IsEnabled { get; }

    /// <summary>The labels offered by a choice setting (its values are what's stored).</summary>
    public IReadOnlyList<string> ChoiceLabels { get; }

    /// <summary>Bounds for the number field's spinner; a number with no declared bound gets a very wide one.</summary>
    public decimal SpinnerMinimum => _definition.Min ?? -1_000_000_000m;

    public decimal SpinnerMaximum => _definition.Max ?? 1_000_000_000m;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    partial void OnTextChanged(string value) => Commit();

    partial void OnIsOnChanged(bool value) => Commit();

    private void Commit()
    {
        if (_loading || !IsEnabled)
        {
            return;
        }

        string? raw;
        if (IsToggle)
        {
            raw = IsOn ? "true" : "false";
        }
        else if (IsChoice)
        {
            raw = _definition.Choices.FirstOrDefault(c => string.Equals(c.Label, Text, StringComparison.Ordinal))?.Value;
            if (raw is null)
            {
                Error = "Pick one of the listed options.";
                return;
            }
        }
        else
        {
            raw = IsNumber ? Text.Trim() : Text;
        }

        string? problem = PluginSettingsSchema.Validate(_definition, raw);
        if (problem is not null)
        {
            Error = problem;
            return;
        }

        try
        {
            _access.Set(_pluginKey, _definition.Key, raw);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    /// <summary>What to flag when the overlay opens: a stored value that violates the schema, or a secret that can't be read back.</summary>
    private string? InitialProblem(string? storedRaw, bool unreadableSecret)
    {
        if (unreadableSecret)
        {
            return "The saved value couldn't be read on this Windows account. Enter it again.";
        }

        if (IsSecret || storedRaw is null)
        {
            return null;
        }

        string? problem = PluginSettingsSchema.Validate(_definition, storedRaw);
        if (problem is null)
        {
            return null;
        }

        string usingDefault = _definition.Default is null ? "no value" : $"the default ('{_definition.Default}')";
        return $"The saved value '{storedRaw}' isn't valid. {problem} The plugin is using {usingDefault} until you change it.";
    }
}
