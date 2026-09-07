using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Backs the single shared <see cref="Views.ConfirmDialogView"/> instance, hosted inside its own
/// <see cref="Controls.OverlayShell"/> in <c>MainWindow.axaml</c>
/// (docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md §2). Driven
/// exclusively through <see cref="DialogService"/> - nothing else should set these properties
/// directly.
///
/// A second <see cref="ShowAsync"/> call while one is already pending is queued rather than
/// clobbering the in-flight request's content (or orphaning its awaiter forever) - concurrent
/// confirms are rare (a plugin's <c>AskQuestion</c> firing while a core-app confirm is also
/// pending, say) but losing an awaited answer silently would be worse than a short queue.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string>? _items;

    [ObservableProperty]
    private string _primaryLabel = "Confirm";

    [ObservableProperty]
    private string? _secondaryLabel = "Cancel";

    [ObservableProperty]
    private bool _isDestructive;

    private TaskCompletionSource<int>? _pending;
    private readonly Queue<(ConfirmDialogRequest Request, TaskCompletionSource<int> Completion)> _queue = new();

    public bool HasItems => Items is { Count: > 0 };
    public bool HasSecondary => SecondaryLabel is not null;

    partial void OnItemsChanged(IReadOnlyList<string>? value) => OnPropertyChanged(nameof(HasItems));
    partial void OnSecondaryLabelChanged(string? value) => OnPropertyChanged(nameof(HasSecondary));

    internal Task<int> ShowAsync(ConfirmDialogRequest request)
    {
        var completion = new TaskCompletionSource<int>();

        if (_pending is not null)
        {
            _queue.Enqueue((request, completion));
            return completion.Task;
        }

        Present(request, completion);
        return completion.Task;
    }

    private void Present(ConfirmDialogRequest request, TaskCompletionSource<int> completion)
    {
        Title = request.Title;
        Message = request.Message;
        Items = request.Items;
        PrimaryLabel = request.PrimaryLabel;
        SecondaryLabel = request.SecondaryLabel;
        IsDestructive = request.IsDestructive;
        _pending = completion;
        IsOpen = true;
    }

    [RelayCommand]
    private void Primary() => Resolve(0);

    [RelayCommand]
    private void Secondary() => Resolve(1);

    /// <summary>Bound as OverlayShell's CloseCommand for the shared dialog's scrim-click.</summary>
    [RelayCommand]
    private void Dismiss() => Resolve(-1);

    private void Resolve(int answer)
    {
        IsOpen = false;
        var completion = _pending;
        _pending = null;
        completion?.TrySetResult(answer);

        if (_queue.Count > 0)
        {
            var (nextRequest, nextCompletion) = _queue.Dequeue();
            Present(nextRequest, nextCompletion);
        }
    }
}
