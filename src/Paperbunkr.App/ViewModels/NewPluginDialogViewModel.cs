using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "New plugin…" (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A3): name, key (derived from the name
/// until the user edits it), and template. Create writes the folder through <see cref="PluginScaffolder"/> and resolves with the result;
/// a refusal (empty name, key already taken) stays open with the reason shown.
/// </summary>
public sealed partial class NewPluginDialogViewModel : ViewModelBase
{
    private readonly string _root;
    private readonly string _appDirectory;
    private readonly Action<PluginScaffoldResult?> _resolve;
    private bool _keyEdited;
    private bool _settingKey;

    public NewPluginDialogViewModel(string root, string appDirectory, Action<PluginScaffoldResult?> resolve)
    {
        _root = root;
        _appDirectory = appDirectory;
        _resolve = resolve;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScriptCSharp), nameof(IsScriptPython), nameof(IsNative), nameof(TemplateNote))]
    private PluginTemplate _template = PluginTemplate.ScriptCSharp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    public bool IsScriptCSharp => Template == PluginTemplate.ScriptCSharp;

    public bool IsScriptPython => Template == PluginTemplate.ScriptPython;

    public bool IsNative => Template == PluginTemplate.Native;

    public string TemplateNote => Template switch
    {
        PluginTemplate.ScriptCSharp => "A C# script (.csx). It runs as soon as it's saved - developer mode reloads it on every change.",
        PluginTemplate.ScriptPython => "A Python script (.py). It runs as soon as it's saved - developer mode reloads it on every change.",
        _ => "A compiled plugin with full access to your library. Build it with the .NET SDK, then restart Paperbunkr to load it.",
    };

    partial void OnNameChanged(string value)
    {
        if (!_keyEdited)
        {
            _settingKey = true;
            Key = PluginScaffolder.KeyFor(value);
            _settingKey = false;
        }

        Error = null;
    }

    partial void OnKeyChanged(string value)
    {
        if (!_settingKey)
        {
            _keyEdited = true;
        }

        Error = null;
    }

    [RelayCommand]
    private void ChooseTemplate(PluginTemplate template) => Template = template;

    [RelayCommand]
    private void Create()
    {
        var result = PluginScaffolder.Create(_root, Name, Key, Template, _appDirectory);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return;
        }

        _resolve(result);
    }

    [RelayCommand]
    private void Cancel() => _resolve(null);
}
