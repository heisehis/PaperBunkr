using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Report + Restart/Exit/Continue dialog shown from <see cref="DiagnosticsService"/>'s unhandled-
/// exception hooks. A plain <see cref="Window"/>, not this app's usual borderless-overlay pattern
/// (MigrationOverlay/ReadingListPropertiesOverlay/etc.) - those render inside MainWindow's own
/// visual tree, which may itself be the thing that just crashed. See docs/superpowers/specs/
/// 2026-08-23-app-chrome-crash-reporter-and-tray-design.md §2.
/// </summary>
public partial class CrashReportWindow : Window
{
    private readonly string _report;

    public CrashOutcome Outcome { get; private set; } = CrashOutcome.Exit;

    public CrashReportWindow() : this(string.Empty, allowContinue: false)
    {
    }

    public CrashReportWindow(string report, bool allowContinue)
    {
        InitializeComponent();
        _report = report;
        ReportTextBox.Text = report;
        ContinueButton.IsVisible = allowContinue;
    }

    private async void OnCopyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(_report);
        }
    }

    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Crash Report",
            SuggestedFileName = $"paperbunkr-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            DefaultExtension = "log",
            FileTypeChoices = new[] { new FilePickerFileType("Log files") { Patterns = new[] { "*.log" } } },
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(_report);
        }
    }

    private void OnRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Outcome = CrashOutcome.Restart;
        Close();
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Outcome = CrashOutcome.Exit;
        Close();
    }

    private void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Outcome = CrashOutcome.Continue;
        Close();
    }

    /// <summary>
    /// Shows this dialog modally via a nested <see cref="DispatcherFrame"/> and blocks the calling
    /// thread (which must already be the UI thread) until it closes - deliberately not
    /// <c>ShowDialog</c>/<c>await</c>, since the callers here are synchronous exception-handler
    /// event methods that must have the user's choice in hand before they return (the CLR
    /// terminates the process the instant an <c>AppDomain.UnhandledException</c> handler returns,
    /// so there is no "come back to this later" option). <see cref="Dispatcher.PushFrame"/> is
    /// explicitly designed to nest like this - it keeps pumping dispatcher jobs (including this
    /// window's own rendering and button clicks) until <see cref="DispatcherFrame.Continue"/> is
    /// set false, regardless of whether it's called from within another dispatcher callback.
    /// </summary>
    public static CrashOutcome ShowModal(string report, bool allowContinue)
    {
        var window = new CrashReportWindow(report, allowContinue);
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        window.Show();
        Dispatcher.UIThread.PushFrame(frame);
        return window.Outcome;
    }
}
