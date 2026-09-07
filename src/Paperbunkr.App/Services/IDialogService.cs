using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services;

/// <summary>
/// One shared blocking confirm/prompt dialog (docs/superpowers/specs/2026-09-06-feedback-
/// notification-system-design.md §2), rendered inside a single <see cref="Controls.OverlayShell"/>
/// instance rather than a one-off overlay per feature. Absorbs <c>PluginQuestionDialog</c> (see
/// <c>Plugins/PaperbunkrApplication.AskQuestion</c>) and serves as the mechanism for any future
/// confirm dialog, e.g. Library Health's proposed "Remove All Confirmed Missing" prompt.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows the dialog and awaits the user's answer: 0 = primary, 1 = secondary
    /// (only reachable when <see cref="ConfirmDialogRequest.SecondaryLabel"/> is set), -1 =
    /// dismissed (scrim-click, when the primary/secondary buttons weren't the ones clicked).</summary>
    Task<int> ShowAsync(ConfirmDialogRequest request);

    /// <summary>Convenience wrapper for the common yes/no case: 0 (primary) maps to true, anything
    /// else (secondary or dismissed) maps to false.</summary>
    Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
        string cancelLabel = "Cancel", bool isDestructive = false);
}

/// <summary>
/// <paramref name="Items"/>, when non-null/non-empty, renders as a scrollable list beneath
/// <paramref name="Message"/> - the shape Library Health's bulk-delete confirmation needs (a list
/// of affected series/issue/paths), which a plain title+message dialog (the old
/// <c>PluginQuestionDialog</c>'s only shape) can't cover.
/// <paramref name="SecondaryLabel"/> = null collapses the dialog to a single-button prompt,
/// matching <c>PluginQuestionDialog.ShowModal</c>'s existing empty-<c>optionText</c> behavior.
/// </summary>
public sealed record ConfirmDialogRequest(
    string Message,
    string? Title = null,
    IReadOnlyList<string>? Items = null,
    string PrimaryLabel = "Confirm",
    string? SecondaryLabel = "Cancel",
    bool IsDestructive = false);
