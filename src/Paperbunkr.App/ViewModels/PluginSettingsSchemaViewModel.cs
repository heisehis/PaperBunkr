using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The settings overlay for a plugin that declares a <c>&lt;Settings&gt;</c> schema (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §6.4) - hosted through the same <c>OpenPluginSettingsAsync</c> path a
/// native plugin's own settings view uses. One <see cref="PluginSettingRowViewModel"/> per declared setting,
/// in declaration order.
/// </summary>
public sealed partial class PluginSettingsSchemaViewModel : ViewModelBase
{
    private readonly string _pluginKey;
    private readonly string _pluginName;
    private readonly PluginSettingsSchema _schema;
    private readonly PluginSettingsAccess _access;
    private readonly IFilePickerService? _filePicker;

    public PluginSettingsSchemaViewModel(string pluginKey, string pluginName, PluginSettingsSchema schema, PluginSettingsAccess access, IFilePickerService? filePicker = null)
    {
        _pluginKey = pluginKey;
        _pluginName = pluginName;
        _schema = schema;
        _access = access;
        _filePicker = filePicker;
        ResetAll = new TwoStepConfirm(ResetAllRows, "Reset all", "Confirm reset all?");
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

    /// <summary>Two-step "Reset all": every unlocked setting goes back to its declared default.</summary>
    public TwoStepConfirm ResetAll { get; }

    /// <summary>What the last reset / export / import did, in one line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    public bool HasStatus => Status is not null;

    // Rows are refreshed in place - never by clearing Rows from inside a click (see the removal-during-event gotcha in CLAUDE.md).
    private void ResetAllRows()
    {
        int reset = 0;
        int skippedLocked = 0;
        foreach (var row in Rows)
        {
            if (row.IsLocked)
            {
                skippedLocked++;
            }
            else if (row.ResetValue())
            {
                reset++;
            }
        }

        Status = (reset, skippedLocked) switch
        {
            (0, 0) => "Nothing to reset - every setting is already at its default.",
            (_, 0) => $"Reset {reset} {(reset == 1 ? "setting" : "settings")} to default.",
            _ => $"Reset {reset} {(reset == 1 ? "setting" : "settings")} to default; {skippedLocked} locked {(skippedLocked == 1 ? "setting was" : "settings were")} skipped.",
        };
    }

    [RelayCommand]
    private async Task Export()
    {
        if (_filePicker is null)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Export plugin settings", $"{_pluginKey}-settings.json", "json", "Plugin settings (.json)");
        if (path is null)
        {
            return;
        }

        try
        {
            string json = PluginSettingsTransfer.Export(_access, _pluginKey, _pluginName, _schema);
            await File.WriteAllTextAsync(path, json);
            int secrets = _schema.Definitions.Count(d => d.Type == PluginSettingType.Secret);
            Status = secrets == 0
                ? "Settings exported."
                : $"Settings exported. {secrets} secret {(secrets == 1 ? "setting was" : "settings were")} left out.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't write the file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (_filePicker is null)
        {
            return;
        }

        string? path = await _filePicker.PickOpenFileAsync("Import plugin settings", "json", "Plugin settings (.json)");
        if (path is null)
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't read the file: {ex.Message}";
            return;
        }

        var result = PluginSettingsTransfer.Import(_access, _pluginKey, _schema, json);
        Status = result.Describe();
        if (result.Succeeded)
        {
            foreach (var row in Rows)
            {
                row.Refresh();
            }
        }
    }
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
    private bool _loading;

    public PluginSettingRowViewModel(string pluginKey, PluginSettingDefinition definition, PluginSettingsAccess access)
    {
        _pluginKey = pluginKey;
        _definition = definition;
        _access = access;
        ChoiceLabels = definition.Choices.Select(c => c.Label).ToList();

        _platformAllowsEditing = !(IsSecret && !access.Protector.IsAvailable);

        // Locked once when the overlay opens (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md section 4), so a
        // locked setting doesn't lock itself mid-typing the first time a value is saved. Only Unlock changes it afterwards.
        _isLocked = access.IsLocked(pluginKey, definition);
        Unlock = new TwoStepConfirm(() => IsLocked = false, "Unlock", "Confirm unlock?");
        Load();
    }

    /// <summary>Re-reads the stored value into the editor in place (after a reset or an import).</summary>
    public void Refresh() => Load();

    private void Load()
    {
        _loading = true;
        string shown = _access.GetForEditing(_pluginKey, _definition, out bool unreadableSecret);
        if (IsToggle)
        {
            IsOn = string.Equals(shown, "true", StringComparison.OrdinalIgnoreCase);
        }
        else if (IsChoice)
        {
            Text = _definition.Choices.FirstOrDefault(c => c.Value == shown)?.Label ?? shown;
        }
        else
        {
            Text = shown;
        }

        _loading = false;

        string? raw = _access.GetRaw(_pluginKey, _definition.Key);
        HasStored = raw is not null;
        Error = InitialProblem(raw, unreadableSecret);
    }

    /// <summary>Removes the stored value so the setting falls back to its default. False when locked or when nothing was stored.</summary>
    public bool ResetValue()
    {
        if (IsLocked || !_access.Reset(_pluginKey, _definition.Key))
        {
            return false;
        }

        Load();
        return true;
    }

    [RelayCommand]
    private void Reset() => ResetValue();

    public string Key => _definition.Key;

    public string Label => _definition.Label;

    public string? Description => _definition.Description;

    public bool IsToggle => _definition.Type == PluginSettingType.Toggle;

    public bool IsText => _definition.Type == PluginSettingType.Text;

    public bool IsChoice => _definition.Type == PluginSettingType.Choice;

    public bool IsNumber => _definition.Type == PluginSettingType.Number;

    public bool IsSecret => _definition.Type == PluginSettingType.Secret;

    private readonly bool _platformAllowsEditing;

    /// <summary>False for a secret on a platform with no DPAPI, and while the setting is locked - shown, but not editable.</summary>
    public bool IsEnabled => _platformAllowsEditing && !IsLocked;

    /// <summary>Set when the overlay opened with a value stored for a <c>locked</c> setting; cleared only by <see cref="Unlock"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled), nameof(CanReset))]
    private bool _isLocked;

    /// <summary>Two-step "Unlock" for this overlay session only; nothing about it is persisted.</summary>
    public TwoStepConfirm Unlock { get; }

    /// <summary>True when a value is stored (so there is something to reset).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    private bool _hasStored;

    public bool CanReset => HasStored && !IsLocked;

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
            HasStored = true;
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
