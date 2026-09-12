using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    private PendingModal? _current;
    private readonly Queue<PendingModal> _queue = new();

    /// <summary>
    /// Builds the resolve callback first and hands it to <paramref name="contentFactory"/>, so the
    /// plugin's own ViewModel constructor can capture it and invoke it directly from whichever
    /// command/button should close the dialog - see <c>INativePluginUiEnvironment.ShowModalAsync</c>'s
    /// own doc comment for why this is a factory rather than an already-built <see cref="Control"/>.
    /// Dismissing via the shell's own scrim/close (no plugin-supplied result) cancels the returned
    /// task, matching <see cref="ConfirmDialogViewModel"/>'s "-1/dismissed" convention's spirit -
    /// callers awaiting this should expect a possible <see cref="System.Threading.Tasks.TaskCanceledException"/>.
    /// </summary>
    internal Task<TResult> ShowAsync<TResult>(Func<Action<TResult>, Control> contentFactory)
    {
        var completion = new TaskCompletionSource<TResult>();
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

        return completion.Task;
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
        IsOpen = false;
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
