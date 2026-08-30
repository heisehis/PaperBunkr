using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shown from <c>App.axaml.cs</c> before <c>MainWindow</c> exists when
/// <see cref="DatabaseIntegrityService.CheckIntegrity"/> fails at startup - offers Restore/Start
/// Fresh/Quit. Same "blocking modal via a nested DispatcherFrame, shown before Avalonia's normal
/// window lifecycle is running" pattern as <see cref="CrashReportWindow"/>; see
/// docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md §3.
/// </summary>
public partial class DatabaseRecoveryWindow : Window
{
    public DatabaseRecoveryOutcome Outcome { get; private set; } = DatabaseRecoveryOutcome.Quit;

    /// <summary>The full path of the backup selected when <see cref="Outcome"/> is <see cref="DatabaseRecoveryOutcome.Restore"/>.</summary>
    public string? SelectedBackupPath { get; private set; }

    public DatabaseRecoveryWindow() : this(null, System.Array.Empty<string>())
    {
    }

    public DatabaseRecoveryWindow(string? detail, IReadOnlyList<string> backupPaths)
    {
        InitializeComponent();

        DetailTextBlock.Text = string.IsNullOrWhiteSpace(detail)
            ? "SQLite reported a structural problem with the database file."
            : detail;

        var items = backupPaths.Select(p => new BackupListItem(Path.GetFileName(p), p)).ToList();
        BackupsListBox.ItemsSource = items;
        if (items.Count > 0)
        {
            BackupsListBox.SelectedIndex = 0;
        }
        else
        {
            NoBackupsTextBlock.IsVisible = true;
            RestoreButton.IsEnabled = false;
        }
    }

    private void OnRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (BackupsListBox.SelectedItem is not BackupListItem selected)
        {
            return;
        }

        SelectedBackupPath = selected.FullPath;
        Outcome = DatabaseRecoveryOutcome.Restore;
        Close();
    }

    private void OnStartFreshClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Outcome = DatabaseRecoveryOutcome.StartFresh;
        Close();
    }

    private void OnQuitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Outcome = DatabaseRecoveryOutcome.Quit;
        Close();
    }

    /// <summary>Blocks the calling UI thread until the dialog closes - see <see cref="CrashReportWindow.ShowModal"/> for why this shape is required this early in startup.</summary>
    public static (DatabaseRecoveryOutcome Outcome, string? SelectedBackupPath) ShowModal(string? detail, IReadOnlyList<string> backupPaths)
    {
        var window = new DatabaseRecoveryWindow(detail, backupPaths);
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        window.Show();
        Dispatcher.UIThread.PushFrame(frame);
        return (window.Outcome, window.SelectedBackupPath);
    }

    private sealed record BackupListItem(string DisplayName, string FullPath)
    {
        public override string ToString() => DisplayName;
    }
}
