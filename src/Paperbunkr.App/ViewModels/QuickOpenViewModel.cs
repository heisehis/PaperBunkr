using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Ctrl+P command palette (docs/superpowers/specs/2026-09-03-quick-open-command-palette-
/// design.md). Owns the query / result list / selection and the fuzzy re-rank; hands the chosen
/// entry to <c>MainViewModel.ActivateQuickOpenEntry</c> via the <c>activate</c> delegate.
/// </summary>
public partial class QuickOpenViewModel : ViewModelBase
{
    private readonly Action<QuickOpenEntry> _activate;
    private readonly Action _close;
    private readonly QuickOpenService _service;
    private PluginHostService? _pluginHost;

    private IReadOnlyList<QuickOpenEntry> _index = Array.Empty<QuickOpenEntry>();

    public QuickOpenViewModel(Action<QuickOpenEntry> activate, Action close, QuickOpenService? service = null)
    {
        _activate = activate;
        _close = close;
        _service = service ?? new QuickOpenService();
    }

    /// <summary>Called once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> - the host doesn't exist yet when this ViewModel is constructed in <c>MainViewModel</c>'s own constructor.</summary>
    public void AttachHost(PluginHostService host) => _pluginHost = host;

    /// <summary>
    /// Runs a <see cref="QuickOpenKind.PluginCommand"/> entry's underlying command with whatever's
    /// currently typed as the query, returning display-ready text (tags stripped for the Html
    /// variant, per docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5's no-WebView
    /// caveat) or null if nothing could run. <c>MainViewModel.ActivateQuickOpenEntry</c> just shows
    /// whatever comes back as a toast - no generic result-display surface exists yet.
    /// </summary>
    public async Task<(string CommandName, string Text)?> RunPluginCommandAsync(string commandKey)
    {
        var command = _pluginHost?.Engine.AllCommands.FirstOrDefault(c => c.Key == commandKey && (c.Hook == PluginHooks.QuickOpenHtml || c.Hook == PluginHooks.QuickOpenUI));
        if (_pluginHost is null || command?.Environment is null)
        {
            return null;
        }

        var result = await _pluginHost.RunCommandAsync(command, new QuickOpenHookGlobals { Environment = command.Environment, Query = Query }).ConfigureAwait(true);
        if (!result.Success)
        {
            return (command.Name, $"({result.Error?.Message})");
        }

        string text = PluginInfoPanelSample.RenderText(result.ReturnValue as string, isHtml: command.Hook == PluginHooks.QuickOpenHtml);
        return (command.Name, string.IsNullOrEmpty(text) ? "(no result)" : text);
    }

    public ObservableCollection<QuickOpenEntry> Results { get; } = new();

    /// <summary>Raised by <see cref="Open"/> - the overlay code-behind uses this to focus the search box.</summary>
    public event Action? Opened;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    public QuickOpenEntry? SelectedEntry =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public bool HasNoMatches => Query.Length > 0 && Results.Count == 0;

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(SelectedEntry));

    // Re-ranked synchronously on each keystroke - QuickOpenMatcher.Rank is an O(n) in-memory pass
    // over a few thousand short strings (microseconds), so a debounce would add latency for nothing.
    partial void OnQueryChanged(string value) => Rerank();

    /// <summary>Called every time the overlay is opened - rebuilds the index and starts blank on the recency list.</summary>
    public void Open()
    {
        var index = _service.BuildIndex().ToList();

        // Plugin API v2 QuickOpenHtml/QuickOpenUI hooks (docs/superpowers/specs/2026-09-05-plugin-
        // api-v2-remaining-hooks-plan.md §11) - CE's own "QuickOpen" is a recent-books grid with
        // attached info panels, not a text command palette (verified against
        // _reference/ComicRackCE/ComicRack/Views/QuickOpenView.cs); this app already has its own
        // Ctrl+P navigation palette built independently, so a plugin command is added here as a
        // static, fuzzy-findable-by-name entry rather than a live per-keystroke search provider -
        // activating it re-invokes the command with whatever's currently typed as the query.
        if (_pluginHost is not null)
        {
            foreach (var command in _pluginHost.Engine.GetCommands(PluginHooks.QuickOpenHtml).Concat(_pluginHost.Engine.GetCommands(PluginHooks.QuickOpenUI)))
            {
                index.Add(new QuickOpenEntry(QuickOpenKind.PluginCommand, null, command.Name, null, "Apps", null, command.Key));
            }
        }

        _index = index;
        Query = string.Empty;
        Rerank();
        Opened?.Invoke();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    public void ActivateSelected()
    {
        if (SelectedEntry is { } entry)
        {
            _activate(entry);
            _close();
        }
    }

    private void Rerank()
    {
        Results.Clear();
        foreach (var entry in QuickOpenMatcher.Rank(Query, _index))
        {
            Results.Add(entry);
        }

        SelectedIndex = 0;
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasNoMatches));
    }
}
