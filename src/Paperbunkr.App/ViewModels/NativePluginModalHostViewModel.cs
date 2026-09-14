using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Backs the single shared generic modal-host slot native plugins show their own compiled dialogs
/// through (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §3, implementation
/// plan Phase 1 Step 1.5) - one <see cref="Controls.OverlayShell"/> in <c>MainWindow.axaml</c> hosting
/// whatever plugin-supplied <see cref="Control"/> is current, rather than one bespoke overlay per
/// dialog type. Mirrors <see cref="ConfirmDialogViewModel"/>'s queueing shape (a second
/// <see cref="ShowAsync{TResult}"/> call while one is already open is queued, not clobbered), but
/// stores nothing generic on the view model itself - every call's <c>TResult</c> is captured entirely
/// in that call's own closures, since different calls may resolve to different result types.
/// </summary>
public sealed partial class NativePluginModalHostViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private Control? _hostedContent;

    /// <summary>Set only while a <see cref="BeginBatch"/> session is active - rendered above
    /// <see cref="HostedContent"/> inside the same shell (docs/superpowers/specs/2026-09-13-cluster-
    /// scraper-ui-redesign-design.md §4). Persists across whatever number of sequential
    /// <see cref="ShowAsync{TResult}"/> calls the batch's caller makes.</summary>
    [ObservableProperty]
    private Control? _headerContent;

    private bool _batchActive;
    private PendingModal? _current;
    private readonly Queue<PendingModal> _queue = new();

    /// <summary>
    /// Keeps the shell open (and <see cref="HeaderContent"/> mounted) across a sequence of otherwise-
    /// independent <see cref="ShowAsync{TResult}"/> calls, so a caller doing a per-item review loop
    /// (e.g. Cluster Library Manager's scrape batch) can show one persistent progress header instead
    /// of it re-mounting between items. Disposing ends the batch: <see cref="HeaderContent"/> clears,
    /// and if nothing is currently shown or queued the shell closes.
    /// </summary>
    internal IDisposable BeginBatch(Control header)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException($"{nameof(BeginBatch)} must be called from the UI thread.");
        }

        _batchActive = true;
        HeaderContent = header;
        return new BatchScope(this);
    }

    private void EndBatch()
    {
        _batchActive = false;
        HeaderContent = null;
        if (_current is null)
        {
            IsOpen = false;
        }
    }

    private sealed class BatchScope : IDisposable
    {
        private readonly NativePluginModalHostViewModel _owner;
        private bool _disposed;

        public BatchScope(NativePluginModalHostViewModel owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.EndBatch();
        }
    }

    /// <summary>
    /// Builds the resolve callback first and hands it to <paramref name="contentFactory"/>, so the
    /// plugin's own ViewModel constructor can capture it and invoke it directly from whichever
    /// command/button should close the dialog - see <c>INativePluginUiEnvironment.ShowModalAsync</c>'s
    /// own doc comment for why this is a factory rather than an already-built <see cref="Control"/>.
    /// Dismissing via the shell's own scrim/close (no plugin-supplied result) cancels the returned
    /// task, matching <see cref="ConfirmDialogViewModel"/>'s "-1/dismissed" convention's spirit -
    /// callers awaiting this should expect a possible <see cref="System.Threading.Tasks.TaskCanceledException"/>.
    /// </summary>
    /// <summary>
    /// Plugin scrape/organize orchestrators (e.g. <c>ComicVineScrapeOrchestrator.ScrapeAsync</c>)
    /// chain <c>ConfigureAwait(false)</c> through their network/DB calls, so the continuation that
    /// resolves <c>interactiveReview</c> and reaches this method often runs on a thread-pool thread,
    /// not the UI thread. Building the <see cref="Control"/> and setting <see cref="HostedContent"/>/
    /// <see cref="IsOpen"/> off the UI thread gives that control the wrong thread affinity, which
    /// later throws <c>Avalonia.Threading.Dispatcher</c> VerifyAccess errors (non-terminating from a
    /// binding update, terminating from <c>Control.UpdateDataValidation</c>) whenever Avalonia touches
    /// it from the real UI thread. Marshal here - the single entry point every native plugin modal
    /// goes through - instead of fixing every orchestrator's await chain individually.
    /// </summary>
    internal Task<TResult> ShowAsync<TResult>(Func<Action<TResult>, Control> contentFactory)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var onUiThread = new TaskCompletionSource<TResult>();
            Dispatcher.UIThread.Post(() => ShowOnUiThread(contentFactory, onUiThread));
            return onUiThread.Task;
        }

        var completion = new TaskCompletionSource<TResult>();
        ShowOnUiThread(contentFactory, completion);
        return completion.Task;
    }

    private void ShowOnUiThread<TResult>(Func<Action<TResult>, Control> contentFactory, TaskCompletionSource<TResult> completion)
    {
        Control content = contentFactory(result => Complete(completion, result));
        var pending = new PendingModal(content, () => completion.TrySetCanceled());

        if (_current is not null)
        {
            _queue.Enqueue(pending);
        }
        else
        {
            Present(pending);
        }
    }

    private void Present(PendingModal pending)
    {
        _current = pending;
        HostedContent = pending.Content;
        IsOpen = true;
    }

    /// <summary>Bound as OverlayShell's CloseCommand for the shared host's scrim-click.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        _current?.OnDismiss();
        Advance();
    }

    private void Complete<TResult>(TaskCompletionSource<TResult> completion, TResult result)
    {
        completion.TrySetResult(result);
        Advance();
    }

    /// <summary>
    /// Opportunistic, not mandated (docs/superpowers/specs/2026-09-12-plugin-management-screen-
    /// redesign-design.md §4.4) - disposes the outgoing content's <c>DataContext</c> and/or the
    /// content <see cref="Control"/> itself if either implements <see cref="IDisposable"/>, and does
    /// nothing otherwise. No native plugin's settings UI holds anything disposable today (verified:
    /// ClusterLibraryManager's own Settings/ has no Timer/Subscribe/IDisposable at all) - this costs
    /// that plugin nothing and closes the gap for free the moment a future one's settings view model
    /// ever does hold something real, without requiring every <c>INativePluginSettingsUi</c>
    /// implementation to opt into a new mandatory interface member for a need nothing has yet.
    /// </summary>
    private void Advance()
    {
        // While a batch is active, the shell/header stay mounted between consecutive ShowAsync calls
        // (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md §4) - only
        // HostedContent itself clears, HostedContent going briefly null between items rather than the
        // whole shell tearing down and re-mounting with it.
        if (!_batchActive)
        {
            IsOpen = false;
        }

        (_current?.Content.DataContext as IDisposable)?.Dispose();
        (_current?.Content as IDisposable)?.Dispose();
        HostedContent = null;
        _current = null;

        if (_queue.Count > 0)
        {
            Present(_queue.Dequeue());
        }
    }

    private sealed record PendingModal(Control Content, Action OnDismiss);
}
