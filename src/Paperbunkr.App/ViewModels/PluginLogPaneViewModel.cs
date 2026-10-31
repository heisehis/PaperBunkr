using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The dev-mode live log in a plugin's detail pane (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md
/// §A2): the last <see cref="MaxLines"/> lines of <c>&lt;key&gt;.log</c>, re-read once a second while <see cref="Start"/>ed (a poll
/// rather than a watcher - the host appends to this file itself, so a watcher would fire on every line). The screen starts it when
/// the pane is shown and stops it when the pane is replaced, so a discarded pane never keeps a timer alive.
/// </summary>
public sealed partial class PluginLogPaneViewModel : ViewModelBase
{
    public const int MaxLines = 200;

    private readonly string _pluginKey;
    private readonly PluginLogFiles _files;
    private readonly IFilePickerService _clipboard;
    private DispatcherTimer? _timer;

    public PluginLogPaneViewModel(string pluginKey, PluginLogFiles files, IFilePickerService clipboard)
    {
        _pluginKey = pluginKey;
        _files = files;
        _clipboard = clipboard;
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _text = string.Empty;

    public bool IsEmpty => Text.Length == 0;

    /// <summary>Re-reads the tail. Only assigns when it changed, so an unchanged file doesn't re-render (or move the scroll).</summary>
    public void Refresh()
    {
        string next = string.Join(Environment.NewLine, _files.ReadTail(_pluginKey, MaxLines));
        if (next != Text)
        {
            Text = next;
        }
    }

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (!IsEmpty)
        {
            await _clipboard.SetClipboardTextAsync(Text);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _files.Clear(_pluginKey);
        Refresh();
    }
}
