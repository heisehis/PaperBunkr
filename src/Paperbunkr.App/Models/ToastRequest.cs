using System.Collections.Generic;
using System.Windows.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// What a toast shows and how it's colored (docs/superpowers/specs/2026-09-06-feedback-
/// notification-system-design.md §5). Every toast in the app funnels through one shape now -
/// <c>MainViewModel.ShowToast</c>'s plain title+message, Activity Center's completion toasts, and
/// the update-ready toast's action buttons all construct one of these.
/// </summary>
public sealed record ToastRequest(
    string Title,
    string? Message = null,
    ToastSeverity Severity = ToastSeverity.Info,
    IReadOnlyList<ToastAction>? Actions = null);

/// <summary>One button on a toast, e.g. the update-ready toast's Restart/Later/What's New.</summary>
public sealed record ToastAction(string Label, ICommand Command);

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error,
}
