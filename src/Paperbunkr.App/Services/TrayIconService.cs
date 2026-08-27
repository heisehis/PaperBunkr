using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Paperbunkr.App.Services;

/// <summary>
/// Thin wrapper around Avalonia's <see cref="TrayIcon"/> for minimize-to-tray (docs/superpowers/
/// specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §4), following this codebase's
/// existing "thin native-wrapping service, no direct unit tests" precedent (e.g.
/// <c>FilePickerService</c>) - callers (<see cref="Views.MainWindow"/>) decide when to show/hide
/// based on the persisted <c>AppSettings.MinimizeToTray</c> preference; this class owns only the
/// tray icon itself and its two actions (Restore/Exit).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon _trayIcon;

    public event Action? RestoreRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Paperbunkr.App/Assets/paperbunkr.ico"))),
            ToolTipText = "Paperbunkr",
            IsVisible = false,
        };
        _trayIcon.Clicked += (_, _) => RestoreRequested?.Invoke();

        var restoreItem = new NativeMenuItem("Restore");
        restoreItem.Click += (_, _) => RestoreRequested?.Invoke();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _trayIcon.Menu = new NativeMenu { Items = { restoreItem, exitItem } };
    }

    public bool IsVisible
    {
        get => _trayIcon.IsVisible;
        set => _trayIcon.IsVisible = value;
    }

    public void Dispose() => _trayIcon.Dispose();
}
